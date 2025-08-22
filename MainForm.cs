using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using ScottPlot.WinForms;

namespace UdpUnicast
{
    public partial class MainForm : Form
    {
        // --- Data Structures ---
        private readonly ConcurrentDictionary<string, Device> _devices = new();
        private string? currentGraphIp = null;
        private readonly ConcurrentDictionary<string, bool> respondedMacsInCycle = new();

        // --- Dependencies & State ---
        private readonly UdpManager _udpManager;
        private CsvLogger? _csvLogger;
        private bool _isListening = false;
        private bool _isPeriodicSending = false;

        // --- Constants ---
        private const string ResponseTimeDefault = "N/A";

        // --- UI ---
        private ComboBox cmbGraphIp;

        // A private record to hold parsed response data cleanly.
        private record FtestResponse(string Mac, int Seq);

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

        #region UDP Event Handlers & Core Logic

        private void UdpManager_MessageReceived(byte[] buffer, IPEndPoint remoteEP)
        {
            var rawMessage = Encoding.UTF8.GetString(buffer);

            const string ResponsePrefix = "[FTEST,0,";
            if (rawMessage.StartsWith(ResponsePrefix) && rawMessage.EndsWith("]"))
            {
                ProcessFtestResponse(rawMessage, remoteEP);
            }
            else
            {
                AppendLog($"Received from {remoteEP}: {rawMessage}");
            }
        }

        private void ProcessFtestResponse(string rawMessage, IPEndPoint remoteEP)
        {
            const string ResponsePrefix = "[FTEST,0,";
            string content = rawMessage.Substring(ResponsePrefix.Length, rawMessage.Length - ResponsePrefix.Length - 1);
            string[] parts = content.Split(new[] { ',' }, 5);

            if (parts.Length != 5 || !int.TryParse(parts[1], out int echoedSeq))
            {
                AppendLog($"Could not parse FTEST response: {rawMessage}");
                return;
            }

            var response = new FtestResponse(Mac: parts[0], Seq: echoedSeq);
            string ip = remoteEP.Address.ToString();

            var device = AddOrUpdateDevice(response.Mac, ip);
            if (device == null) return;

            if (response.Seq != _udpManager.LastGlobalSentMessageCounter)
            {
                HandleSequenceMismatch(device, response);
            }
            else
            {
                HandleCorrectSequence(device, response, ip);
            }

            RefreshDeviceListViewItem(device);
        }

        private void HandleCorrectSequence(Device device, FtestResponse response, string ip)
        {
            if (respondedMacsInCycle.ContainsKey(device.MacAddress))
            {
                device.OverCount++;
                AppendLog($"Over Received from {ip}");
            }
            respondedMacsInCycle.TryAdd(device.MacAddress, true);

            if (_udpManager.SentMessageTimestamps.TryGetValue(response.Seq, out DateTime sendTime))
            {
                var rtt = (DateTime.UtcNow - sendTime).TotalMilliseconds;
                device.LastResponseTime = rtt;
                device.ResponseData.Add((response.Seq, rtt));

                if (ip == currentGraphIp)
                {
                    UpdateGraph(ip);
                }
                _csvLogger?.LogRecv(ip, response.Seq, rtt);
            }
        }

        private void HandleSequenceMismatch(Device device, FtestResponse response)
        {
            device.MismatchCount++;
            AppendLog($"Delay Received from {device.IpAddress}, recv {response.Seq}, send {_udpManager.LastGlobalSentMessageCounter}");
        }

        #endregion

        #region UI and Device Management

        private Device? AddOrUpdateDevice(string mac, string ipAddress)
        {
            if (_devices.TryGetValue(mac, out var existingDevice))
            {
                if (existingDevice.IpAddress != ipAddress)
                {
                    existingDevice.IpAddress = ipAddress;
                    RefreshDeviceListViewItem(existingDevice);
                }
                return existingDevice;
            }

            Device? newDevice = null;
            InvokeIfRequired(lvMacStatus, () =>
            {
                if (_devices.ContainsKey(mac))
                {
                    newDevice = _devices[mac];
                    return;
                }

                var item = new ListViewItem(mac) { Checked = true };
                item.SubItems.AddRange(new[] { ipAddress, "0", "N/A", "0", "0" });

                newDevice = new Device(mac, ipAddress, item);
                if (_devices.TryAdd(mac, newDevice))
                {
                    AddNewDeviceToUI(newDevice, item);
                }
            });
            return newDevice;
        }

        private void RefreshDeviceListViewItem(Device device)
        {
            if (device?.ListViewItem?.ListView == null) return;

            InvokeIfRequired(device.ListViewItem.ListView, () =>
            {
                device.ListViewItem.SubItems[1].Text = device.IpAddress;
                device.ListViewItem.SubItems[2].Text = device.ErrorCount.ToString();
                device.ListViewItem.SubItems[3].Text = device.LastResponseTime > 0 ? device.LastResponseTime.ToString("F1") : ResponseTimeDefault;
                device.ListViewItem.SubItems[4].Text = device.MismatchCount.ToString();
                device.ListViewItem.SubItems[5].Text = device.OverCount.ToString();
            });
        }

        private void UpdateDeviceCount()
        {
            InvokeIfRequired(lblDeviceCount, () =>
            {
                lblDeviceCount.Text = $"Discovered: {_devices.Count}";
            });
        }

