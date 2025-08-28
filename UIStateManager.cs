using System.Windows.Forms;

namespace UdpUnicast
{
    /// <summary>
    /// UI 상태 관리 및 컨트롤 활성화/비활성화, 텍스트 등 관리 클래스
    /// </summary>
    public class UIStateManager
    {
        public void SetPeriodicSendUIState(
            Form form,
            bool isPeriodicChecked,
            bool isContinuousChecked,
            bool isSending,
            Label lblInterval,
            NumericUpDown numInterval,
            Label lblDummySize,
            NumericUpDown numDummySize,
            Label lblSendCountLimit,
            NumericUpDown numSendCountLimit,
            TextBox txtSendMessage,
            Label lblSendMessage,
            Button btnSend,
            CheckBox chkContinuousSend)
        {
            if (form.InvokeRequired)
            {
                form.Invoke(new MethodInvoker(() => SetPeriodicSendUIState(form, isPeriodicChecked, isContinuousChecked, isSending, lblInterval, numInterval, lblDummySize, numDummySize, lblSendCountLimit, numSendCountLimit, txtSendMessage, lblSendMessage, btnSend, chkContinuousSend)));
                return;
            }
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
        }
    }
}
