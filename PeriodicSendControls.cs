namespace UdpUnicast
{
    /// <summary>
    /// 주기적 전송 관련 UI 컨트롤을 묶는 DTO
    /// </summary>
    public class PeriodicSendControls
    {
        public System.Windows.Forms.Label LblInterval { get; set; }
        public System.Windows.Forms.NumericUpDown NumInterval { get; set; }
        public System.Windows.Forms.Label LblDummySize { get; set; }
        public System.Windows.Forms.NumericUpDown NumDummySize { get; set; }
        public System.Windows.Forms.Label LblSendCountLimit { get; set; }
        public System.Windows.Forms.NumericUpDown NumSendCountLimit { get; set; }
        public System.Windows.Forms.TextBox TxtSendMessage { get; set; }
        public System.Windows.Forms.Label LblSendMessage { get; set; }
        public System.Windows.Forms.Button BtnSend { get; set; }
        public System.Windows.Forms.CheckBox ChkContinuousSend { get; set; }
    }
}
