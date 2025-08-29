using System.Net;
using System.Text;

namespace UdpUnicast
{
    /// <summary>
    /// FTEST 프로토콜 메시지를 파싱하는 클래스입니다.
    /// </summary>
    public class FtestProtocolParser
    {
        private const string ResponsePrefix = "[FTEST,0,";
        private const char ResponseSuffix = ']';
        private const int ExpectedParts = 5;

        /// <summary>
        /// 수신된 바이트 배열을 파싱하여 FtestMessage 객체를 생성합니다.
        /// </summary>
        /// <param name="buffer">수신된 데이터 버퍼</param>
        /// <param name="remoteEP">송신자 EndPoint</param>
        /// <returns>파싱에 성공하면 FtestMessage 객체를, 실패하면 null을 반환합니다.</returns>
        public FtestMessage? Parse(byte[] buffer, IPEndPoint remoteEP)
        {
            var rawMessage = Encoding.UTF8.GetString(buffer);

            if (!rawMessage.StartsWith(ResponsePrefix) || !rawMessage.EndsWith(ResponseSuffix))
            {
                return null; // FTEST 프로토콜 메시지가 아님
            }

            string content = rawMessage[ResponsePrefix.Length..^1];
            string[] parts = content.Split(new[] { ',' }, ExpectedParts);

            if (parts.Length == ExpectedParts && int.TryParse(parts[1], out int echoedSeq))
            {
                string mac = parts[0];
                return new FtestMessage(mac, echoedSeq, remoteEP);
            }

            return null;
        }
    }
}
