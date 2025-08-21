using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UdpUnicast
{
    public class UdpManager : IDisposable
    {
        public event Action<string>? LogMessage;
        public event Action<IPEndPoint>? ListenerStarted;
        public event Action? ListenerStopped;
        public event Action<byte[], IPEndPoint>? MessageReceived;
        public event Action<string>? PeriodicSendStatusChanged;

        private UdpClient? _udpClient;
        private CancellationTokenSource? _listenerCts;
        private CancellationTokenSource? _periodicSendCts;
        private int _messageCounter = 0;
        private int _periodicSendCount = 0;

        public int LastGlobalSentMessageCounter { get; private set; } = -1;
        public ConcurrentDictionary<int, DateTime> SentMessageTimestamps { get; } = new();

        public void StartListener(IPAddress ipAddress)
        {
            try
            {
                _udpClient = new UdpClient(new IPEndPoint(ipAddress, 0));
                _listenerCts = new CancellationTokenSource();
                Task.Run(() => ListenForMessages(_listenerCts.Token));
                ListenerStarted?.Invoke((IPEndPoint)_udpClient.Client.LocalEndPoint!);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Listener start failed: {ex.Message}");
                _udpClient?.Dispose();
                _udpClient = null;
            }
        }

        public void StopListener()
        {
            StopPeriodicSend();
            _listenerCts?.Cancel();
            _udpClient?.Close();
            _udpClient = null;
            ListenerStopped?.Invoke();
        }

        private async Task ListenForMessages(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_udpClient == null) break;
                    var receivedResult = await _udpClient.ReceiveAsync(token);
                    MessageReceived?.Invoke(receivedResult.Buffer, receivedResult.RemoteEndPoint);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Listen error: {ex.Message}");
                }
            }
        }

        public async Task SendSingleMessageAsync(IPEndPoint targetEndPoint, bool enableBroadcast, string message)
        {
            if (_udpClient == null) return;
            try
            {
                _udpClient.EnableBroadcast = enableBroadcast;
                var fullMessage = $"<FTEST,{_messageCounter},{message}>";
                byte[] bytesToSend = Encoding.UTF8.GetBytes(fullMessage);

                LastGlobalSentMessageCounter = _messageCounter;
                SentMessageTimestamps[_messageCounter] = DateTime.UtcNow;

                await _udpClient.SendAsync(bytesToSend, targetEndPoint);
                LogMessage?.Invoke($"Discovery message sent with Seq={_messageCounter}");
                _messageCounter++;
                if (_messageCounter > 65535) _messageCounter = 1;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error sending: {ex.Message}");
            }
        }

        public void StartPeriodicSend(IPEndPoint targetEndPoint, bool enableBroadcast, int interval, int dummySize, int sendLimit, bool isContinuous)
        {
            if (_udpClient == null) return;

            _messageCounter = 0;
            _periodicSendCount = 0;
            _periodicSendCts = new CancellationTokenSource();
            var localCts = _periodicSendCts;

            Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
                try
                {
                    while (await timer.WaitForNextTickAsync(localCts.Token))
                    {
                        if (!isContinuous && _periodicSendCount >= sendLimit)
                        {
                            LogMessage?.Invoke($"Periodic send limit ({sendLimit}) reached. Stopping.");
                            PeriodicSendStatusChanged?.Invoke("Stop");
                            break;
                        }

                        var dummyData = new string('X', dummySize);
                        var message = $"<FTEST,{_messageCounter},{dummyData}>";
                        byte[] bytesToSend = Encoding.UTF8.GetBytes(message);

                        LastGlobalSentMessageCounter = _messageCounter;
                        SentMessageTimestamps[_messageCounter] = DateTime.UtcNow;
                        
                        _udpClient.EnableBroadcast = enableBroadcast;
                        await _udpClient.SendAsync(bytesToSend, targetEndPoint);
                        LogMessage?.Invoke($"Sent: <FTEST,{_messageCounter},...>");
                        _messageCounter++;
                        _periodicSendCount++;
                        if (_messageCounter > 65535) _messageCounter = 1;
                    }
                }
                catch (OperationCanceledException) { /* Normal stop */ }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Periodic send error: {ex.Message}");
                    PeriodicSendStatusChanged?.Invoke("Stop");
                }
            });

            PeriodicSendStatusChanged?.Invoke("Start");
        }

        public void StopPeriodicSend()
        {
            _periodicSendCts?.Cancel();
            _periodicSendCts = null;
            PeriodicSendStatusChanged?.Invoke("Stop");
        }

        public void Dispose()
        {
            StopListener();
        }
    }
}
