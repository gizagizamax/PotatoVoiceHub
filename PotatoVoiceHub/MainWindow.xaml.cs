using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;
using System.Windows;

namespace PotatoVoiceHub
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private HttpListener listener;
        private List<string> listLog = new List<string>();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            readOption();
        }

        private void btnAIVoiceEditorPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WriteLog("AIVoiceEditor.exeの場所参照");

                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.InitialDirectory = txtAIVoiceEditorPath.Text;
                var dialogResult = openFileDialog.ShowDialog();
                if (dialogResult != null && dialogResult.Value)
                {
                    txtAIVoiceEditorPath.Text = openFileDialog.FileName;
                }
            }
            catch (Exception exc)
            {
                WriteLog(exc.Message + "\n" + exc.StackTrace);
            }
        }

        private void btnStartAIVoice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WriteLog("A.I.VOICE 開始");

                var aivoiceEditorPath = new FileInfo(txtAIVoiceEditorPath.Text);
                Environment.SetEnvironmentVariable("path", Environment.GetEnvironmentVariable("path") + ";" + aivoiceEditorPath.DirectoryName);
                var com = new Process();
                com.StartInfo.FileName = System.Environment.GetEnvironmentVariable("ComSpec");
                com.StartInfo.UseShellExecute = false;
                com.StartInfo.RedirectStandardOutput = true;
                com.StartInfo.RedirectStandardInput = false;
                com.StartInfo.CreateNoWindow = true;
                foreach (var arg in new string[] {
                    String.Format(@"/c mklink ""{0}aitalk.lic"" ""{1}\aitalk.lic""", AppDomain.CurrentDomain.BaseDirectory, aivoiceEditorPath.DirectoryName),
                    String.Format(@"/c mklink ""{0}aitalk5.lic"" ""{1}\aitalk5.lic""", AppDomain.CurrentDomain.BaseDirectory, aivoiceEditorPath.DirectoryName),
                    String.Format(@"/c mklink /d ""{0}Voice"" ""{1}\Voice""", AppDomain.CurrentDomain.BaseDirectory, aivoiceEditorPath.DirectoryName),
                    String.Format(@"/c mklink /d ""{0}Lang"" ""{1}\Lang""", AppDomain.CurrentDomain.BaseDirectory, aivoiceEditorPath.DirectoryName),
                    String.Format(@"/c mklink /d ""{0}copying"" ""{1}\copying""", AppDomain.CurrentDomain.BaseDirectory, aivoiceEditorPath.DirectoryName)
                })
                {
                    WriteLog(arg);
                    com.StartInfo.Arguments = arg;
                    com.Start();
                    com.WaitForExit();
                }
                com.Close();

                listener = new HttpListener();
                listener.Prefixes.Add("http://+:" + txtHttpPort.Text + "/");
                listener.Start();
                startLoop(listener);

                WriteLog("AI.Talk.Editor.App init");
                var app = new AI.Talk.Editor.App();
                app.InitializeComponent();
                app.Run();
            }
            catch (Exception exc)
            {
                WriteLog(exc.Message + "\n" + exc.StackTrace);
            }
        }

        private void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WriteLog("Play");

                AIUtil.GetTextEditPresenter().ViewModel.Text = txtSpeekText.Text;
                AIUtil.GetMainModel().PlayText(txtSpeekText.Text, AIUtil.GetMainPresenter().CurrentPresetName);
            }
            catch (Exception exc)
            {
                WriteLog(exc.Message + "\n" + exc.StackTrace);
            }
        }

        private void btnDark_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WriteLog("ダークモード");

                AIUtil.GetMainPresenter().SetTheme("Dark");
            }
            catch (Exception exc)
            {
                WriteLog(exc.Message + "\n" + exc.StackTrace);
            }
        }

        private void btnSimple_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WriteLog("シンプルモード");

                AIUtil.GetMainPresenter().SetTheme("Simple");
            }
            catch (Exception exc)
            {
                WriteLog(exc.Message + "\n" + exc.StackTrace);
            }
        }

        private void startLoop(HttpListener _listener)
        {
            if (!_listener.IsListening)
            {
                return;
            }

            _listener.BeginGetContext((IAsyncResult ar) =>
            {
                if (!_listener.IsListening)
                {
                    return;
                }

                listenerBeginGetContext(ar, _listener);
                startLoop(_listener);
            }, null);
        }

        private void listenerBeginGetContext(IAsyncResult result, HttpListener _listener)
        {
            HttpListenerContext context;
            try
            {
                context = _listener.EndGetContext(result);
            }
            catch (HttpListenerException)
            {
                return;
            }

            string response;
            try
            {
                if (AIUtil.GetTextEditPresenter().ViewModel.IsPlaying)
                {
                    response = "{\"status\":\"playing\"}";
                }
                else
                {
                    Dispatcher.Invoke((() =>
                    {
                        var queryString = HttpUtility.ParseQueryString(context.Request.Url.Query);
                        AIUtil.GetTextEditPresenter().ViewModel.Text = queryString["text"];
                        AIUtil.GetMainModel().PlayText(queryString["text"], AIUtil.GetMainPresenter().CurrentPresetName);
                    }));
                    response = "{\"status\":\"ok\"}";
                }
            }
            catch (Exception)
            {
                response = "{\"status\":\"error\"}";
            }

            var buffer = Encoding.UTF8.GetBytes(response);
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }

        private void WriteLog(string log)
        {
            listLog.Add(log);
            if (listLog.Count > 30)
            {
                listLog.RemoveAt(0);
            }

            try
            {
                txtLog.Text = string.Join("\n", listLog.ToArray());
                txtLog.ScrollToEnd();
            }
            catch (Exception)
            {
            }
        }

        private void saveOption()
        {
            var option = new VoiceHubOption();
            option.aiVoiceEditorPath = txtAIVoiceEditorPath.Text;
            option.httpPort = txtHttpPort.Text;
            string jsonStr = JsonConvert.SerializeObject(option, Formatting.Indented);
            File.WriteAllText(AppDomain.CurrentDomain.BaseDirectory + "VoiceHubOption.json", jsonStr);
        }

        private void readOption()
        {
            try
            {
                var jsonStr = File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "VoiceHubOption.json");
                var option = JsonConvert.DeserializeObject<VoiceHubOption>(jsonStr);
                txtAIVoiceEditorPath.Text = option.aiVoiceEditorPath;
                txtHttpPort.Text = option.httpPort;
            }
            catch (Exception)
            {
                txtHttpPort.Text = "2119";
            }

            if (txtAIVoiceEditorPath.Text == "")
            {
                var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey("SOFTWARE\\AI\\AIVoice\\AIVoiceEditor\\1.0");
                if (baseKey != null)
                {
                    var installDir = baseKey.GetValue("InstallDir");
                    if (installDir != null)
                    {
                        txtAIVoiceEditorPath.Text = installDir.ToString() + "AIVoiceEditor.exe";
                    }
                }
            }
        }

        private void txtAIVoiceEditorPath_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            saveOption();
        }

        private void txtHttpPort_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            saveOption();
        }
    }
}
