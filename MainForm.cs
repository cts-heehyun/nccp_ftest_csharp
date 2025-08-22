using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.IO;

using ScottPlot.WinForms;
using System.Text;

namespace UdpUnicast;

/// <summary>
/// UDP 유니캐스트/브로드캐스트 테스트를 위한 메인 폼 클래스입니다.
/// 장치 발견, 응답 시간 측정, 데이터 로깅 및 시각화 기능을 제공합니다.
/// </summary>
public partial class MainForm : Form
{
    // --- 필드 정의 ---

    // 그래프 데이터 관리: IP 주소별 (시퀀스 번호, 응답 시간) 목록
    private readonly Dictionary<string, List<(int seq, double rtt)>> ipResponseData = new();
    // 현재 그래프에 표시 중인 IP 주소
    private string? currentGraphIp = null;
    // UDP 통신 핵심 로직을 처리하는 관리자 클래스
    private readonly UdpManager _udpManager;
    // UDP 리스너 동작 상태 플래그
    private bool _isListening = false;
    // 주기적 전송 동작 상태 플래그
    private bool _isPeriodicSending = false;

    // UI에 표시될 장치(MAC 주소 기준) 목록 관리 (쓰레드 안전)
    private readonly ConcurrentDictionary<string, ListViewItem> macListViewItems = new();
    // 한 전송 주기 내에 응답한 MAC 주소 목록 (중복 응답 확인용)
    private readonly ConcurrentDictionary<string, bool> respondedMacsInCycle = new();

    // 상수 정의
    private const string ErrorCountDefault = "0";       // 오류 카운트 기본값
    private const string ResponseTimeDefault = "N/A";   // 응답 시간 기본값
    private const string TimeoutText = "Timeout";       // 타임아웃 표시 문자열

    // CSV 로그 관련 필드
    private string? _currentLogFileName; // 현재 사용 중인 로그 파일 이름
    private StreamWriter? _logWriter;    // 로그 파일 쓰기 스트림
    private readonly object _logLock = new(); // 로그 파일 접근 동기화를 위한 잠금 객체
    private readonly Dictionary<(string ip, int seq), long> _sendLogMap = new(); // 전송 로그 기록을 위한 맵 (IP, 시퀀스) -> 전송 시간(ms)

    /// <summary>
    /// 컨트롤의 Invoke가 필요한 경우, UI 스레드에서 안전하게 액션을 실행합니다.
    /// </summary>
    /// <param name="control">UI 컨트롤</param>
    /// <param name="action">실행할 액션</param>
    private void InvokeIfRequired(Control control, Action action)
    {
        if (control.InvokeRequired) control.Invoke(action);
        else action();
    }

    // 그래프에 표시할 IP를 선택하는 콤보박스
    private ComboBox cmbGraphIp;

    /// <summary>
    /// MainForm 생성자
    /// </summary>
    public MainForm()
    {
        InitializeComponent();

        // UdpManager 초기화 및 이벤트 핸들러 연결
        _udpManager = new UdpManager();
        _udpManager.LogMessage += AppendLog;
        _udpManager.ListenerStarted += UdpManager_ListenerStarted;
        _udpManager.ListenerStopped += UdpManager_ListenerStopped;
        _udpManager.MessageReceived += UdpManager_MessageReceived;
        _udpManager.PeriodicSendStatusChanged += UdpManager_PeriodicSendStatusChanged;
        _udpManager.SendRecvLogCallback = UdpManager_SendRecvLogCallback;
        _udpManager.CheckForMissedResponses = CheckForMissedResponses;
        _udpManager.CleanupOldTimestamps = CleanupOldTimestamps;

        // 그래프용 IP 선택 콤보박스 동적 생성 및 설정
        cmbGraphIp = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Width = 200,
            Location = new System.Drawing.Point(10, 10),
            Name = "cmbGraphIp"
        };
        cmbGraphIp.SelectedIndexChanged += CmbGraphIp_SelectedIndexChanged;
        tabPageGraph.Controls.Add(cmbGraphIp); // 그래프 탭에 추가

