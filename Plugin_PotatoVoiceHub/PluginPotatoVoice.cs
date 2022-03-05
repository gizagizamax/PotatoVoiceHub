using FNF.BouyomiChanApp;
using FNF.JsonParser;
using FNF.Utility;
using FNF.XmlSerializerSetting;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;
using System.Windows.Forms;

namespace Plugin_PotatoVoiceHub
{
    public class PluginPotatoVoice : IPlugin
    {
        private PluginPotatoVoiceOption pluginPotatoVoiceOption = new PluginPotatoVoiceOption();
        private ToolStripButton toolStripButton = new ToolStripButton();
        private ToolStripSeparator toolStripSeparator = new ToolStripSeparator();
        private Form1 form1;
        private bool isTalking;

        public string Name => "PluginPotatoVoice";

        public string Version => "2022/03/05版";

        public string Caption => "棒読みちゃんの音声をA.I.Voiceに変えます";

        public ISettingFormData SettingFormData => null;

        public void Begin()
        {
            Console.WriteLine("Begin");
            Pub.FormMain.BC.TalkTaskStarted += new EventHandler<BouyomiChan.TalkTaskStartedEventArgs>(Pub_FormMain_BC_TalkTaskStarted);

            Pub.ToolStrip.Items.Add(toolStripSeparator);

            toolStripButton.ToolTipText = "PluginPotatoVoice";
            toolStripButton.Image = Resource1.icon;
            toolStripButton.Click += new EventHandler(this.toolStripButton_Click);
            Pub.ToolStrip.Items.Add(toolStripButton);

            readPluginPotatoVoiceOption();
        }

        public void End()
        {
            Console.WriteLine("End");
            Pub.FormMain.BC.TalkTaskStarted -= new EventHandler<BouyomiChan.TalkTaskStartedEventArgs>(Pub_FormMain_BC_TalkTaskStarted);
            Pub.ToolStrip.Items.Remove(toolStripButton);
        }

        private void toolStripButton_Click(object sender, EventArgs e)
        {
            if (form1 == null || form1.IsDisposed)
            {
                form1 = new Form1(pluginPotatoVoiceOption);
            }
            form1.Show();
        }

        private void Pub_FormMain_BC_TalkTaskStarted(object sender, BouyomiChan.TalkTaskStartedEventArgs e)
        {
            if (form1 == null || form1.IsDisposed)
            {
                form1 = new Form1(pluginPotatoVoiceOption);
            }
            form1.writeLog(e.TalkTask.SourceText);

            isTalking = true;
            while (isTalking)
            {
                string text;
                if (e.ReplaceWord.Contains("(Ｔ "))
                {
                    form1.writeLog("コマンドは読まない。" + e.ReplaceWord);
                    break;
                }

                // 棒読みちゃんの辞書変換を使う
                if (pluginPotatoVoiceOption.UseReplace)
                {
                    text = e.ReplaceWord;

                } else {
                    text = e.TalkTask.SourceText;
                }

                string html;
                using (var st = WebRequest.Create("http://localhost:" + pluginPotatoVoiceOption.HttpPort + "/play?text=" + HttpUtility.UrlEncode(text)).GetResponse().GetResponseStream())
                {
                    using (var sr = new StreamReader(st, Encoding.UTF8))
                    {
                        html = sr.ReadToEnd();
                    }
                }

                form1.writeLog(html);

                var json = new Parser(html);
                if (json["status"].String == "busy")
                {
                    Thread.Sleep(1);
                    continue;
                }
                break;
            }

            e.Cancel = true;
        }

        private void readPluginPotatoVoiceOption()
        {
            try
            {
                var txt = File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "Plugin_PotatoVoiceHubOption.json");
                Parser parser = new Parser(txt);
                if (!parser["httpPort"].TryGetString(out pluginPotatoVoiceOption.HttpPort)) pluginPotatoVoiceOption.HttpPort = "2119";
                if (!parser["useReplace"].TryGetBool(out pluginPotatoVoiceOption.UseReplace)) pluginPotatoVoiceOption.UseReplace = true;
            }
            catch (Exception)
            {
                pluginPotatoVoiceOption.HttpPort = "2119";
                pluginPotatoVoiceOption.UseReplace = true;
            }
        }
    }
}