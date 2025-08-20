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
    private int lastGlobalSentMessageCounter = -1; // New field

    private readonly ConcurrentDictionary<string, ListViewItem> macListViewItems = new();
    private readonly ConcurrentDictionary<string, bool> respondedMacsInCycle = new();
    private readonly ConcurrentDictionary<int, DateTime> sentMessageTimestamps = new(); // Changed name and key type
    private readonly ConcurrentDictionary<string, int> macMismatchCounts = new(); // New dictionary

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
    }

    private void btnStartStopListen_Click(object sender, EventArgs e)
    {
        if (udpClient == null)
        {
            try
            {
                IPAddress selectedIp = cmbBindIp.SelectedIndex == 0 ? IPAddress.Any : IPAddress.Parse((string)cmbBindIp.SelectedItem);
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
            catch (Exception) { /* 무시 */ }
        }
    }

    private void AddOrUpdateMac(string mac, string ipAddress)
    {
        if (macListViewItems.TryGetValue(mac, out var existingItem))
        {
            // IP 주소가 변경되었는지 확인하고 필요한 경우 업데이트합니다.
            if (existingItem.SubItems[1].Text != ipAddress)
            {
                Invoke(() => existingItem.SubItems[1].Text = ipAddress);
            }
            return;
        }

        Invoke(() =>
        {
            if (macListViewItems.ContainsKey(mac)) return;
            var item = new ListViewItem(mac) { Checked = true };
            item.SubItems.Add(ipAddress);
            item.SubItems.Add("0"); // Error Count
            item.SubItems.Add("N/A"); // Response Time
            item.SubItems.Add("0"); // Mismatch Count
            lvMacStatus.Items.Add(item);
            macListViewItems.TryAdd(mac, item);
            AppendLog($"New device discovered: {mac} at {ipAddress}");
            UpdateDeviceCount();
        });
    }

    private void UpdateDeviceCount()
    {
        if (lblDeviceCount.InvokeRequired)
        {
            lblDeviceCount.Invoke(new Action(UpdateDeviceCount));
        }
        else
        {
            lblDeviceCount.Text = $"Discovered: {macListViewItems.Count}";
        }
    }

    private void UpdateMismatchCount(string mac)
    {
        if (macListViewItems.TryGetValue(mac, out var item))
        {
            Invoke(() =>
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
            Invoke(() => item.SubItems[3].Text = rtt.ToString("F1"));
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
        var localCts = new CancellationTokenSource();
        this.periodicSendCts = localCts;
        var interval = (int)numInterval.Value;
        var dummySize = (int)numDummySize.Value;

        // Reset error counters in ListView
        Invoke(() =>
        {
            foreach (ListViewItem item in lvMacStatus.Items)
            {
                item.SubItems[2].Text = "0"; // Error Count
                item.SubItems[4].Text = "0"; // Mismatch Count
            }
        });
        macMismatchCounts.Clear(); // Clear the dictionary as well

        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
            try
            {
                while (await timer.WaitForNextTickAsync(localCts.Token))
                {
                    CleanupOldTimestamps();
                    CheckForMissedResponses();
                    var dummyData = new string('X', dummySize);
                    var message = $"<FTEST,{messageCounter},{dummyData}>";
                    byte[] bytesToSend = Encoding.UTF8.GetBytes(message);
                    
                    lastGlobalSentMessageCounter = messageCounter; // Store last sent global message counter
                    sentMessageTimestamps[messageCounter] = DateTime.UtcNow; // Store timestamp for RTT

                    await udpClient!.SendAsync(bytesToSend, targetEndPoint!);
                    AppendLog($"Sent: <FTEST,{messageCounter},...>");
                    messageCounter++;
                    if (messageCounter > 65535) messageCounter = 1;
                }
            }
            catch (OperationCanceledException) { /* 정상 */ }
            catch (Exception ex) { Invoke(() => MessageBox.Show($"Periodic send error: {ex.Message}", "Error")); }
        });
        btnSend.Text = "Stop";
        SetPeriodicSendUIState(isSending: true);
    }

    private void CheckForMissedResponses()
    {
        List<ListViewItem> checkedItems = new List<ListViewItem>();
        Invoke(() => checkedItems = lvMacStatus.CheckedItems.Cast<ListViewItem>().ToList());
        foreach (var item in checkedItems)
        {
            string mac = item.Text;
            if (!respondedMacsInCycle.ContainsKey(mac))
            {
                Invoke(() =>
                {
                    int currentErrors = int.Parse(item.SubItems[2].Text);
                    item.SubItems[2].Text = (currentErrors + 1).ToString();
                    item.SubItems[3].Text = "Timeout"; // 응답 시간 타임아웃 처리
                });
            }
        }
        respondedMacsInCycle.Clear();
    }

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
        IPAddress targetIp;
        if (chkEnableBroadcast.Checked)
        {
            targetIp = IPAddress.Broadcast;
            udpClient.EnableBroadcast = true;
        }
        else
        {
            if (!IPAddress.TryParse(txtIpAddress.Text, out targetIp)) { MessageBox.Show("Invalid IP.", "Warning"); return false; }
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

    private void SetPeriodicSendUIState(bool isSending)
    {
        bool isPeriodicChecked = chkPeriodicSend.Checked;
        lblInterval.Enabled = isPeriodicChecked;
        numInterval.Enabled = isPeriodicChecked && !isSending;
        lblDummySize.Enabled = isPeriodicChecked;
        numDummySize.Enabled = isPeriodicChecked && !isSending;
        txtSendMessage.Enabled = !isPeriodicChecked;
        lblSendMessage.Enabled = !isPeriodicChecked;
        btnSend.Text = isPeriodicChecked ? (isSending ? "Stop" : "Start") : "Send";
    }

    private void chkEnableBroadcast_CheckedChanged(object sender, EventArgs e)
    {
        if (chkEnableBroadcast.Checked) { txtIpAddress.Text = "255.255.255.255"; txtIpAddress.Enabled = false; }
        else { txtIpAddress.Text = "127.0.0.1"; txtIpAddress.Enabled = true; }
    }

    private void AppendLog(string message)
    {
        if (txtLog.InvokeRequired) { Invoke(() => AppendLog(message)); return; }
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        StopPeriodicSend();
        listenerCts?.Cancel();
        udpClient?.Dispose();
    }
}