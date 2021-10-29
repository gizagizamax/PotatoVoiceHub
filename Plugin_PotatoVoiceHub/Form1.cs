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
        }

        private void txtHttpPort_TextChanged(object sender, System.EventArgs e)
        {
            pluginPotatoVoiceOption.HttpPort = txtHttpPort.Text;
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
                txtLog.Text = string.Join("\n", listLog.ToArray());
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
            File.WriteAllText(AppDomain.CurrentDomain.BaseDirectory + "Plugin_PotatoVoiceHubOption.json", item.ToString());
        }
    }
}