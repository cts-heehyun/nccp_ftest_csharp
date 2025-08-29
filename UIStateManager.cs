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
            PeriodicSendControls controls)
        {
            if (form.InvokeRequired)
            {
                form.Invoke(new MethodInvoker(() => SetPeriodicSendUIState(form, isPeriodicChecked, isContinuousChecked, isSending, controls)));
                return;
            }
            controls.LblInterval.Enabled = isPeriodicChecked;
            controls.NumInterval.Enabled = isPeriodicChecked && !isSending;
            controls.LblDummySize.Enabled = isPeriodicChecked;
            controls.NumDummySize.Enabled = isPeriodicChecked && !isSending;
            controls.LblSendCountLimit.Enabled = isPeriodicChecked && !isContinuousChecked;
            controls.NumSendCountLimit.Enabled = isPeriodicChecked && !isContinuousChecked && !isSending;
            controls.TxtSendMessage.Enabled = !isPeriodicChecked;
            controls.LblSendMessage.Enabled = !isPeriodicChecked;
            controls.BtnSend.Text = isPeriodicChecked ? (isSending ? "Stop" : "Start") : "Send";
            controls.ChkContinuousSend.Enabled = isPeriodicChecked;
        }
    }
}
