using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UdpUnicast;

public partial class MainForm : Form
{
    private UdpClient? udpClient;
    private CancellationTokenSource? listenerCts;
    private CancellationTokenSource? periodicSendCts;
    private int messageCounter = 0;
    private int periodicSendCount = 0;
    private int lastGlobalSentMessageCounter = -1; // New field

    private readonly ConcurrentDictionary<string, ListViewItem> macListViewItems = new();
    private readonly ConcurrentDictionary<string, bool> respondedMacsInCycle = new();
    private readonly ConcurrentDictionary<int, DateTime> sentMessageTimestamps = new(); // Changed name and key type
    private readonly ConcurrentDictionary<string, int> macMismatchCounts = new();

    // 매직넘버/문자열 상수화
    private const string ErrorCountDefault = "0";
    private const string ResponseTimeDefault = "N/A";
    private const string TimeoutText = "Timeout";

    // UI 스레드 안전 호출 유틸
    private void InvokeIfRequired(Control control, Action action)
    {
        if (control.InvokeRequired) control.Invoke(action);
        else action();
    }

    public MainForm()
    {
        InitializeComponent();
    }

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
        chkPeriodicSend_CheckedChanged(this, EventArgs.Empty);
        chkContinuousSend_CheckedChanged(this, EventArgs.Empty);
    }

    private void btnStartStopListen_Click(object sender, EventArgs e)
    {
        if (udpClient == null)
        {
            try
            {
                IPAddress selectedIp = cmbBindIp.SelectedIndex == 0 ? IPAddress.Any : IPAddress.Parse((string?)cmbBindIp.SelectedItem ?? "127.0.0.1");
                udpClient = new UdpClient(new IPEndPoint(selectedIp, 0));
                listenerCts = new CancellationTokenSource();
                AppendLog($"Bound to {udpClient.Client.LocalEndPoint}");
                Task.Run(() => ListenForMessages(listenerCts.Token));
                btnStartStopListen.Text = "Stop";
                cmbBindIp.Enabled = false;
                grpSend.Enabled = true;
            }
            catch (Exception ex) { MessageBox.Show($"Listener start failed: {ex.Message}", "Error"); udpClient?.Dispose(); udpClient = null; }
        }
        else
        {
            StopPeriodicSend();
            listenerCts?.Cancel();
            udpClient?.Close();
            udpClient = null;
            AppendLog("Listener stopped.");
            btnStartStopListen.Text = "Start";
            cmbBindIp.Enabled = true;
            grpSend.Enabled = false;
        }
    }

    // 비동기로 메시지 수신 및 처리
    private async Task ListenForMessages(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var receivedResult = await udpClient!.ReceiveAsync(token);
                var rawMessage = Encoding.UTF8.GetString(receivedResult.Buffer);

                const string ResponsePrefix = "[FTEST,0,";
                if (rawMessage.StartsWith(ResponsePrefix) && rawMessage.EndsWith("]"))
                {
                    string content = rawMessage[ResponsePrefix.Length..^1];
                    string[] parts = content.Split(new[] { ',' }, 5);
                    if (parts.Length == 5)
                    {
                        string mac = parts[0];
                        string echoedSeqStr = parts[1];
                        
                        AddOrUpdateMac(mac, receivedResult.RemoteEndPoint.Address.ToString());

                        if (int.TryParse(echoedSeqStr, out int echoedSeq))
                        {
                            // Sequence Number Mismatch Check
                            if (echoedSeq != lastGlobalSentMessageCounter)
                            {
                                UpdateMismatchCount(mac);
                            }
                            else
                            {
                                respondedMacsInCycle.TryAdd(mac, true); // Only add if sequence matches
                            }

                            // Update RTT
                            if (sentMessageTimestamps.TryGetValue(echoedSeq, out DateTime sendTime))
                            {
                                var rtt = (DateTime.UtcNow - sendTime).TotalMilliseconds;
                                UpdateDeviceResponseTime(mac, rtt);
                            }
                        }
                    }
                }
                else { AppendLog($"Received from {receivedResult.RemoteEndPoint}: {rawMessage}"); }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { AppendLog($"Listen error: {ex.Message}"); }
        }
    }

    // MAC 주소와 IP를 리스트뷰에 추가하거나, 이미 있으면 IP만 갱신
    private void AddOrUpdateMac(string mac, string ipAddress)
    {
        if (macListViewItems.TryGetValue(mac, out var existingItem))
        {
            // IP 주소가 변경된 경우만 UI 갱신
            if (existingItem.SubItems[1].Text != ipAddress)
            {
                if (existingItem.ListView != null)
                    InvokeIfRequired(existingItem.ListView, () => existingItem.SubItems[1].Text = ipAddress);
            }
            return;
        }

        // 새 MAC이면 리스트뷰에 추가
        InvokeIfRequired(lvMacStatus, () =>
        {
            if (macListViewItems.ContainsKey(mac)) return;
            var item = new ListViewItem(mac) { Checked = true };
            item.SubItems.Add(ipAddress);
            item.SubItems.Add(ErrorCountDefault); // Error Count
            item.SubItems.Add(ResponseTimeDefault); // Response Time
            item.SubItems.Add(ErrorCountDefault); // Mismatch Count
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

    private void UpdateDeviceResponseTime(string mac, double rtt)
    {
        if (macListViewItems.TryGetValue(mac, out var item))
        {
            if (item.ListView != null)
                InvokeIfRequired(item.ListView, () => item.SubItems[3].Text = rtt.ToString("F1"));
        }
    }

    private void btnSend_Click(object sender, EventArgs e)
    {
        if (chkPeriodicSend.Checked)
        {
            if (periodicSendCts == null) StartPeriodicSend();
            else StopPeriodicSend();
        }
        else { SendSingleMessage(); }
    }

    private async void SendSingleMessage()
    {
        if (!ValidateAndGetTarget(out IPEndPoint? targetEndPoint, isPeriodic: false)) return;
        try
        {
            var message = $"<FTEST,{messageCounter},{txtSendMessage.Text}>";
            byte[] bytesToSend = Encoding.UTF8.GetBytes(message);
            
            lastGlobalSentMessageCounter = messageCounter; // Store last sent global message counter
            sentMessageTimestamps[messageCounter] = DateTime.UtcNow; // Store timestamp for RTT

            await udpClient!.SendAsync(bytesToSend, targetEndPoint!);
            AppendLog($"Discovery message sent with Seq={messageCounter}");
            messageCounter++;
            if (messageCounter > 65535) messageCounter = 1;
        }
        catch (Exception ex) { MessageBox.Show($"Error sending: {ex.Message}", "Error"); }
    }

    private void StartPeriodicSend()
    {
        if (!ValidateAndGetTarget(out IPEndPoint? targetEndPoint, isPeriodic: true)) return;
        messageCounter = 0;
        periodicSendCount = 0;
        var localCts = new CancellationTokenSource();
        this.periodicSendCts = localCts;
        var interval = (int)numInterval.Value;
        var dummySize = (int)numDummySize.Value;
        var sendLimit = (int)numSendCountLimit.Value;

        // Reset error counters in ListView
        InvokeIfRequired(lvMacStatus, () =>
        {
            foreach (ListViewItem item in lvMacStatus.Items)
            {
                item.SubItems[2].Text = ErrorCountDefault; // Error Count
                item.SubItems[4].Text = ErrorCountDefault; // Mismatch Count
            }
        });
        macMismatchCounts.Clear();

        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
            try
            {
                while (await timer.WaitForNextTickAsync(localCts.Token))
                {
                    if (!chkContinuousSend.Checked && periodicSendCount >= sendLimit)
                    {
                        AppendLog($"Periodic send limit ({sendLimit}) reached. Stopping.");
                        InvokeIfRequired(this, StopPeriodicSend);
                        break;
                    }

                    CleanupOldTimestamps();
                    CheckForMissedResponses();
                    var dummyData = new string('X', dummySize);
                    var message = $"<FTEST,{messageCounter},{dummyData}>";
                    byte[] bytesToSend = Encoding.UTF8.GetBytes(message);
                    
                    lastGlobalSentMessageCounter = messageCounter;
                    sentMessageTimestamps[messageCounter] = DateTime.UtcNow;

                    await udpClient!.SendAsync(bytesToSend, targetEndPoint!);
                    AppendLog($"Sent: <FTEST,{messageCounter},...>");
                    messageCounter++;
                    periodicSendCount++;
                    if (messageCounter > 65535) messageCounter = 1;
                }
            }
            catch (OperationCanceledException) { /* 정상 */ }
            catch (Exception ex) { AppendLog($"Periodic send error: {ex.Message}"); }
        });
        btnSend.Text = "Stop";
        SetPeriodicSendUIState(isSending: true);
    }

    // 체크된 MAC 중 응답이 없는 경우 에러 카운트 증가 및 타임아웃 표시
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
            }
        }
        respondedMacsInCycle.Clear();
    }

    // 오래된 타임스탬프 정리 (5초 이전 데이터 삭제)
    private void CleanupOldTimestamps()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-5);
        foreach (var pair in sentMessageTimestamps)
        {
            if (pair.Value < cutoff)
            {
                sentMessageTimestamps.TryRemove(pair.Key, out _);
            }
        }
    }

    private void StopPeriodicSend()
    {
        periodicSendCts?.Cancel();
        periodicSendCts = null;
        btnSend.Text = "Start";
        SetPeriodicSendUIState(isSending: false);
    }

    private bool ValidateAndGetTarget(out IPEndPoint? targetEndPoint, bool isPeriodic)
    {
        targetEndPoint = null;
        if (udpClient == null) return false;
        IPAddress? targetIp;
        if (chkEnableBroadcast.Checked)
        {
            targetIp = IPAddress.Broadcast;
            udpClient.EnableBroadcast = true;
        }
        else
        {
            if (!IPAddress.TryParse(txtIpAddress.Text ?? string.Empty, out targetIp) || targetIp == null) { MessageBox.Show("Invalid IP.", "Warning"); return false; }
            udpClient.EnableBroadcast = false;
        }
        if (!int.TryParse(txtPort.Text, out int targetPort) || targetPort < 1 || targetPort > 65535) { MessageBox.Show("Invalid Port.", "Warning"); return false; }
        if (!isPeriodic && string.IsNullOrWhiteSpace(txtSendMessage.Text)) { MessageBox.Show("Discovery message is empty.", "Warning"); return false; }
        targetEndPoint = new IPEndPoint(targetIp, targetPort);
        return true;
    }

    private void chkPeriodicSend_CheckedChanged(object sender, EventArgs e)
    {
        SetPeriodicSendUIState(isSending: false);
    }

    private void chkContinuousSend_CheckedChanged(object sender, EventArgs e)
    {
        SetPeriodicSendUIState(isSending: false);
    }

    private void SetPeriodicSendUIState(bool isSending)
    {
        bool isPeriodicChecked = chkPeriodicSend.Checked;
        bool isContinuousChecked = chkContinuousSend.Checked;

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

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        StopPeriodicSend();
        listenerCts?.Cancel();
        udpClient?.Dispose();
    }
}