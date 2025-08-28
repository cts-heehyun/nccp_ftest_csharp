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
        public ConcurrentDictionary<string, bool> RespondedMacsInCycle { get; } = new();
        public const string ErrorCountDefault = "0";
        public const string ResponseTimeDefault = "N/A";
        public const string TimeoutText = "Timeout";

        public void AddOrUpdateMac(ListView lvMacStatus, string mac, string ipAddress, Action<string> logCallback, Action updateDeviceCount)
        {
            if (MacListViewItems.TryGetValue(mac, out var existingItem))
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
                if (MacListViewItems.ContainsKey(mac)) return;
                var item = new ListViewItem(mac) { Checked = true };
                item.SubItems.Add(ipAddress);
                item.SubItems.Add(ErrorCountDefault);
                item.SubItems.Add(ResponseTimeDefault);
                item.SubItems.Add(ErrorCountDefault);
                item.SubItems.Add(ErrorCountDefault);
                lvMacStatus.Items.Add(item);
                MacListViewItems.TryAdd(mac, item);
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

        public void UpdateOverCount(string mac)
        {
            if (MacListViewItems.TryGetValue(mac, out var item))
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

        public void UpdateMismatchCount(string mac)
        {
            if (MacListViewItems.TryGetValue(mac, out var item))
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

        public void UpdateDeviceResponseTime(string mac, double rtt)
        {
            if (MacListViewItems.TryGetValue(mac, out var item))
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

        public void CheckForMissedResponses(ListView lvMacStatus, Action<string> logCallback)
        {
            List<ListViewItem> checkedItems = new List<ListViewItem>();
            InvokeIfRequired(lvMacStatus, () => checkedItems = lvMacStatus.CheckedItems.Cast<ListViewItem>().ToList());
            foreach (var item in checkedItems)
            {
                string mac = item.Text;
                if (!RespondedMacsInCycle.ContainsKey(mac))
                {
                    if (item.ListView != null)
                        InvokeIfRequired(item.ListView, () =>
                        {
                            int currentErrors = int.Parse(item.SubItems[2].Text);
                            item.SubItems[2].Text = (currentErrors + 1).ToString();
                            item.SubItems[3].Text = TimeoutText;
                        });
                    logCallback?.Invoke($"Don't Received from {item.SubItems[1].Text}, count {item.SubItems[2].Text}");
                }
            }
            RespondedMacsInCycle.Clear();
        }

        private void InvokeIfRequired(Control control, Action action)
        {
            if (control.InvokeRequired) control.Invoke(action);
            else action();
        }
    }
}
