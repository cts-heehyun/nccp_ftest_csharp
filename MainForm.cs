using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.IO;

using ScottPlot.WinForms;
using System.Text;

namespace UdpUnicast;

public partial class MainForm : Form
{
    // IP별 (시퀀스, 응답시간) 데이터 관리
    private readonly Dictionary<string, List<(int seq, double rtt)>> ipResponseData = new();
    private string? currentGraphIp = null;
    private readonly UdpManager _udpManager;
    private bool _isListening = false;
    private bool _isPeriodicSending = false;

    private readonly ConcurrentDictionary<string, ListViewItem> macListViewItems = new();
    private readonly ConcurrentDictionary<string, bool> respondedMacsInCycle = new();
    private readonly System.Windows.Forms.Timer _periodicCheckTimer;

    // 매직넘버/문자열 상수화
    private const string ErrorCountDefault = "0";
    private const string ResponseTimeDefault = "N/A";
    private const string TimeoutText = "Timeout";

    // CSV 로그 관련 필드
    private string? _currentLogFileName;
    private StreamWriter? _logWriter;
    private readonly object _logLock = new();
    private readonly Dictionary<(string ip, int seq), long> _sendLogMap = new();

    /// <summary>
    /// UI 스레드 안전 호출 유틸
    /// </summary>
    private void InvokeIfRequired(Control control, Action action)
    {
        if (control.InvokeRequired) control.Invoke(action);
        else action();
    }

    private ComboBox cmbGraphIp;

