namespace UdpUnicast
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            lblIpAddress = new Label();
            txtIpAddress = new TextBox();
            lblPort = new Label();
            txtPort = new TextBox();
            lblSendMessage = new Label();
            txtSendMessage = new TextBox();
            btnSend = new Button();
            grpSettings = new GroupBox();
            btnStartStopListen = new Button();
            chkEnableBroadcast = new CheckBox();
            cmbBindIp = new ComboBox();
            lblBindIp = new Label();
            lblDeviceCount = new Label();
            grpSend = new GroupBox();
            lblDummySize = new Label();
            numDummySize = new NumericUpDown();
            lblInterval = new Label();
            numInterval = new NumericUpDown();
            chkPeriodicSend = new CheckBox();
            splitContainer = new SplitContainer();
            lvMacStatus = new ListView();
            colMacAddress = new ColumnHeader();
            colIpAddress = new ColumnHeader();
            colErrorCount = new ColumnHeader();
            colResponseTime = new ColumnHeader();
            colMismatchCount = new ColumnHeader();
            grpLog = new GroupBox();
            txtLog = new TextBox();
            grpSettings.SuspendLayout();
            grpSend.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numDummySize).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numInterval).BeginInit();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            grpLog.SuspendLayout();
            SuspendLayout();
            // 
            // lblIpAddress
            // 
            lblIpAddress.AutoSize = true;
            lblIpAddress.Location = new Point(15, 93);
            lblIpAddress.Name = "lblIpAddress";
            lblIpAddress.Size = new Size(85, 15);
            lblIpAddress.TabIndex = 0;
            lblIpAddress.Text = "Destination IP:";
            // 
            // txtIpAddress
            // 
            txtIpAddress.Location = new Point(110, 90);
            txtIpAddress.Name = "txtIpAddress";
            txtIpAddress.Size = new Size(150, 23);
            txtIpAddress.TabIndex = 4;
            txtIpAddress.Text = "127.0.0.1";
            // 
            // lblPort
            // 
            lblPort.AutoSize = true;
            lblPort.Location = new Point(280, 93);
            lblPort.Name = "lblPort";
            lblPort.Size = new Size(32, 15);
            lblPort.TabIndex = 2;
            lblPort.Text = "Port:";
            // 
            // txtPort
            // 
            txtPort.Location = new Point(318, 90);
            txtPort.Name = "txtPort";
            txtPort.Size = new Size(70, 23);
            txtPort.TabIndex = 5;
            txtPort.Text = "9123";
            // 
            // lblSendMessage
            // 
            lblSendMessage.AutoSize = true;
            lblSendMessage.Location = new Point(15, 60);
            lblSendMessage.Name = "lblSendMessage";
            lblSendMessage.Size = new Size(112, 15);
            lblSendMessage.TabIndex = 0;
            lblSendMessage.Text = "Discovery Message:";
            // 
            // txtSendMessage
            // 
            txtSendMessage.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtSendMessage.Location = new Point(18, 80);
            txtSendMessage.Multiline = true;
            txtSendMessage.Name = "txtSendMessage";
            txtSendMessage.Size = new Size(787, 50);
            txtSendMessage.TabIndex = 3;
            txtSendMessage.Text = "0";
            // 
            // btnSend
            // 
            btnSend.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSend.Location = new Point(711, 136);
            btnSend.Name = "btnSend";
            btnSend.Size = new Size(94, 29);
            btnSend.TabIndex = 4;
            btnSend.Text = "Send";
            btnSend.UseVisualStyleBackColor = true;
            btnSend.Click += btnSend_Click;
            // 
            // grpSettings
            // 
            grpSettings.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grpSettings.Controls.Add(btnStartStopListen);
            grpSettings.Controls.Add(chkEnableBroadcast);
            grpSettings.Controls.Add(cmbBindIp);
            grpSettings.Controls.Add(lblBindIp);
            grpSettings.Controls.Add(lblIpAddress);
            grpSettings.Controls.Add(txtIpAddress);
            grpSettings.Controls.Add(lblPort);
            grpSettings.Controls.Add(txtPort);
            grpSettings.Controls.Add(lblDeviceCount);
            grpSettings.Location = new Point(12, 12);
            grpSettings.Name = "grpSettings";
            grpSettings.Size = new Size(826, 130);
            grpSettings.TabIndex = 0;
            grpSettings.TabStop = false;
            grpSettings.Text = "Connection Info";
            // 
            // btnStartStopListen
            // 
            btnStartStopListen.Location = new Point(110, 55);
            btnStartStopListen.Name = "btnStartStopListen";
            btnStartStopListen.Size = new Size(75, 23);
            btnStartStopListen.TabIndex = 2;
            btnStartStopListen.Text = "Start";
            btnStartStopListen.UseVisualStyleBackColor = true;
            btnStartStopListen.Click += btnStartStopListen_Click;
            // 
            // chkEnableBroadcast
            // 
            chkEnableBroadcast.AutoSize = true;
            chkEnableBroadcast.Checked = true;
            chkEnableBroadcast.CheckState = CheckState.Checked;
            chkEnableBroadcast.Location = new Point(283, 24);
            chkEnableBroadcast.Name = "chkEnableBroadcast";
            chkEnableBroadcast.Size = new Size(117, 19);
            chkEnableBroadcast.TabIndex = 3;
            chkEnableBroadcast.Text = "Enable Broadcast";
            chkEnableBroadcast.UseVisualStyleBackColor = true;
            chkEnableBroadcast.CheckedChanged += chkEnableBroadcast_CheckedChanged;
            // 
            // cmbBindIp
            // 
            cmbBindIp.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbBindIp.FormattingEnabled = true;
            cmbBindIp.Location = new Point(110, 22);
            cmbBindIp.Name = "cmbBindIp";
            cmbBindIp.Size = new Size(150, 23);
            cmbBindIp.TabIndex = 1;
            // 
            // lblBindIp
            // 
            lblBindIp.AutoSize = true;
            lblBindIp.Location = new Point(15, 25);
            lblBindIp.Name = "lblBindIp";
            lblBindIp.Size = new Size(48, 15);
            lblBindIp.TabIndex = 4;
            lblBindIp.Text = "Bind IP:";
            // 
            // lblDeviceCount
            // 
            lblDeviceCount.AutoSize = true;
            lblDeviceCount.Location = new Point(191, 60);
            lblDeviceCount.Name = "lblDeviceCount";
            lblDeviceCount.Size = new Size(80, 15);
            lblDeviceCount.TabIndex = 6;
            lblDeviceCount.Text = "Discovered: 0";
            // 
            // grpSend
            // 
            grpSend.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grpSend.Controls.Add(lblDummySize);
            grpSend.Controls.Add(numDummySize);
            grpSend.Controls.Add(lblInterval);
            grpSend.Controls.Add(numInterval);
            grpSend.Controls.Add(chkPeriodicSend);
            grpSend.Controls.Add(lblSendMessage);
            grpSend.Controls.Add(txtSendMessage);
            grpSend.Controls.Add(btnSend);
            grpSend.Location = new Point(12, 148);
            grpSend.Name = "grpSend";
            grpSend.Size = new Size(823, 175);
            grpSend.TabIndex = 1;
            grpSend.TabStop = false;
            grpSend.Text = "Send Message";
            // 
            // lblDummySize
            // 
            lblDummySize.AutoSize = true;
            lblDummySize.Location = new Point(318, 28);
            lblDummySize.Name = "lblDummySize";
            lblDummySize.Size = new Size(80, 15);
            lblDummySize.TabIndex = 8;
            lblDummySize.Text = "Dummy Size:";
            // 
            // numDummySize
            // 
            numDummySize.Location = new Point(399, 26);
            numDummySize.Maximum = new decimal(new int[] { 500, 0, 0, 0 });
            numDummySize.Minimum = new decimal(new int[] { 100, 0, 0, 0 });
            numDummySize.Name = "numDummySize";
            numDummySize.Size = new Size(70, 23);
            numDummySize.TabIndex = 9;
            numDummySize.Value = new decimal(new int[] { 100, 0, 0, 0 });
            // 
            // lblInterval
            // 
            lblInterval.AutoSize = true;
            lblInterval.Location = new Point(140, 28);
            lblInterval.Name = "lblInterval";
            lblInterval.Size = new Size(77, 15);
            lblInterval.TabIndex = 7;
            lblInterval.Text = "Interval (ms):";
            // 
            // numInterval
            // 
            numInterval.Location = new Point(225, 26);
            numInterval.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            numInterval.Minimum = new decimal(new int[] { 50, 0, 0, 0 });
            numInterval.Name = "numInterval";
            numInterval.Size = new Size(70, 23);
            numInterval.TabIndex = 2;
            numInterval.Value = new decimal(new int[] { 100, 0, 0, 0 });
            // 
            // chkPeriodicSend
            // 
            chkPeriodicSend.AutoSize = true;
            chkPeriodicSend.Location = new Point(18, 27);
            chkPeriodicSend.Name = "chkPeriodicSend";
            chkPeriodicSend.Size = new Size(118, 19);
            chkPeriodicSend.TabIndex = 1;
            chkPeriodicSend.Text = "Send Periodically";
            chkPeriodicSend.UseVisualStyleBackColor = true;
            chkPeriodicSend.CheckedChanged += chkPeriodicSend_CheckedChanged;
            // 
            // splitContainer
            // 
            splitContainer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            splitContainer.Location = new Point(12, 329);
            splitContainer.Name = "splitContainer";
            splitContainer.Orientation = Orientation.Horizontal;
            // 
            // splitContainer.Panel1
            // 
            splitContainer.Panel1.Controls.Add(lvMacStatus);
            // 
            // splitContainer.Panel2
            // 
            splitContainer.Panel2.Controls.Add(grpLog);
            splitContainer.Size = new Size(826, 461);
            splitContainer.SplitterDistance = 228;
            splitContainer.TabIndex = 3;
            // 
            // lvMacStatus
            // 
            lvMacStatus.CheckBoxes = true;
            lvMacStatus.Columns.AddRange(new ColumnHeader[] { colMacAddress, colIpAddress, colErrorCount, colResponseTime, colMismatchCount });
            lvMacStatus.Dock = DockStyle.Fill;
            lvMacStatus.Location = new Point(0, 0);
            lvMacStatus.Name = "lvMacStatus";
            lvMacStatus.Size = new Size(826, 228);
            lvMacStatus.TabIndex = 0;
            lvMacStatus.UseCompatibleStateImageBehavior = false;
            lvMacStatus.View = View.Details;
            // 
            // colMacAddress
            // 
            colMacAddress.Text = "MAC Address";
            colMacAddress.Width = 150;
            // 
            // colIpAddress
            // 
            colIpAddress.Text = "IP Address";
            colIpAddress.Width = 120;
            // 
            // colErrorCount
            // 
            colErrorCount.Text = "Error Count";
            colErrorCount.Width = 80;
            // 
            // colResponseTime
            // 
            colResponseTime.Text = "Response Time (ms)";
            colResponseTime.Width = 120;
            // 
            // colMismatchCount
            // 
            colMismatchCount.Text = "Mismatch Count";
            colMismatchCount.Width = 100;
            // 
            // grpLog
            // 
            grpLog.Controls.Add(txtLog);
            grpLog.Dock = DockStyle.Fill;
            grpLog.Location = new Point(0, 0);
            grpLog.Name = "grpLog";
            grpLog.Size = new Size(826, 229);
            grpLog.TabIndex = 0;
            grpLog.TabStop = false;
            grpLog.Text = "Log";
            // 
            // txtLog
            // 
            txtLog.BackColor = SystemColors.Window;
            txtLog.Dock = DockStyle.Fill;
            txtLog.Location = new Point(3, 19);
            txtLog.Multiline = true;
            txtLog.Name = "txtLog";
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Size = new Size(820, 207);
            txtLog.TabIndex = 0;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(850, 802);
            Controls.Add(splitContainer);
            Controls.Add(grpSend);
            Controls.Add(grpSettings);
            MinimumSize = new Size(540, 630);
            Name = "MainForm";
            Text = "UDP Broadcast/Unicast Tool";
            FormClosing += MainForm_FormClosing;
            Load += MainForm_Load;
            grpSettings.ResumeLayout(false);
            grpSettings.PerformLayout();
            grpSend.ResumeLayout(false);
            grpSend.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numDummySize).EndInit();
            ((System.ComponentModel.ISupportInitialize)numInterval).EndInit();
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            grpLog.ResumeLayout(false);
            grpLog.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Label lblIpAddress;
        private System.Windows.Forms.TextBox txtIpAddress;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.TextBox txtPort;
        private System.Windows.Forms.Label lblSendMessage;
        private System.Windows.Forms.TextBox txtSendMessage;
        private System.Windows.Forms.Button btnSend;
        private System.Windows.Forms.GroupBox grpSettings;
        private System.Windows.Forms.GroupBox grpSend;
        private System.Windows.Forms.GroupBox grpLog;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Label lblBindIp;
        private System.Windows.Forms.ComboBox cmbBindIp;
        private System.Windows.Forms.CheckBox chkEnableBroadcast;
        private System.Windows.Forms.Button btnStartStopListen;
        private System.Windows.Forms.CheckBox chkPeriodicSend;
        private System.Windows.Forms.NumericUpDown numInterval;
        private System.Windows.Forms.Label lblInterval;
        private System.Windows.Forms.Label lblDummySize;
        private System.Windows.Forms.NumericUpDown numDummySize;
        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.ListView lvMacStatus;
        private System.Windows.Forms.ColumnHeader colMacAddress;
        private System.Windows.Forms.ColumnHeader colIpAddress;
        private System.Windows.Forms.ColumnHeader colErrorCount;
        private System.Windows.Forms.ColumnHeader colResponseTime;
        private System.Windows.Forms.ColumnHeader colMismatchCount;
        private System.Windows.Forms.Label lblDeviceCount;
    }
}