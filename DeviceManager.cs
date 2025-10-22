using System.Collections.Concurrent;
using System.Windows.Forms;

namespace UdpUnicast
{
    /// <summary>
    /// 장치(MAC, IP, 통계 등) 관리 전담 클래스
    /// </summary>
    public class DeviceManager
    {
        public ConcurrentDictionary<string, ListViewItem> MacListViewItems { get; } = new();
        public ConcurrentDictionary<string, int> RespondedMacsInCycle { get; } = new();
        public const string ErrorCountDefault = "0";
        public const string ResponseTimeDefault = "N/A";
        public const string TimeoutText = "Timeout";

        public void AddOrUpdateMac(ListView lvMacStatus, string mac, string ipAddress, Action<string> logCallback, Action updateDeviceCount)
        {
            if (MacListViewItems.TryGetValue(ipAddress, out var existingItem))
            {
                if (existingItem.SubItems[1].Text != mac)
                {
                    if (existingItem.ListView != null)
                        InvokeIfRequired(existingItem.ListView, () => existingItem.SubItems[1].Text = mac);
                }
                return;
            }
            InvokeIfRequired(lvMacStatus, () =>
            {
                if (MacListViewItems.ContainsKey(ipAddress)) return;
                var item = new ListViewItem(ipAddress) { Checked = true };
                item.SubItems.Add(mac);
                item.SubItems.Add(ErrorCountDefault);
                item.SubItems.Add(ResponseTimeDefault);
                item.SubItems.Add(ErrorCountDefault);
                item.SubItems.Add(ErrorCountDefault);
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                lvMacStatus.Items.Add(item);
                MacListViewItems.TryAdd(ipAddress, item);
                logCallback?.Invoke($"New device discovered: {mac} at {ipAddress}");
                updateDeviceCount?.Invoke();
            });
        }

        public void UpdateDeviceCount(Label lblDeviceCount)
        {
            InvokeIfRequired(lblDeviceCount, () =>
            {
                lblDeviceCount.Text = $"Discovered: {MacListViewItems.Count}";
            });
        }

        public void UpdateOverCount(string ipAddress)
        {
            if (MacListViewItems.TryGetValue(ipAddress, out var item))
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

        public void UpdateMismatchCount(string ipAddress)
        {
            if (MacListViewItems.TryGetValue(ipAddress, out var item))
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

        public void UpdateDeviceResponseTime(string ipAddress, double rtt)
        {
            if (MacListViewItems.TryGetValue(ipAddress, out var item))
            {
                if (item.ListView != null)
                    InvokeIfRequired(item.ListView, () => item.SubItems[3].Text = rtt.ToString("F1"));
            }
        }

        public void ResetDeviceErrorAndMismatch(ListView lvMacStatus)
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

        public void CheckForMissedResponses(ListView lvMacStatus, Action<string> logCallback,int LastGlobalSentMessageCounter)
        {
            List<ListViewItem> checkedItems = new List<ListViewItem>();
            InvokeIfRequired(lvMacStatus, () => checkedItems = lvMacStatus.CheckedItems.Cast<ListViewItem>().ToList());
            foreach (var item in checkedItems)
            {
                string ipAddress = item.Text;
                if (!RespondedMacsInCycle.ContainsKey(ipAddress))
                {
                    if (item.ListView != null)
                        InvokeIfRequired(item.ListView, () =>
                        {
                            int currentErrors = int.Parse(item.SubItems[2].Text);
                            item.SubItems[2].Text = (currentErrors + 1).ToString();
                            item.SubItems[3].Text = TimeoutText;
                        });
                    logCallback?.Invoke($"Don't Received from {item.SubItems[1].Text}, count {item.SubItems[2].Text}, send {(LastGlobalSentMessageCounter).ToString()}");
                }
            }
            RespondedMacsInCycle.Clear();
        }

        public void UpdatePcir(PcirMessage message)
        {
            if (MacListViewItems.TryGetValue(message.SourceIp, out var item))
            {
                InvokeIfRequired(item.ListView!, () =>
                {
                    item.SubItems[6].Text = message.Id.ToString();
                    item.SubItems[7].Text = message.LinkFailCount.ToString();
                    item.SubItems[8].Text = message.MaxCycle.ToString();
                    item.SubItems[9].Text = message.MinCycle.ToString();
                    item.SubItems[10].Text = message.Over15msCycle.ToString();
                    item.SubItems[11].Text = message.Over20msCycle.ToString();
                    item.SubItems[12].Text = message.Over25msCycle.ToString();
                    item.SubItems[13].Text = message.Over30msCycle.ToString();
                    item.SubItems[14].Text = message.CommRecvCount.ToString();
                    item.SubItems[15].Text = message.CommRecvDoubleCount.ToString();
                    item.SubItems[16].Text = message.CommRecvFailCount.ToString();
                });
            }
        }

        public void UpdateInitPcir(ListView lvMacStatus)
        {
            InvokeIfRequired(lvMacStatus, () =>
            {
                foreach (ListViewItem item in lvMacStatus.Items)
                {
                    item.SubItems[6].Text = "-";
                    item.SubItems[7].Text = "-";
                    item.SubItems[8].Text = "-";
                    item.SubItems[9].Text = "-";
                    item.SubItems[10].Text = "-";
                    item.SubItems[11].Text = "-";
                    item.SubItems[12].Text = "-";
                    item.SubItems[13].Text = "-";
                    item.SubItems[14].Text = "-";
                    item.SubItems[15].Text = "-";
                    item.SubItems[16].Text = "-";
                }
            });
            
        }

        private void InvokeIfRequired(Control control, Action action)
        {
            if (control.InvokeRequired) control.Invoke(action);
            else action();
        }
    }
}