    public MainForm()
    {
        InitializeComponent();
        _udpManager = new UdpManager();
        _udpManager.LogMessage += AppendLog;
        _udpManager.ListenerStarted += UdpManager_ListenerStarted;
        _udpManager.ListenerStopped += UdpManager_ListenerStopped;
        _udpManager.MessageReceived += UdpManager_MessageReceived;
        _udpManager.PeriodicSendStatusChanged += UdpManager_PeriodicSendStatusChanged;
        _udpManager.SendRecvLogCallback = UdpManager_SendRecvLogCallback;

        _udpManager.CheckForMissedResponses = CheckForMissedResponses;
        _udpManager.CleanupOldTimestamps = CleanupOldTimestamps;

        // 그래프용 IP 선택 콤보박스 생성 및 탭에 추가
        cmbGraphIp = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Width = 200,
            Location = new System.Drawing.Point(10, 10),
            Name = "cmbGraphIp"
        };
        cmbGraphIp.SelectedIndexChanged += CmbGraphIp_SelectedIndexChanged;
        tabPageGraph.Controls.Add(cmbGraphIp);
        formsPlot.Location = new System.Drawing.Point(10, 40);
        formsPlot.Size = new System.Drawing.Size(tabPageGraph.Width - 20, tabPageGraph.Height - 50);
        formsPlot.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
    }

    /// <summary>
    /// 폼 로드시 네트워크 인터페이스 목록을 UI에 바인딩
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
        grpSend.Enabled = false;
        SetPeriodicSendUIState(isSending: false);
    }

    /// <summary>
    /// 리스너 시작/중지 버튼 클릭 이벤트
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

    private void UdpManager_MessageReceived(byte[] buffer, IPEndPoint remoteEP)
    {
        var rawMessage = Encoding.UTF8.GetString(buffer);

        const string ResponsePrefix = "[FTEST,0,";
        if (rawMessage.StartsWith(ResponsePrefix) && rawMessage.EndsWith("]"))
        {
            string content = rawMessage[ResponsePrefix.Length..^1];
            string[] parts = content.Split(new[] { ',' }, 5);
            if (parts.Length == 5)
            {
                string mac = parts[0];
                string echoedSeqStr = parts[1];

                AddOrUpdateMac(mac, remoteEP.Address.ToString());

                if (int.TryParse(echoedSeqStr, out int echoedSeq))
                {
                    if (echoedSeq != _udpManager.LastGlobalSentMessageCounter)
                    {
                        UpdateMismatchCount(mac);
                        string ip = remoteEP.Address.ToString();
                        AppendLog($"Delay Received from {ip}, recv {(echoedSeq).ToString()}, send {(_udpManager.LastGlobalSentMessageCounter).ToString()}");
                    }
                    else
                    {
                        // Over Count 처리: 이미 응답된 MAC이면 over count 증가
                        bool alreadyResponded = respondedMacsInCycle.ContainsKey(mac);
                        if (alreadyResponded)
                        {
                            UpdateOverCount(mac);
                            string ip = remoteEP.Address.ToString();
                            AppendLog($"Over Received from {ip}");
                        }
                        respondedMacsInCycle.TryAdd(mac, true);
                        if (_udpManager.SentMessageTimestamps.TryGetValue(echoedSeq, out DateTime sendTime))
                        {
                            var rtt = (DateTime.UtcNow - sendTime).TotalMilliseconds;
                            UpdateDeviceResponseTime(mac, rtt);
                            // --- 그래프 데이터 추가 및 갱신 ---
                            string ip = remoteEP.Address.ToString();
                            lock (ipResponseData)
                            {
                                if (!ipResponseData.ContainsKey(ip))
                                {
                                    ipResponseData[ip] = new List<(int, double)>();
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
                                        // 송신 시간: ms → DateTime 변환 후 "HH:mm:ss.fff" 형식
                                        var dt = DateTimeOffset.FromUnixTimeMilliseconds(sendMs).ToLocalTime().DateTime;
                                        string sendTimeStr = dt.ToString("HH:mm:ss.fff");
                                        // 응답 시간: ms 소수점 첫째 자리
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
            AppendLog($"Received from {remoteEP}: {rawMessage}");
        }
    }

    // 콤보박스 선택 변경 시 그래프 갱신
    private void CmbGraphIp_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (cmbGraphIp.SelectedItem is string ip)
        {
            currentGraphIp = ip;
            UpdateGraph(ip);
        }
    }

    // 그래프 갱신
    private void UpdateGraph(string ip)
    {
        InvokeIfRequired(formsPlot, () =>
        {
            if (!ipResponseData.ContainsKey(ip) || ipResponseData[ip].Count == 0)
            {
                formsPlot.Plot.Clear();
                formsPlot.Refresh();
                return;
            }
            var data = ipResponseData[ip];
            double[] xs = data.Select(d => (double)d.seq).ToArray();
            double[] ys = data.Select(d => d.rtt).ToArray();
            formsPlot.Plot.Clear();
            var scatter = formsPlot.Plot.Add.Scatter(xs, ys, color: ScottPlot.Colors.Blue);
            scatter.MarkerSize = 5;
            scatter.LineWidth = 0;
            formsPlot.Plot.Title($"(IP: {ip})");
            formsPlot.Plot.XLabel("Seq. Number");
            formsPlot.Plot.YLabel("Rep Time(ms)");
            formsPlot.Plot.Axes.AutoScale();
            formsPlot.Refresh();
        });
    }

    /// <summary>
    /// MAC 주소별 장치 정보를 리스트에 추가 또는 갱신
    /// </summary>
    private void AddOrUpdateMac(string mac, string ipAddress)
    {
        if (macListViewItems.TryGetValue(mac, out var existingItem))
        {
            if (existingItem.SubItems[1].Text != ipAddress)
            {
                if (existingItem.ListView != null)
                    InvokeIfRequired(existingItem.ListView, () => existingItem.SubItems[1].Text = ipAddress);
            }
            return;
        }

        InvokeIfRequired(lvMacStatus, () =>
        {
            if (macListViewItems.ContainsKey(mac)) return;
            var item = new ListViewItem(mac) { Checked = true };
            item.SubItems.Add(ipAddress);
            item.SubItems.Add(ErrorCountDefault);
            item.SubItems.Add(ResponseTimeDefault);
            item.SubItems.Add(ErrorCountDefault);
            item.SubItems.Add(ErrorCountDefault);
            lvMacStatus.Items.Add(item);
            macListViewItems.TryAdd(mac, item);
            AppendLog($"New device discovered: {mac} at {ipAddress}");
            UpdateDeviceCount();
        });
    }

    private void UpdateDeviceCount()
    {
        InvokeIfRequired(lblDeviceCount, () =>
        {
            lblDeviceCount.Text = $"Discovered: {macListViewItems.Count}";
        });
    }

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
        /// 시퀀스 불일치 카운트 증가
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
    /// 장치 응답 시간 갱신
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
    /// 전송 버튼 클릭 이벤트 (단일/주기 전송)
    /// </summary>
    private async void btnSend_Click(object sender, EventArgs e)
    {
        if (chkPeriodicSend.Checked)
        {
            if (_isPeriodicSending)
            {
                _udpManager.StopPeriodicSend();
            }
            else
            {
                if (!ValidateAndGetTarget(out IPEndPoint? targetEndPoint, isPeriodic: true)) return;
                ResetDeviceErrorAndMismatch();
                ResetGraphData();
                _udpManager.StartPeriodicSend(targetEndPoint!, chkEnableBroadcast.Checked, (int)numInterval.Value, (int)numDummySize.Value, (int)numSendCountLimit.Value, chkContinuousSend.Checked);
            }
        }
        else
        {
            if (!ValidateAndGetTarget(out IPEndPoint? targetEndPoint, isPeriodic: false)) return;
            ResetGraphData();
            await _udpManager.SendSingleMessageAsync(targetEndPoint!, chkEnableBroadcast.Checked, txtSendMessage.Text);
        }
    }

    // 그래프 데이터 및 UI 초기화
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

    /// 장치 에러/불일치 카운트 초기화
    /// </summary>
    private void ResetDeviceErrorAndMismatch()
    {
        InvokeIfRequired(lvMacStatus, () =>
        {
            foreach (ListViewItem item in lvMacStatus.Items)
            {
                item.SubItems[2].Text = ErrorCountDefault;
                item.SubItems[4].Text = ErrorCountDefault;
                if (item.SubItems.Count > 5)
                {
                    item.SubItems[5].Text = ErrorCountDefault;
                }
            }
        });
    }
    
    private void UdpManager_PeriodicSendStatusChanged(string status)
    {
        // status: "Start|파일명" 또는 "Stop|파일명"
        var parts = status.Split('|');
        var state = parts[0];
        var fileName = parts.Length > 1 ? parts[1] : null;
        if (state == "Start" && fileName != null)
        {
            _currentLogFileName = fileName;
            try
            {
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
            // 오류 카운트 저장
            var errFile = Path.ChangeExtension(_currentLogFileName, ".err.csv");
            try
            {
                using var errWriter = new StreamWriter(errFile, false, Encoding.UTF8);
                errWriter.WriteLine("mac,ip,errorCount,mismatchCount");
                foreach (var item in macListViewItems.Values)
                {
                    var mac = item.Text;
                    var ip = item.SubItems[1].Text;
                    var err = item.SubItems[2].Text;
                    var mismatch = item.SubItems[4].Text;
                    errWriter.WriteLine($"{mac},{ip},{err},{mismatch}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"오류 카운트 저장 실패: {ex.Message}");
            }
        }
        InvokeIfRequired(this, () =>
        {
            if (state == "Start")
            {
                _isPeriodicSending = true;
                SetPeriodicSendUIState(isSending: true);
            }
            else // Stop or Limit Reached
            {
                _isPeriodicSending = false;
                SetPeriodicSendUIState(isSending: false);
            }
        });
    }
    
    private void UdpManager_SendRecvLogCallback(string type, string ip, string? fileName, long sendTimeMs, long? responseTimeMs)
    {
        lock (_logLock)
        {
            if (_logWriter == null) return;
            if (type == "send")
            {
                int seq = _udpManager.LastGlobalSentMessageCounter;
                _sendLogMap[(ip, seq)] = sendTimeMs;
                _logWriter.WriteLine($"send,{ip},{seq},{DateTime.Now:HH:mm:ss.fff},");
            }
            // 수신은 MainForm에서 직접 기록 (아래에서 구현)
        }
    }

    private void CheckForMissedResponses()
    {
        List<ListViewItem> checkedItems = new List<ListViewItem>();
        InvokeIfRequired(lvMacStatus, () => checkedItems = lvMacStatus.CheckedItems.Cast<ListViewItem>().ToList());

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
        respondedMacsInCycle.Clear();
    }

    private void CleanupOldTimestamps()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-5);
        foreach (var pair in _udpManager.SentMessageTimestamps)
        {
            if (pair.Value < cutoff)
            {
                _udpManager.SentMessageTimestamps.TryRemove(pair.Key, out _);
            }
        }
    }

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

    private void chkPeriodicSend_CheckedChanged(object sender, EventArgs e)
    {
        SetPeriodicSendUIState(isSending: _isPeriodicSending);
    }

    private void chkContinuousSend_CheckedChanged(object sender, EventArgs e)
    {
        SetPeriodicSendUIState(isSending: _isPeriodicSending);
    }

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



    private void chkEnableBroadcast_CheckedChanged(object sender, EventArgs e)
    {
        if (chkEnableBroadcast.Checked) { txtIpAddress.Text = "255.255.255.255"; txtIpAddress.Enabled = false; }
        else { txtIpAddress.Text = "127.0.0.1"; txtIpAddress.Enabled = true; }
    }

    private void AppendLog(string message)
    {
        InvokeIfRequired(txtLog, () =>
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        });
    }

    /// <summary>
    /// 폼 종료 시 리소스 해제
    /// </summary>
    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        try { _udpManager.Dispose(); } catch { }
    }
}