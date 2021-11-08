using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web;
using System.Windows.Forms;

namespace VoiceroidTalkPlusReceiverHub
{
    public partial class Form1 : Form
    {
        [DllImport("User32.dll", EntryPoint = "SendMessage")]
        public static extern int SendMessages(
            IntPtr hWnd,
            int Msg,
            int wParam,
            //            ref COPYDATASTRUCT lParam);
            ref COPYDATASTRUCT lParam);

        [DllImport("User32.dll", EntryPoint = "FindWindow")]
        public static extern IntPtr FindWindows(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        static extern bool SetWindowText(IntPtr hWnd, string lpString);

        public struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public string lpData;
        }

        private Thread threadRename;
        private IntPtr hwndOrg = IntPtr.Zero;
        private List<string> listLog = new List<string>();

        public Form1()
        {
            InitializeComponent();
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 74)
            {
                Type type = new COPYDATASTRUCT().GetType();
                var lParam = (COPYDATASTRUCT)m.GetLParam(type);
                var txtOrg = lParam.lpData;

                try
                {
                    WriteLog("受信：" + txtOrg);
                    if (hwndOrg != IntPtr.Zero)
                    {
                        var isSendMessage = true;
                        if (txtOrg.StartsWith("A.I.VOICE "))
                        {
                            var txtSubstr = txtOrg.Substring(10);
                            while (true)
                            {
                                string html;
                                using (var st = WebRequest.Create("http://localhost:" + txtPort.Text + "?text=" + HttpUtility.UrlEncode(txtSubstr)).GetResponse().GetResponseStream())
                                {
                                    using (var sr = new StreamReader(st, Encoding.UTF8))
                                    {
                                        html = sr.ReadToEnd();
                                    }
                                }

                                WriteLog("受信：" + html);

                                var responsePotatoHub = JsonConvert.DeserializeObject<ResponsePotatoHub>(html);
                                if (responsePotatoHub.status == "playing")
                                {
                                    Thread.Sleep(1);
                                    continue;
                                }
                                break;
                            }
                        }
                        else
                        {
                            while (true)
                            {
                                string html;
                                using (var st = WebRequest.Create("http://localhost:" + txtPort.Text + "/getStatus").GetResponse().GetResponseStream())
                                {
                                    using (var sr = new StreamReader(st, Encoding.UTF8))
                                    {
                                        html = sr.ReadToEnd();
                                    }
                                }

                                WriteLog("受信：" + html);

                                var responsePotatoHub = JsonConvert.DeserializeObject<ResponsePotatoHub>(html);
                                if (responsePotatoHub.status == "playing")
                                {
                                    Thread.Sleep(1);
                                    continue;
                                }
                                break;
                            }

                        }

                        if (isSendMessage)
                        {
                            SendMessages(hwndOrg, 74, 0, ref lParam);
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
            base.WndProc(ref m);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            readOption();

            WriteLog("VoiceroidTalkPlusReceiverを検索中");
            WriteLog("棒読みちゃんを起動し、その他→プラグイン→VoiceroidTalkPlusを有効にしてください");
            threadRename = new Thread(new ThreadStart(() => {
                while (true)
                {
                    var hwndRenamed = FindWindows(null, "Voiceroid Talk Plus Receiver Original");
                    if (hwndRenamed != IntPtr.Zero)
                    {
                        if (hwndOrg != hwndRenamed)
                        {
                            hwndOrg = hwndRenamed;
                            Invoke((MethodInvoker)(() =>
                            {
                                this.Text = "Voiceroid Talk Plus Receiver";
                                WriteLog("VoiceroidTalkPlusReceiverが見つかりました(" + hwndRenamed.ToString() + ")");
                            }));
                        }
                        Thread.Sleep(100);
                        continue;
                    }
                    Invoke((MethodInvoker)(() =>
                    {
                        this.Text = "Voiceroid Talk Plus Receiver Hub";
                    }));
                    var hwnd = FindWindows(null, "Voiceroid Talk Plus Receiver");
                    if (hwnd == IntPtr.Zero)
                    {
                        Thread.Sleep(100);
                        continue;
                    }
                    SetWindowText(hwnd, "Voiceroid Talk Plus Receiver Original");
                    Invoke((MethodInvoker)(() =>
                    {
                        this.Text = "Voiceroid Talk Plus Receiver";
                        WriteLog("VoiceroidTalkPlusReceiverが見つかりました(" + hwnd.ToString() + ")");
                    }));
                    hwndOrg = hwnd;
                    Thread.Sleep(100);
                }
            }));
            threadRename.Start();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            threadRename.Abort();
            SetWindowText(hwndOrg, "Voiceroid Talk Plus Receiver");
        }

        private void WriteLog(string txt)
        {
            listLog.Add(txt);
            if (listLog.Count > 30)
            {
                listLog.RemoveAt(0);
            }
            txtLog.Text = string.Join("\r\n", listLog.ToArray());
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.Focus();
            txtLog.ScrollToCaret();
        }

        private void txtPort_TextChanged(object sender, EventArgs e)
        {
            saveOption();
        }

        private void saveOption()
        {
            var option = new VoiceroidTalkPlusReceiverHubOption();
            option.httpPort = txtPort.Text;
            string jsonStr = JsonConvert.SerializeObject(option, Formatting.Indented);
            File.WriteAllText(AppDomain.CurrentDomain.BaseDirectory + "VoiceroidTalkPlusReceiverHub.json", jsonStr);
        }

        private void readOption()
        {
            try
            {
                var jsonStr = File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "VoiceroidTalkPlusReceiverHub.json");
                var option = JsonConvert.DeserializeObject<VoiceroidTalkPlusReceiverHubOption>(jsonStr);
                txtPort.Text = option.httpPort;
            }
            catch (Exception)
            {
                txtPort.Text = "2119";
            }
        }
    }
}