        private void AddNewDeviceToUI(Device newDevice, ListViewItem item)
        {
            lvMacStatus.Items.Add(item);
            AppendLog($"New device discovered: {newDevice.MacAddress} at {newDevice.IpAddress}");
            UpdateDeviceCount();

            bool isNewIp = _devices.Values.Count(d => d.IpAddress == newDevice.IpAddress) == 1;
            if (isNewIp)
            {
                cmbGraphIp.Items.Add(newDevice.IpAddress);
                if (cmbGraphIp.Items.Count == 1)
                {
                    cmbGraphIp.SelectedIndex = 0;
                }
            }
        }

        #endregion

        #region Test & Logging Control

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
                    ResetAllStatistics();
                    _udpManager.StartPeriodicSend(targetEndPoint!, chkEnableBroadcast.Checked, (int)numInterval.Value, (int)numDummySize.Value, (int)numSendCountLimit.Value, chkContinuousSend.Checked);
                }
            }
            else
            {
                if (!ValidateAndGetTarget(out IPEndPoint? targetEndPoint, isPeriodic: false)) return;
                ResetAllStatistics();
                await _udpManager.SendSingleMessageAsync(targetEndPoint!, chkEnableBroadcast.Checked, txtSendMessage.Text);
            }
        }

        private void ResetAllStatistics()
        {
            foreach (var device in _devices.Values)
            {
                device.ResetStatistics();
                RefreshDeviceListViewItem(device);
            }
            respondedMacsInCycle.Clear();

            InvokeIfRequired(cmbGraphIp, () =>
            {
                cmbGraphIp.Items.Clear();
                var uniqueIps = _devices.Values.Select(d => d.IpAddress).Distinct().ToList();
                cmbGraphIp.Items.AddRange(uniqueIps.ToArray());
                currentGraphIp = null;
            });
            formsPlot.Plot.Clear();
            formsPlot.Refresh();
        }

        private void UdpManager_PeriodicSendStatusChanged(string status)
        {
            var parts = status.Split('|');
            var state = parts[0];
            var fileName = parts.Length > 1 ? parts[1] : null;

            if (state == "Start" && fileName != null)
            {
                _csvLogger = new CsvLogger();
                if (!_csvLogger.Start(fileName))
                {
                    AppendLog($"CSV 파일 생성 실패: {fileName}");
                    _csvLogger.Dispose();
                    _csvLogger = null;
                }
            }
            else if (state == "Stop")
            {
                _csvLogger?.WriteErrorLog(_devices.Values);
                _csvLogger?.Dispose();
                _csvLogger = null;
            }

            InvokeIfRequired(this, () =>
            {
                _isPeriodicSending = state == "Start";
                SetPeriodicSendUIState(isSending: _isPeriodicSending);
            });
        }

        private void UdpManager_SendRecvLogCallback(string type, string ip, string? fileName, long sendTimeMs, long? responseTimeMs)
        {
            if (type == "send")
            {
                _csvLogger?.LogSend(ip, _udpManager.LastGlobalSentMessageCounter, sendTimeMs);
            }
        }

        private void CheckForMissedResponses()
        {
            var checkedMacs = lvMacStatus.CheckedItems.Cast<ListViewItem>().Select(item => item.Text).ToList();

            foreach (string mac in checkedMacs)
            {
                if (_devices.TryGetValue(mac, out var device) && !respondedMacsInCycle.ContainsKey(mac))
                {
                    device.ErrorCount++;
                    device.LastResponseTime = -1;
                    RefreshDeviceListViewItem(device);
                    AppendLog($"Don't Received from {device.IpAddress}, count {device.ErrorCount}");
                }
            }
            respondedMacsInCycle.Clear();
        }

        private void CleanupOldTimestamps()
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-5);
            foreach (var pair in _udpManager.SentMessageTimestamps.Where(p => p.Value < cutoff).ToList())
            {
                _udpManager.SentMessageTimestamps.TryRemove(pair.Key, out _);
            }
        }

        #endregion

        #region UI Event Handlers & Helpers

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
                string selectedIp = cmbBindIp.SelectedItem.ToString();
                IPAddress bindIp = selectedIp == "Any IP (0.0.0.0)" ? IPAddress.Any : IPAddress.Parse(selectedIp);

                if (int.TryParse(txtPort.Text, out int port))
                {
                    // UdpManager.StartListener only takes IPAddress, not IPEndPoint.
                    // The port is handled internally by UdpManager (dynamic port 0).
                    // So, we only need to pass the IPAddress to StartListener.
                    _udpManager.StartListener(bindIp);
                }
                else
                {
                    MessageBox.Show("Please enter a valid port.", "Warning");
                }
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

        private void CmbGraphIp_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cmbGraphIp.SelectedItem is string ip)
            {
                currentGraphIp = ip;
                UpdateGraph(ip);
            }
        }

        private void UpdateGraph(string ip)
        {
            InvokeIfRequired(formsPlot, () =>
            {
                formsPlot.Plot.Clear();
                var devicesWithIp = _devices.Values.Where(d => d.IpAddress == ip).ToList();
                if (!devicesWithIp.Any())
                {
                    formsPlot.Refresh();
                    return;
                }

                var allResponseData = devicesWithIp.SelectMany(d => d.ResponseData).OrderBy(d => d.seq).ToList();
                if (!allResponseData.Any())
                {
                    formsPlot.Refresh();
                    return;
                }

                double[] xs = allResponseData.Select(d => (double)d.seq).ToArray();
                double[] ys = allResponseData.Select(d => d.rtt).ToArray();

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

        private void AppendLog(string message)
        {
            InvokeIfRequired(txtLog, () =>
            {
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            });
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try { _udpManager.Dispose(); } catch { }
        }

        private void InvokeIfRequired(Control control, Action action)
        {
            if (control.InvokeRequired) control.Invoke(action);
            else action();
        }

        #endregion
    }
}
