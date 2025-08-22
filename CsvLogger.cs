using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UdpUnicast
{
    public class CsvLogger : IDisposable
    {
        private StreamWriter? _logWriter;
        private string? _errorLogFileName;

        private readonly Dictionary<(string ip, int seq), long> _sendLogMap = new();

        public bool Start(string baseFileName)
        {
            try
            {
                _logWriter = new StreamWriter(baseFileName, false, Encoding.UTF8);
                _logWriter.WriteLine("type,ip,seq,sendTime,responseTimeMs");
                _errorLogFileName = Path.ChangeExtension(baseFileName, ".err.csv");
                return true;
            }
            catch
            {
                Dispose();
                return false;
            }
        }

        public void LogSend(string ip, int seq, long sendTimeMs)
        {
            if (_logWriter == null) return;

            _sendLogMap[(ip, seq)] = sendTimeMs;
            var sendTime = DateTimeOffset.FromUnixTimeMilliseconds(sendTimeMs).ToLocalTime().DateTime;
            _logWriter.WriteLine($"send,{ip},{seq},{sendTime:HH:mm:ss.fff},");
        }

        public void LogRecv(string ip, int seq, double rtt)
        {
            if (_logWriter == null) return;

            string sendTimeStr = string.Empty;
            if (_sendLogMap.TryGetValue((ip, seq), out long sendMs))
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(sendMs).ToLocalTime().DateTime;
                sendTimeStr = dt.ToString("HH:mm:ss.fff");
            }
            
            string rttStr = rtt.ToString("F1");
            _logWriter.WriteLine($"recv,{ip},{seq},{sendTimeStr},{rttStr}");
        }

        public void WriteErrorLog(IEnumerable<Device> devices)
        {
            if (string.IsNullOrEmpty(_errorLogFileName)) return;

            try
            {
                using var errWriter = new StreamWriter(_errorLogFileName, false, Encoding.UTF8);
                errWriter.WriteLine("mac,ip,errorCount,mismatchCount,overCount");
                foreach (var device in devices.OrderBy(d => d.MacAddress))
                {
                    errWriter.WriteLine($"{device.MacAddress},{device.IpAddress},{device.ErrorCount},{device.MismatchCount},{device.OverCount}");
                }
            }
            catch
            {
                // Silently fail if error log cannot be written.
            }
        }

        public void Dispose()
        {
            _logWriter?.Flush();
            _logWriter?.Close();
            _logWriter?.Dispose();
            _logWriter = null;
        }
    }
}
