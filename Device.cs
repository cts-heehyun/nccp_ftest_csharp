using System.Collections.Generic;
using System.Windows.Forms;

namespace UdpUnicast
{
    public class Device
    {
        public string MacAddress { get; }
        public string IpAddress { get; set; }
        public ListViewItem ListViewItem { get; }

        // Statistics
        public int ErrorCount { get; set; }
        public int MismatchCount { get; set; }
        public int OverCount { get; set; }
        public double LastResponseTime { get; set; }

        // Graphing Data
        public List<(int seq, double rtt)> ResponseData { get; } = new List<(int, double)>();

        public Device(string macAddress, string ipAddress, ListViewItem item)
        {
            MacAddress = macAddress;
            IpAddress = ipAddress;
            ListViewItem = item;
            LastResponseTime = -1; // Default value
        }

        public void ResetStatistics()
        {
            ErrorCount = 0;
            MismatchCount = 0;
            OverCount = 0;
            LastResponseTime = -1;
            ResponseData.Clear();
        }
    }
}
