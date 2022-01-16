using FNF.JsonParser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Plugin_PotatoVoiceHub
{
    public partial class Form1 : Form
    {
        private PluginPotatoVoiceOption pluginPotatoVoiceOption;
        private List<string> listLog = new List<string>();

        public Form1(PluginPotatoVoiceOption _pluginPotatoVoiceOption)
        {
            pluginPotatoVoiceOption = _pluginPotatoVoiceOption;
            InitializeComponent();
        }

        private void Form1_Load(object sender, System.EventArgs e)
        {
            txtHttpPort.Text = pluginPotatoVoiceOption.HttpPort;
            chkUseReplace.Checked = pluginPotatoVoiceOption.UseReplace;
        }

        private void txtHttpPort_TextChanged(object sender, System.EventArgs e)
        {
            pluginPotatoVoiceOption.HttpPort = txtHttpPort.Text;
            pluginPotatoVoiceOption.UseReplace = chkUseReplace.Checked;
            savePluginPotatoVoiceOption();
        }

        private void chkUseReplace_CheckedChanged(object sender, EventArgs e)
        {
            pluginPotatoVoiceOption.HttpPort = txtHttpPort.Text;
            pluginPotatoVoiceOption.UseReplace = chkUseReplace.Checked;
            savePluginPotatoVoiceOption();
        }

        public void writeLog(string log)
        {
            listLog.Add(log);
            if (listLog.Count > 30)
            {
                listLog.RemoveAt(0);
            }

            try
            {
                txtLog.Text = string.Join("\r\n", listLog.ToArray());
                txtLog.ScrollToCaret();
            }
            catch (Exception)
            {
            }
        }

        private void savePluginPotatoVoiceOption()
        {
            JsonItem item = new JsonObject();
            item.Object.Add("httpPort", new JsonString(pluginPotatoVoiceOption.HttpPort));
            item.Object.Add("useReplace", new JsonBool(pluginPotatoVoiceOption.UseReplace));
            File.WriteAllText(AppDomain.CurrentDomain.BaseDirectory + "Plugin_PotatoVoiceHubOption.json", item.ToString());
        }
    }
}