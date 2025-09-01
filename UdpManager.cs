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
        /// <summary>
        /// 로그 메시지 발생 시 호출될 이벤트
        /// </summary>
        public event Action<string>? LogMessage;
        /// <summary>
        /// 리스너 시작 시 호출될 이벤트
        /// </summary>
        public event Action<IPEndPoint>? ListenerStarted;
        /// <summary>
        /// 리스너 중지 시 호출될 이벤트
        /// </summary>
        public event Action? ListenerStopped;
        /// <summary>
        /// 메시지 수신 시 호출될 이벤트
        /// </summary>
        public event Action<byte[], IPEndPoint>? MessageReceived;
        /// <summary>
        /// 주기적 전송 상태 변경 시 호출될 이벤트
        /// </summary>
        public event Action<string>? PeriodicSendStatusChanged;

        /// <summary>
        /// 송수신 로그 기록을 위한 콜백 (타입, 소스IP, 파일명?, 송신시간(ms), 응답시간(ms))
        /// </summary>
        public Action<string, string, string?, string, long?>? SendRecvLogCallback;

        /// <summary>
        /// 응답 없는 장치 확인을 위한 콜백
        /// </summary>
        public Action? CheckForMissedResponses;
        /// <summary>
        /// 오래된 타임스탬프 정리를 위한 콜백
        /// </summary>
        public Action? CleanupOldTimestamps;

        private UdpClient? _udpClient;
        private CancellationTokenSource? _listenerCts;
        private CancellationTokenSource? _periodicSendCts;
        private int _messageCounter = 0;
        private int _periodicSendCount = 0;
        private readonly object _udpLock = new();
        private readonly object _sendRecvLock = new();
        private bool _disposed = false;

        /// <summary>
        /// 마지막으로 전역으로 보낸 메시지의 시퀀스 카운터
        /// </summary>
        public int LastGlobalSentMessageCounter { get; private set; } = -1;
        /// <summary>
        /// RTT 계산을 위해 전송된 메시지의 타임스탬프를 저장하는 딕셔너리
        /// </summary>
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
                    // UdpClient를 지정된 IP와 동적 포트(0)로 초기화
                    _udpClient = new UdpClient(new IPEndPoint(ipAddress, 0));
                    _listenerCts = new CancellationTokenSource();
                    // 메시지 수신을 위한 백그라운드 작업 시작
                    Task.Run(() => ListenForMessages(_listenerCts.Token));
                    // 리스너가 성공적으로 시작되었음을 알림
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
                // 주기적 전송이 활성화되어 있으면 중지
                StopPeriodicSend();
                // 리스너 작업 취소
                _listenerCts?.Cancel();
                _listenerCts?.Dispose();
                _listenerCts = null;
                _udpClient?.Close();
                _udpClient?.Dispose();
                _udpClient = null;
                // 리스너가 중지되었음을 알림
                ListenerStopped?.Invoke();
            }
        }

        // 백그라운드에서 메시지를 수신하는 비동기 메서드
        private async Task ListenForMessages(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    UdpClient? client;
                    lock (_udpLock) { client = _udpClient; }
                    if (client == null) break;
                    
                    // 메시지를 비동기적으로 수신
                    var receivedResult = await client.ReceiveAsync(token);
                    lock (_sendRecvLock)
                    {
                        // 메시지 수신 이벤트 호출
                        MessageReceived?.Invoke(receivedResult.Buffer, receivedResult.RemoteEndPoint);
                        // 수신 로그 콜백 호출 (type, sourceIp, 파일명 null, 송신 시간 0, 응답 시간 ms)
                        //SendRecvLogCallback?.Invoke("recv", receivedResult.RemoteEndPoint.Address.ToString(), null, "0", (long)(DateTime.Now - DateTime.UnixEpoch).TotalMilliseconds);
                    }
                        
                }
                catch (OperationCanceledException)
                {
                    // 작업이 취소되면 루프를 빠져나감 (정상적인 종료)
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
                // 프로토콜 형식에 맞춰 메시지 구성
                var fullMessage = $"<{message}>";
                byte[] bytesToSend = Encoding.UTF8.GetBytes(fullMessage);

                const string SendPrefix = "<FTEST,";
                if (fullMessage.StartsWith(SendPrefix) && fullMessage.EndsWith(">"))
                {
                    string content = fullMessage[SendPrefix.Length..^1];
                    string[] parts = content.Split(new[] { ',' }, 5);

                    // RTT 계산을 위해 시퀀스 번호와 전송 시간 기록
                    _messageCounter = int.Parse(parts[0]);
                    LastGlobalSentMessageCounter = _messageCounter;
                    SentMessageTimestamps[_messageCounter] = DateTime.Now;

                    CheckForMissedResponses?.Invoke();
                }

                await client.SendAsync(bytesToSend, targetEndPoint);
                LogMessage?.Invoke($"{fullMessage}");
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

            // 파일명 생성 및 이벤트 알림 (MainForm에서 파일 관리)
            string fileTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string logFileName = $"comm_{fileTimestamp}.csv";
            PeriodicSendStatusChanged?.Invoke($"Start|{logFileName}");

            LogMessage?.Invoke($"Start");

            client.EnableBroadcast = enableBroadcast;

            // 주기적 전송을 위한 백그라운드 작업 시작
            Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
                try
                {
                    while (await timer.WaitForNextTickAsync(localCts.Token))
                    {
                        // 전송 횟수 제한 확인
                        if (!isContinuous && _periodicSendCount >= sendLimit)
                        {
                            LogMessage?.Invoke($"Periodic send limit ({sendLimit}) reached. Stopping.");
                            PeriodicSendStatusChanged?.Invoke("Stop");
                            break;
                        }

                        // 더미 데이터를 포함한 메시지 생성
                        var dummyData = new string('X', dummySize);
                        var message = $"<FTEST,{_messageCounter},{dummyData}>";
                        byte[] bytesToSend = Encoding.UTF8.GetBytes(message);

                        var now = DateTime.Now;
                        lock (_sendRecvLock)
                        {
                            LastGlobalSentMessageCounter = _messageCounter;
                            SentMessageTimestamps[_messageCounter] = now;
                            // 응답 없는 장치 확인 및 타임스탬프 정리 콜백 호출
                            CheckForMissedResponses?.Invoke();
                            CleanupOldTimestamps?.Invoke();
                            // 송신 로그 콜백 호출 (type, sourceIp, 파일명, 송신 시간 ms, 응답 시간 null)
                            string localIp = ((IPEndPoint)client.Client.LocalEndPoint!).Address.ToString();
                            SendRecvLogCallback?.Invoke("send", localIp, logFileName, now.ToString("HH:mm:ss.fff"), null);
                        }
                        await client.SendAsync(bytesToSend, targetEndPoint);

                        _messageCounter++;
                        _periodicSendCount++;
                        if (_messageCounter > 65535) _messageCounter = 1; // 시퀀스 번호 순환
                    }
                }
                catch (OperationCanceledException) { /* 작업 취소 시 정상 종료 */ }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Periodic send error: {ex.Message}");
                    PeriodicSendStatusChanged?.Invoke($"Stop|{logFileName}");
                }
            });
        }

        /// <summary>
        /// 주기적 전송을 중지합니다.
        /// </summary>
        public void StopPeriodicSend()
        {
            if (_periodicSendCts != null)
            {
                _periodicSendCts.Cancel();
                _periodicSendCts.Dispose();
                _periodicSendCts = null;
            }
            PeriodicSendStatusChanged?.Invoke("Stop");
        }

        /// <summary>
        /// 리소스 해제 및 이벤트 해제
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            StopListener();
            // 이벤트 핸들러 참조 제거
            LogMessage = null;
            ListenerStarted = null;
            ListenerStopped = null;
            MessageReceived = null;
            PeriodicSendStatusChanged = null;
            _disposed = true;
        }
    }
}