        // 그래프 컨트롤 위치 및 크기 조정
        formsPlot.Location = new System.Drawing.Point(10, 40);
        formsPlot.Size = new System.Drawing.Size(tabPageGraph.Width - 20, tabPageGraph.Height - 50);
        formsPlot.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
    }

    /// <summary>
    /// 폼 로드 시 실행됩니다. 로컬 IP 주소 목록을 가져와 콤보박스에 채웁니다.
    /// </summary>
    private void MainForm_Load(object sender, EventArgs e)
    {
        cmbBindIp.Items.Add("Any IP (0.0.0.0)");
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork) { cmbBindIp.Items.Add(ip.ToString()); }
        }
        cmbBindIp.SelectedIndex = 0;
        grpSend.Enabled = false; // 리스너 시작 전에는 전송 그룹 비활성화
        SetPeriodicSendUIState(isSending: false);
    }

    /// <summary>
    /// "Start"/"Stop" 버튼 클릭 시 UDP 리스너를 시작하거나 중지합니다.
    /// </summary>
    private void btnStartStopListen_Click(object sender, EventArgs e)
    {
        if (_isListening)
        {
            _udpManager.StopListener();
        }
        else
        {
            IPAddress selectedIp = cmbBindIp.SelectedIndex == 0 ? IPAddress.Any : IPAddress.Parse((string?)cmbBindIp.SelectedItem ?? "127.0.0.1");
            _udpManager.StartListener(selectedIp);
        }
    }

    /// <summary>
    /// UdpManager가 리스너를 성공적으로 시작했을 때 호출됩니다.
    /// </summary>
    /// <param name="localEndPoint">바인딩된 로컬 EndPoint</param>
    private void UdpManager_ListenerStarted(IPEndPoint localEndPoint)
    {
        InvokeIfRequired(this, () =>
        {
            AppendLog($"Bound to {localEndPoint}");
            btnStartStopListen.Text = "Stop";
            cmbBindIp.Enabled = false;
            grpSend.Enabled = true;
            _isListening = true;
        });
    }

    /// <summary>
    /// UdpManager가 리스너를 중지했을 때 호출됩니다.
    /// </summary>
    private void UdpManager_ListenerStopped()
    {
        InvokeIfRequired(this, () =>
        {
            AppendLog("Listener stopped.");
            btnStartStopListen.Text = "Start";
            cmbBindIp.Enabled = true;
            grpSend.Enabled = false;
            _isListening = false;
        });
    }

    /// <summary>
    /// UDP 메시지 수신 시 호출되는 핵심 처리 메서드입니다.
    /// </summary>
    /// <param name="buffer">수신된 데이터 버퍼</param>
    /// <param name="remoteEP">송신자 EndPoint</param>
    private void UdpManager_MessageReceived(byte[] buffer, IPEndPoint remoteEP)
    {
        var rawMessage = Encoding.UTF8.GetString(buffer);

        // FTEST 프로토콜 응답 메시지인지 확인
        const string ResponsePrefix = "[FTEST,0,";
        if (rawMessage.StartsWith(ResponsePrefix) && rawMessage.EndsWith("]"))
        {
            string content = rawMessage[ResponsePrefix.Length..^1];
            string[] parts = content.Split(new[] { ',' }, 5);
            if (parts.Length == 5)
            {
                string mac = parts[0];
                string echoedSeqStr = parts[1];

                // 장치 목록(ListView)에 MAC 주소와 IP 주소 추가 또는 업데이트
                AddOrUpdateMac(mac, remoteEP.Address.ToString());

                if (int.TryParse(echoedSeqStr, out int echoedSeq))
                {
                    // 수신된 시퀀스 번호와 마지막으로 보낸 시퀀스 번호가 다른 경우
                    if (echoedSeq != _udpManager.LastGlobalSentMessageCounter)
                    {
                        UpdateMismatchCount(mac); // 불일치 카운트 증가
                        string ip = remoteEP.Address.ToString();
                        AppendLog($"Delay Received from {ip}, recv {(echoedSeq).ToString()}, send {(_udpManager.LastGlobalSentMessageCounter).ToString()}");
                    }
                    else
                    {
                        // Over Count 처리: 이미 응답한 MAC이면 over count 증가
                        if (respondedMacsInCycle.ContainsKey(mac))
                        {
                            UpdateOverCount(mac);
                            string ip = remoteEP.Address.ToString();
                            AppendLog($"Over Received from {ip}");
                        }
                        respondedMacsInCycle.TryAdd(mac, true);

                        // RTT(왕복 시간) 계산
                        if (_udpManager.SentMessageTimestamps.TryGetValue(echoedSeq, out DateTime sendTime))
                        {
                            var rtt = (DateTime.UtcNow - sendTime).TotalMilliseconds;
                            UpdateDeviceResponseTime(mac, rtt); // UI에 응답 시간 업데이트

                            // --- 그래프 데이터 추가 및 갱신 ---
                            string ip = remoteEP.Address.ToString();
                            lock (ipResponseData)
                            {
                                if (!ipResponseData.ContainsKey(ip))
                                {
                                    ipResponseData[ip] = new List<(int, double)>();
                                    // 새 IP가 발견되면 그래프 IP 선택 콤보박스에 추가
                                    InvokeIfRequired(cmbGraphIp, () =>
                                    {
                                        cmbGraphIp.Items.Add(ip);
                                        if (cmbGraphIp.Items.Count == 1)
                                        {
                                            cmbGraphIp.SelectedIndex = 0;
                                            currentGraphIp = ip;
                                        }
                                    });
                                }
                                ipResponseData[ip].Add((echoedSeq, rtt));
                                // 현재 선택된 IP의 데이터인 경우 그래프 즉시 업데이트
                                if (ip == currentGraphIp)
                                {
                                    UpdateGraph(ip);
                                }
                            }

                            // --- CSV 로그 기록 ---
                            lock (_logLock)
                            {
                                if (_logWriter != null)
                                {
                                    if (_sendLogMap.TryGetValue((ip, echoedSeq), out long sendMs))
                                    {
                                        var dt = DateTimeOffset.FromUnixTimeMilliseconds(sendMs).ToLocalTime().DateTime;
                                        string sendTimeStr = dt.ToString("HH:mm:ss.fff");
                                        string rttStr = rtt.ToString("F1");
                                        _logWriter.WriteLine($"recv,{ip},{echoedSeq},{sendTimeStr},{rttStr}");
                                    }
                                    else
                                    {
                                        string rttStr = rtt.ToString("F1");
                                        _logWriter.WriteLine($"recv,{ip},{echoedSeq},,{rttStr}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // FTEST 프로토콜이 아닌 경우, 받은 메시지 그대로 로그에 표시
            AppendLog($"Received from {remoteEP}: {rawMessage}");
        }
    }

    /// <summary>
    /// 그래프 IP 선택 콤보박스에서 다른 IP를 선택했을 때 호출됩니다.
    /// </summary>
    private void CmbGraphIp_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (cmbGraphIp.SelectedItem is string ip)
        {
            currentGraphIp = ip;
            UpdateGraph(ip);
        }
    }

    /// <summary>
    /// 지정된 IP에 대한 응답 시간 그래프를 업데이트합니다.
    /// </summary>
    /// <param name="ip">그래프를 그릴 IP 주소</param>
    private void UpdateGraph(string ip)
    {
        InvokeIfRequired(formsPlot, () =>
        {
            formsPlot.Plot.Clear();
            if (!ipResponseData.ContainsKey(ip) || ipResponseData[ip].Count == 0)
            {
                formsPlot.Refresh();
                return;
            }
            var data = ipResponseData[ip];
            double[] xs = data.Select(d => (double)d.seq).ToArray();
            double[] ys = data.Select(d => d.rtt).ToArray();
            
            var scatter = formsPlot.Plot.Add.Scatter(xs, ys, color: ScottPlot.Colors.Blue);
            scatter.MarkerSize = 5;
            scatter.LineWidth = 0;
            formsPlot.Plot.Title($"Response Time (IP: {ip})");
            formsPlot.Plot.XLabel("Sequence Number");
            formsPlot.Plot.YLabel("Response Time (ms)");
            formsPlot.Plot.Axes.AutoScale();
            formsPlot.Refresh();
        });
    }

    /// <summary>
    /// 장치 목록(ListView)에 새 장치를 추가하거나 기존 장치의 IP 주소를 업데이트합니다.
    /// </summary>
    /// <param name="mac">장치의 MAC 주소</param>
    /// <param name="ipAddress">장치의 IP 주소</param>
    private void AddOrUpdateMac(string mac, string ipAddress)
    {
        // 이미 목록에 있는 MAC인 경우
        if (macListViewItems.TryGetValue(mac, out var existingItem))
        {
            // IP 주소가 변경되었으면 업데이트
            if (existingItem.SubItems[1].Text != ipAddress)
            {
                if (existingItem.ListView != null)
                    InvokeIfRequired(existingItem.ListView, () => existingItem.SubItems[1].Text = ipAddress);
            }
            return;
        }

        // 새 MAC인 경우, 리스트에 새로 추가
        InvokeIfRequired(lvMacStatus, () =>
        {
            if (macListViewItems.ContainsKey(mac)) return; // 이중 확인
            var item = new ListViewItem(mac) { Checked = true };
            item.SubItems.Add(ipAddress);
            item.SubItems.Add(ErrorCountDefault);    // Error Count
            item.SubItems.Add(ResponseTimeDefault);  // Response Time
            item.SubItems.Add(ErrorCountDefault);    // Mismatch Count
            item.SubItems.Add(ErrorCountDefault);    // Over Count
            lvMacStatus.Items.Add(item);
            macListViewItems.TryAdd(mac, item);
            AppendLog($"New device discovered: {mac} at {ipAddress}");
            UpdateDeviceCount();
        });
    }

    /// <summary>
    /// 발견된 장치 수를 UI에 업데이트합니다.
    /// </summary>
    private void UpdateDeviceCount()
    {
        InvokeIfRequired(lblDeviceCount, () =>
        {
            lblDeviceCount.Text = $"Discovered: {macListViewItems.Count}";
        });
    }

    /// <summary>
    /// 중복 수신(Over Count) 카운트를 1 증가시킵니다.
    /// </summary>
    private void UpdateOverCount(string mac)
    {
        if (macListViewItems.TryGetValue(mac, out var item))
        {
            InvokeIfRequired(item.ListView!, () =>
            {
                if (item.SubItems.Count > 5)
                {
                    int currentOverCount = int.Parse(item.SubItems[5].Text);
                    item.SubItems[5].Text = (currentOverCount + 1).ToString();
                }
            });
        }
    }

    /// <summary>
    /// 시퀀스 번호 불일치(Mismatch Count) 카운트를 1 증가시킵니다.
    /// </summary>
    private void UpdateMismatchCount(string mac)
    {
        if (macListViewItems.TryGetValue(mac, out var item))
        {
            InvokeIfRequired(item.ListView!, () =>
            {
                if (item.SubItems.Count > 4)
                {
                    int currentMismatch = int.Parse(item.SubItems[4].Text);
                    item.SubItems[4].Text = (currentMismatch + 1).ToString();
                }
            });
        }
    }

    /// <summary>
    /// 장치의 응답 시간(RTT)을 UI에 업데이트합니다.
    /// </summary>
    private void UpdateDeviceResponseTime(string mac, double rtt)
    {
        if (macListViewItems.TryGetValue(mac, out var item))
        {
            if (item.ListView != null)
                InvokeIfRequired(item.ListView, () => item.SubItems[3].Text = rtt.ToString("F1"));
        }
    }

    /// <summary>
    /// "Send" 버튼 클릭 시 단일 또는 주기적 메시지 전송을 시작/중지합니다.
    /// </summary>
    private async void btnSend_Click(object sender, EventArgs e)
    {
        // 주기적 전송 모드
        if (chkPeriodicSend.Checked)
        {
            if (_isPeriodicSending)
            {
                _udpManager.StopPeriodicSend();
            }
            else
            {
                if (!ValidateAndGetTarget(out IPEndPoint? targetEndPoint, isPeriodic: true)) return;
                ResetDeviceErrorAndMismatch(); // 통계 초기화
                ResetGraphData();              // 그래프 데이터 초기화
                _udpManager.StartPeriodicSend(targetEndPoint!, chkEnableBroadcast.Checked, (int)numInterval.Value, (int)numDummySize.Value, (int)numSendCountLimit.Value, chkContinuousSend.Checked);
            }
        }
        // 단일 전송 모드
        else
        {
            if (!ValidateAndGetTarget(out IPEndPoint? targetEndPoint, isPeriodic: false)) return;
            ResetGraphData();
            await _udpManager.SendSingleMessageAsync(targetEndPoint!, chkEnableBroadcast.Checked, txtSendMessage.Text);
        }
    }

    /// <summary>
    /// 그래프 데이터와 관련 UI를 초기화합니다.
    /// </summary>
    private void ResetGraphData()
    {
        lock (ipResponseData)
        {
            ipResponseData.Clear();
        }
        InvokeIfRequired(cmbGraphIp, () =>
        {
            cmbGraphIp.Items.Clear();
            currentGraphIp = null;
        });
        formsPlot.Plot.Clear();
        formsPlot.Refresh();
    }

    /// <summary>
    /// 모든 장치의 오류 및 불일치 카운트를 0으로 초기화합니다.
    /// </summary>
    private void ResetDeviceErrorAndMismatch()
    {
        InvokeIfRequired(lvMacStatus, () =>
        {
            foreach (ListViewItem item in lvMacStatus.Items)
            {
                item.SubItems[2].Text = ErrorCountDefault; // Error
                item.SubItems[4].Text = ErrorCountDefault; // Mismatch
                if (item.SubItems.Count > 5)
                {
                    item.SubItems[5].Text = ErrorCountDefault; // Over
                }
            }
        });
    }
    
    /// <summary>
    /// 주기적 전송 상태 변경 시 호출됩니다. CSV 로깅을 시작하거나 중지합니다.
    /// </summary>
    /// <param name="status">"Start|파일명" 또는 "Stop|파일명" 형태의 상태 문자열</param>
    private void UdpManager_PeriodicSendStatusChanged(string status)
    {
        var parts = status.Split('|');
        var state = parts[0];
        var fileName = parts.Length > 1 ? parts[1] : null;

        if (state == "Start" && fileName != null)
        {
            _currentLogFileName = fileName;
            try
            {
                // CSV 파일 및 헤더 생성
                _logWriter = new StreamWriter(_currentLogFileName, false, Encoding.UTF8);
                _logWriter.WriteLine("type,ip,seq,sendTimeMs,responseTimeMs");
            }
            catch (Exception ex)
            {
                AppendLog($"CSV 파일 생성 실패: {ex.Message}");
            }
        }
        else if (state == "Stop" && _logWriter != null)
        {
            try { _logWriter.Flush(); _logWriter.Close(); } catch { }
            _logWriter = null;
            
            // 주기적 전송 종료 시, 에러 카운트 별도 파일로 저장
            var errFile = Path.ChangeExtension(_currentLogFileName, ".err.csv");
            try
            {
                using var errWriter = new StreamWriter(errFile, false, Encoding.UTF8);
                errWriter.WriteLine("mac,ip,errorCount,mismatchCount,OverCount");
                foreach (var item in macListViewItems.Values)
                {
                    var mac = item.Text;
                    var ip = item.SubItems[1].Text;
                    var err = item.SubItems[2].Text;
                    var mismatch = item.SubItems[4].Text;
                    var over = item.SubItems[5].Text;
                    errWriter.WriteLine($"{mac},{ip},{err},{mismatch},{over}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"오류 카운트 저장 실패: {ex.Message}");
            }
        }

        // UI 상태 업데이트
        InvokeIfRequired(this, () =>
        {
            if (state == "Start")
            {
                _isPeriodicSending = true;
                SetPeriodicSendUIState(isSending: true);
            }
            else // Stop 또는 전송 횟수 도달
            {
                _isPeriodicSending = false;
                SetPeriodicSendUIState(isSending: false);
            }
        });
    }
    
    /// <summary>
    /// UdpManager로부터 송/수신 로그 콜백을 받아 처리합니다. (주로 송신 기록용)
    /// </summary>
    private void UdpManager_SendRecvLogCallback(string type, string ip, string? fileName, long sendTimeMs, long? responseTimeMs)
    {
        lock (_logLock)
        {
            if (_logWriter == null) return;
            if (type == "send")
            {
                int seq = _udpManager.LastGlobalSentMessageCounter;
                _sendLogMap[(ip, seq)] = sendTimeMs; // 수신 시 RTT 계산을 위해 전송 시간 저장
                _logWriter.WriteLine($"send,{ip},{seq},{DateTime.Now:HH:mm:ss.fff},");
            }
            // 수신 로그는 MessageReceived 핸들러에서 직접 기록합니다.
        }
    }

    /// <summary>
    /// 주기적 전송의 한 사이클이 끝날 때마다 응답이 없었던 장치를 확인하고 처리합니다.
    /// </summary>
    private void CheckForMissedResponses()
    {
        List<ListViewItem> checkedItems = new List<ListViewItem>();
        InvokeIfRequired(lvMacStatus, () => checkedItems = lvMacStatus.CheckedItems.Cast<ListViewItem>().ToList());

        // 체크된 항목 중 이번 주기에 응답이 없었던 장치의 에러 카운트 증가
        foreach (var item in checkedItems)
        {
            string mac = item.Text;
            if (!respondedMacsInCycle.ContainsKey(mac))
            {
                if (item.ListView != null)
                    InvokeIfRequired(item.ListView, () =>
                    {
                        int currentErrors = int.Parse(item.SubItems[2].Text);
                        item.SubItems[2].Text = (currentErrors + 1).ToString();
                        item.SubItems[3].Text = TimeoutText;
                    });
                AppendLog($"Don't Received from {item.SubItems[1].Text}, count {item.SubItems[2].Text}");
            }
        }
        // 다음 주기를 위해 응답 기록 초기화
        respondedMacsInCycle.Clear();
    }

    /// <summary>
    /// 오래된 전송 타임스탬프를 정리하여 메모리 사용량을 관리합니다.
    /// </summary>
    private void CleanupOldTimestamps()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-5); // 5초 이상 지난 타임스탬프 제거
        foreach (var pair in _udpManager.SentMessageTimestamps)
        {
            if (pair.Value < cutoff)
            {
                _udpManager.SentMessageTimestamps.TryRemove(pair.Key, out _);
            }
        }
    }

    /// <summary>
    /// 전송하기 전 대상 IP, 포트 등 입력 값의 유효성을 검사합니다.
    /// </summary>
    /// <param name="targetEndPoint">유효성 검사를 통과한 경우 생성된 IPEndPoint</param>
    /// <param name="isPeriodic">주기적 전송인지 여부</param>
    /// <returns>유효하면 true, 아니면 false</returns>
    private bool ValidateAndGetTarget(out IPEndPoint? targetEndPoint, bool isPeriodic)
    {
        targetEndPoint = null;
        if (!_isListening) return false;

        IPAddress? targetIp;
        if (chkEnableBroadcast.Checked)
        {
            targetIp = IPAddress.Broadcast;
        }
        else
        {
            if (!IPAddress.TryParse(txtIpAddress.Text ?? string.Empty, out targetIp) || targetIp == null)
            {
                MessageBox.Show("Invalid IP.", "Warning");
                return false;
            }
        }

        if (!int.TryParse(txtPort.Text, out int targetPort) || targetPort < 1 || targetPort > 65535)
        {
            MessageBox.Show("Invalid Port.", "Warning");
            return false;
        }

        if (!isPeriodic && string.IsNullOrWhiteSpace(txtSendMessage.Text))
        {
            MessageBox.Show("Discovery message is empty.", "Warning");
            return false;
        }

        targetEndPoint = new IPEndPoint(targetIp, targetPort);
        return true;
    }

    /// <summary>
    /// '주기적 전송' 체크박스 상태 변경 시 UI를 업데이트합니다.
    /// </summary>
    private void chkPeriodicSend_CheckedChanged(object sender, EventArgs e)
    {
        SetPeriodicSendUIState(isSending: _isPeriodicSending);
    }

    /// <summary>
    /// '무한 전송' 체크박스 상태 변경 시 UI를 업데이트합니다.
    /// </summary>
    private void chkContinuousSend_CheckedChanged(object sender, EventArgs e)
    {
        SetPeriodicSendUIState(isSending: _isPeriodicSending);
    }

    /// <summary>
    /// 주기적 전송 관련 UI 컨트롤들의 활성화/비활성화 상태를 설정합니다.
    /// </summary>
    /// <param name="isSending">현재 전송 중인지 여부</param>
    private void SetPeriodicSendUIState(bool isSending)
    {
        bool isPeriodicChecked = chkPeriodicSend.Checked;
        bool isContinuousChecked = chkContinuousSend.Checked;

        InvokeIfRequired(this, () =>
        {
            lblInterval.Enabled = isPeriodicChecked;
            numInterval.Enabled = isPeriodicChecked && !isSending;
            lblDummySize.Enabled = isPeriodicChecked;
            numDummySize.Enabled = isPeriodicChecked && !isSending;

            lblSendCountLimit.Enabled = isPeriodicChecked && !isContinuousChecked;
            numSendCountLimit.Enabled = isPeriodicChecked && !isContinuousChecked && !isSending;

            txtSendMessage.Enabled = !isPeriodicChecked;
            lblSendMessage.Enabled = !isPeriodicChecked;
            btnSend.Text = isPeriodicChecked ? (isSending ? "Stop" : "Start") : "Send";

            chkContinuousSend.Enabled = isPeriodicChecked;
        });
    }



    /// <summary>
    /// '브로드캐스트' 체크박스 상태 변경 시 IP 주소 입력을 처리합니다.
    /// </summary>
    private void chkEnableBroadcast_CheckedChanged(object sender, EventArgs e)
    {
        if (chkEnableBroadcast.Checked) 
        { 
            txtIpAddress.Text = "255.255.255.255"; 
            txtIpAddress.Enabled = false; 
        }
        else 
        { 
            txtIpAddress.Text = "127.0.0.1"; 
            txtIpAddress.Enabled = true; 
        }
    }

    /// <summary>
    /// 로그 메시지를 UI의 텍스트 박스에 추가합니다.
    /// </summary>
    /// <param name="message">기록할 메시지</param>
    private void AppendLog(string message)
    {
        InvokeIfRequired(txtLog, () =>
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        });
    }

    /// <summary>
    /// 폼이 닫힐 때 UdpManager 리소스를 정리합니다.
    /// </summary>
    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        try { _udpManager.Dispose(); } catch { }
    }
}