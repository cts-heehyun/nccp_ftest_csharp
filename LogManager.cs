using System;
using System.IO;
using System.Text;

namespace UdpUnicast
{
    /// <summary>
    /// CSV 및 텍스트 로그 파일 관리 클래스
    /// </summary>
    public class LogManager : IDisposable
    {
        private StreamWriter? _logWriter = null;
        private readonly object _logLock = new();
        public string? CurrentLogFileName { get; private set; }

        public void StartLog(string fileName)
        {
            lock (_logLock)
            {
                CurrentLogFileName = fileName;
                _logWriter = new StreamWriter(fileName, false, Encoding.UTF8);
                _logWriter.WriteLine("type,ip,seq,sendTimeMs,responseTimeMs");
            }
        }

        public void WriteLog(string line)
        {
            lock (_logLock)
            {
                _logWriter?.WriteLine(line);
            }
        }

        public void StopLog()
        {
            lock (_logLock)
            {
                if (_logWriter != null)
                {
                    try { _logWriter.Flush(); _logWriter.Close(); } catch { }
                    _logWriter = null;
                }
            }
        }

        public void Dispose()
        {
            StopLog();
        }
    }
}
