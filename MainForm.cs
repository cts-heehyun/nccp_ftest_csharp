using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UdpUnicast;

public partial class MainForm : Form
{
    private readonly UdpManager _udpManager;
    private bool _isListening = false;
    private bool _isPeriodicSending = false;

    private readonly ConcurrentDictionary<string, ListViewItem> macListViewItems = new();
    private readonly ConcurrentDictionary<string, bool> respondedMacsInCycle = new();
    private readonly ConcurrentDictionary<string, int> macMismatchCounts = new();
    private readonly System.Windows.Forms.Timer _periodicCheckTimer;

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
        _udpManager = new UdpManager();
        _udpManager.LogMessage += AppendLog;
        _udpManager.ListenerStarted += UdpManager_ListenerStarted;
        _udpManager.ListenerStopped += UdpManager_ListenerStopped;
        _udpManager.MessageReceived += UdpManager_MessageReceived;
        _udpManager.PeriodicSendStatusChanged += UdpManager_PeriodicSendStatusChanged;

        _periodicCheckTimer = new System.Windows.Forms.Timer();
        _periodicCheckTimer.Interval = 1000; // 1초마다 체크
        _periodicCheckTimer.Tick += PeriodicCheckTimer_Tick;
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
        SetPeriodicSendUIState(isSending: false);
    }

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
                    }
                    else
                    {
                        respondedMacsInCycle.TryAdd(mac, true);
                    }

                    if (_udpManager.SentMessageTimestamps.TryGetValue(echoedSeq, out DateTime sendTime))
                    {
                        var rtt = (DateTime.UtcNow - sendTime).TotalMilliseconds;
                        UpdateDeviceResponseTime(mac, rtt);
                    }
                }
            }
        }
        else
        {
            AppendLog($"Received from {remoteEP}: {rawMessage}");
        }
    }

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
                
                // Reset UI
                InvokeIfRequired(lvMacStatus, () =>
                {
                    foreach (ListViewItem item in lvMacStatus.Items)
                    {
                        item.SubItems[2].Text = ErrorCountDefault;
                        item.SubItems[4].Text = ErrorCountDefault;
                    }
                });
                macMismatchCounts.Clear();

                _udpManager.StartPeriodicSend(targetEndPoint!, chkEnableBroadcast.Checked, (int)numInterval.Value, (int)numDummySize.Value, (int)numSendCountLimit.Value, chkContinuousSend.Checked);
            }
        }
        else
        {
            if (!ValidateAndGetTarget(out IPEndPoint? targetEndPoint, isPeriodic: false)) return;
            await _udpManager.SendSingleMessageAsync(targetEndPoint!, chkEnableBroadcast.Checked, txtSendMessage.Text);
        }
    }
    
    private void UdpManager_PeriodicSendStatusChanged(string status)
    {
        InvokeIfRequired(this, () =>
        {
            if (status == "Start")
            {
                _isPeriodicSending = true;
                SetPeriodicSendUIState(isSending: true);
                _periodicCheckTimer.Start();
            }
            else // Stop or Limit Reached
            {
                _isPeriodicSending = false;
                SetPeriodicSendUIState(isSending: false);
                _periodicCheckTimer.Stop();
            }
        });
    }

    private void PeriodicCheckTimer_Tick(object? sender, EventArgs e)
    {
        CleanupOldTimestamps();
        CheckForMissedResponses();
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

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        _udpManager.Dispose();
        _periodicCheckTimer.Dispose();
    }
}