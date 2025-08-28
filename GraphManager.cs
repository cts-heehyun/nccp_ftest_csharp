using System.Collections.Generic;
using System.Linq;
using ScottPlot.WinForms;

namespace UdpUnicast
{
    /// <summary>
    /// IP별 응답 시간 그래프 데이터 및 시각화 관리 클래스
    /// </summary>
    public class GraphManager
    {
        private readonly Dictionary<string, List<(int seq, double rtt)>> ipResponseData = new();
        private string? currentGraphIp = null;
        private const int MaxGraphPointsPerIp = 65535;

        public int Y_Max_limit { get; set; } = 200;

        public void AddResponse(string ip, int seq, double rtt)
        {
            lock (ipResponseData)
            {
                if (!ipResponseData.ContainsKey(ip))
                    ipResponseData[ip] = new List<(int, double)>();
                ipResponseData[ip].Add((seq, rtt));
                if (ipResponseData[ip].Count > MaxGraphPointsPerIp)
                {
                    int overflow = ipResponseData[ip].Count - MaxGraphPointsPerIp;
                    ipResponseData[ip].RemoveRange(0, overflow);
                }
            }
        }

        public void Reset()
        {
            lock (ipResponseData)
            {
                ipResponseData.Clear();
            }
            currentGraphIp = null;
        }

        public IEnumerable<string> GetAllIps()
        {
            lock (ipResponseData)
            {
                return ipResponseData.Keys.ToList();
            }
        }

        public void SetCurrentGraphIp(string ip)
        {
            currentGraphIp = ip;
        }

        public void UpdateGraph(FormsPlot formsPlot)
        {
            if (currentGraphIp == null) return;
            lock (ipResponseData)
            {
                formsPlot.Plot.Clear();
                if (!ipResponseData.ContainsKey(currentGraphIp) || ipResponseData[currentGraphIp].Count == 0)
                {
                    formsPlot.Refresh();
                    return;
                }
                var data = ipResponseData[currentGraphIp];
                double[] xs = data.Select(d => (double)d.seq).ToArray();
                double[] ys = data.Select(d => d.rtt).ToArray();
                var scatter = formsPlot.Plot.Add.Scatter(xs, ys, color: ScottPlot.Colors.Blue);
                scatter.MarkerSize = 5;
                scatter.LineWidth = 0;
                formsPlot.Plot.Title($"Response Time (IP: {currentGraphIp})");
                formsPlot.Plot.XLabel("Sequence Number");
                formsPlot.Plot.YLabel("Response Time (ms)");
                //formsPlot.Plot.Axes.AutoScale();
                formsPlot.Plot.Axes.SetLimits(0, ipResponseData[currentGraphIp].Count, 0, Y_Max_limit);
                formsPlot.Refresh();
            }
        }
    }
}
