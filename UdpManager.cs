
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UdpUnicast
{

    /// <summary>
    /// UDP 통신 및 주기적 전송 관리 클래스
    /// </summary>
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
        private readonly object _udpLock = new();
        private bool _disposed = false;

        public int LastGlobalSentMessageCounter { get; private set; } = -1;
        public ConcurrentDictionary<int, DateTime> SentMessageTimestamps { get; } = new();

        /// <summary>
        /// 지정한 IP로 리스너를 시작합니다.
        /// </summary>
        public void StartListener(IPAddress ipAddress)
        {
            lock (_udpLock)
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
        }

        /// <summary>
        /// 리스너 및 주기적 전송을 중지합니다.
        /// </summary>
        public void StopListener()
        {
            lock (_udpLock)
            {
                StopPeriodicSend();
                _listenerCts?.Cancel();
                _udpClient?.Close();
                _udpClient = null;
                ListenerStopped?.Invoke();
            }
        }

        private async Task ListenForMessages(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    UdpClient? client;
                    lock (_udpLock) { client = _udpClient; }
                    if (client == null) break;
                    var receivedResult = await client.ReceiveAsync(token);
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

        /// <summary>
        /// 단일 메시지를 비동기로 전송합니다.
        /// </summary>
        public async Task SendSingleMessageAsync(IPEndPoint targetEndPoint, bool enableBroadcast, string message)
        {
            UdpClient? client;
            lock (_udpLock) { client = _udpClient; }
            if (client == null) return;
            try
            {
                client.EnableBroadcast = enableBroadcast;
                var fullMessage = $"<FTEST,{_messageCounter},{message}>";
                byte[] bytesToSend = Encoding.UTF8.GetBytes(fullMessage);

                LastGlobalSentMessageCounter = _messageCounter;
                SentMessageTimestamps[_messageCounter] = DateTime.UtcNow;

                await client.SendAsync(bytesToSend, targetEndPoint);
                LogMessage?.Invoke($"Discovery message sent with Seq={_messageCounter}");
                _messageCounter++;
                if (_messageCounter > 65535) _messageCounter = 1;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error sending: {ex.Message}");
            }
        }

        /// <summary>
        /// 주기적 메시지 전송을 시작합니다.
        /// </summary>
        public void StartPeriodicSend(IPEndPoint targetEndPoint, bool enableBroadcast, int interval, int dummySize, int sendLimit, bool isContinuous)
        {
            UdpClient? client;
            lock (_udpLock) { client = _udpClient; }
            if (client == null) return;

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

                        client.EnableBroadcast = enableBroadcast;
                        await client.SendAsync(bytesToSend, targetEndPoint);
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

        /// <summary>
        /// 주기적 전송을 중지합니다.
        /// </summary>
        public void StopPeriodicSend()
        {
            _periodicSendCts?.Cancel();
            _periodicSendCts = null;
            PeriodicSendStatusChanged?.Invoke("Stop");
        }

        /// <summary>
        /// 리소스 해제 및 이벤트 해제
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            StopListener();
            LogMessage = null;
            ListenerStarted = null;
            ListenerStopped = null;
            MessageReceived = null;
            PeriodicSendStatusChanged = null;
            _disposed = true;
        }
    }
}
