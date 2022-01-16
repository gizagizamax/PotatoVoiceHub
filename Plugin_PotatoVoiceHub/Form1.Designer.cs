namespace Plugin_PotatoVoiceHub
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.label1 = new System.Windows.Forms.Label();
            this.txtHttpPort = new System.Windows.Forms.TextBox();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.chkUseReplace = new System.Windows.Forms.CheckBox();
            this.label4 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(3, 5);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(33, 12);
            this.label1.TabIndex = 1;
            this.label1.Text = "ポート";
            // 
            // txtHttpPort
            // 
            this.txtHttpPort.Location = new System.Drawing.Point(42, 2);
            this.txtHttpPort.Name = "txtHttpPort";
            this.txtHttpPort.Size = new System.Drawing.Size(100, 19);
            this.txtHttpPort.TabIndex = 2;
            this.txtHttpPort.TextChanged += new System.EventHandler(this.txtHttpPort_TextChanged);
            // 
            // txtLog
            // 
            this.txtLog.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtLog.Location = new System.Drawing.Point(2, 64);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(450, 83);
            this.txtLog.TabIndex = 2;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(3, 49);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(23, 12);
            this.label3.TabIndex = 1;
            this.label3.Text = "ログ";
            // 
            // label2
            // 
            this.label2.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.label2.Location = new System.Drawing.Point(2, 23);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(448, 2);
            this.label2.TabIndex = 3;
            // 
            // chkUseReplace
            // 
            this.chkUseReplace.AutoSize = true;
            this.chkUseReplace.Location = new System.Drawing.Point(42, 28);
            this.chkUseReplace.Name = "chkUseReplace";
            this.chkUseReplace.Size = new System.Drawing.Size(172, 16);
            this.chkUseReplace.TabIndex = 4;
            this.chkUseReplace.Text = "棒読みちゃんの辞書変換を使う";
            this.chkUseReplace.UseVisualStyleBackColor = true;
            this.chkUseReplace.CheckedChanged += new System.EventHandler(this.chkUseReplace_CheckedChanged);
            // 
            // label4
            // 
            this.label4.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.label4.Location = new System.Drawing.Point(2, 47);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(446, 2);
            this.label4.TabIndex = 3;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(453, 149);
            this.Controls.Add(this.chkUseReplace);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.txtHttpPort);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.label3);
            this.Name = "Form1";
            this.Text = "PluginPotatoVoice";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtHttpPort;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.CheckBox chkUseReplace;
        private System.Windows.Forms.Label label4;
    }
}