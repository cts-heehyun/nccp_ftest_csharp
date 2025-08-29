using System.Net;

namespace UdpUnicast
{
    /// <summary>
    /// 파싱된 FTEST 프로토콜 응답 메시지를 나타내는 데이터 클래스입니다.
    /// </summary>
    public class FtestMessage
    {
        public string Mac { get; }
        public int EchoedSequence { get; }
        public string SourceIp { get; }
        public IPEndPoint RemoteEP { get; }

        public FtestMessage(string mac, int echoedSequence, IPEndPoint remoteEP)
        {
            Mac = mac;
            EchoedSequence = echoedSequence;
            SourceIp = remoteEP.Address.ToString();
            RemoteEP = remoteEP;
        }
    }
}
