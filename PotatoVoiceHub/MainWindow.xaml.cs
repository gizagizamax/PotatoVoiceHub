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
    public partial class MainWindow : Window
    {
        private VoiceHubOption option;
        private HttpListener listener;
        private Thread threadClipboard;
        private string clipboardTextLast = "";
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

                var openFileDialog = new OpenFileDialog();
                try
                {
                    openFileDialog.InitialDirectory = new FileInfo(txtAIVoiceEditorPath.Text).DirectoryName;
                }
                catch (Exception)
                {
                }
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

                mklink();
                initHttp();
                initClipboard();
                startAivoice();
                btnStartAIVoice.IsEnabled = false;
            }
            catch (Exception exc)
            {
                WriteLog(exc.Message + "\n" + exc.StackTrace);
            }
        }

        private void mklink()
        {
            var com = new Process();
            com.StartInfo.FileName = Environment.GetEnvironmentVariable("ComSpec");
            com.StartInfo.UseShellExecute = false;
            com.StartInfo.RedirectStandardOutput = true;
            com.StartInfo.RedirectStandardInput = false;
            com.StartInfo.CreateNoWindow = true;
            var aivoiceEditorPath = new FileInfo(option.aiVoiceEditorPath);
            foreach (var aiFile in aivoiceEditorPath.Directory.EnumerateFiles())
            {
                var cmd = String.Format(@"/c mklink ""{0}{1}"" ""{2}""", AppDomain.CurrentDomain.BaseDirectory, aiFile.Name, aiFile.FullName);
                WriteLog(cmd);
                com.StartInfo.Arguments = cmd;
                com.Start();
                com.WaitForExit();
            }
            foreach (var aiDir in aivoiceEditorPath.Directory.EnumerateDirectories())
            {
                var cmd = String.Format(@"/c mklink /d ""{0}{1}"" ""{2}""", AppDomain.CurrentDomain.BaseDirectory, aiDir.Name, aiDir.FullName);
                WriteLog(cmd);
                com.StartInfo.Arguments = cmd;
                com.Start();
                com.WaitForExit();
            }
            com.Close();
        }

        private void initHttp()
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://+:" + option.httpPort + "/");
            listener.Start();
            startLoop(listener);
        }

        private void startAivoice()
        {
            WriteLog("AI.Talk.Editor.App init");
            var app = new AI.Talk.Editor.App();
            app.InitializeComponent();
            app.Run();
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
                var api = context.Request.Url.AbsolutePath;
                switch (api)
                {
                    case "/getStatus":
                        if (AIUtil.GetTextEditPresenter() == null || AIUtil.GetTextEditPresenter().ViewModel.IsPlaying)
                        {
                            response = "{\"status\":\"playing\"}";
                        }
                        else
                        {
                            response = "{\"status\":\"waiting\"}";
                        }
                        break;
                    case "/saveWave":
                        if (AIUtil.GetTextEditPresenter() == null || AIUtil.GetTextEditPresenter().ViewModel.IsPlaying)
                        {
                            response = "{\"status\":\"playing\"}";
                        }
                        else
                        {
                            var queryString = HttpUtility.ParseQueryString(context.Request.Url.Query, Encoding.GetEncoding(option.saveWaveEncode));

                            var presetName = "";
                            if (queryString["presetName"] == null)
                            {
                                presetName = AIUtil.GetMainPresenter().CurrentPresetName;
                            }
                            else
                            {
                                foreach (var voicePreset in AIUtil.GetAppFramework().UserSettings.VoicePreset.VoicePresets)
                                {
                                    if (queryString["presetName"] == voicePreset.PresetName)
                                    {
                                        presetName = voicePreset.PresetName;
                                        break;
                                    }
                                }

                                if (presetName == "")
                                {
                                    presetName = AIUtil.GetMainPresenter().CurrentPresetName;
                                }
                            }

                            AI.Framework.SaveWaveSettings sws = new AI.Framework.SaveWaveSettings(AI.Framework.AppFramework.Current.UserSettings.SaveWave)
                            {
                                BeginPause = 0,
                                TermPause = 0,
                                FilePath = queryString["filePath"]
                            };
                            AIUtil.GetMainModel().SaveWave(queryString["text"], presetName, sws).Wait();

                            response = "{\"status\":\"ok\"}";
                        }
                        break;
                    case "/":
                        if (AIUtil.GetTextEditPresenter() == null || AIUtil.GetTextEditPresenter().ViewModel.IsPlaying)
                        {
                            response = "{\"status\":\"playing\"}";
                        }
                        else
                        {
                            var queryString = HttpUtility.ParseQueryString(context.Request.Url.Query);
                            Dispatcher.Invoke((() =>
                            {
                                AIUtil.GetTextEditPresenter().ViewModel.Text = queryString["text"];
                                AIUtil.GetMainModel().PlayText(queryString["text"], AIUtil.GetMainPresenter().CurrentPresetName);
                            }));
                            response = "{\"status\":\"ok\"}";
                        }
                        break;
                    default:
                        response = "{\"status\":\"\"}";
                        break;
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

        private void initClipboard()
        {
            if (threadClipboard != null)
            {
                return;
            }

            clipboardTextLast = Clipboard.GetText().Trim();

            threadClipboard = new Thread(new ThreadStart(() =>
            {
                // Windowが生きてる間はポーリングする
                while (IsVisible)
                {
                    try
                    {
                        if (AIUtil.GetTextEditPresenter() == null || AIUtil.GetTextEditPresenter().ViewModel.IsPlaying)
                        {
                            Thread.Sleep(100);
                            continue;
                        }

                        var clipboardText = "";
                        Dispatcher.Invoke((() =>
                        {
                            clipboardText = Clipboard.GetText().Trim();
                        }));
                        if (clipboardText == "" || clipboardText == clipboardTextLast)
                        {
                            clipboardTextLast = clipboardText;
                            Thread.Sleep(100);
                            continue;
                        }

                        if (bool.Parse(option.isClipboardPlay))
                        {
                            Dispatcher.Invoke((() =>
                            {
                                AIUtil.GetTextEditPresenter().ViewModel.Text = clipboardText;
                                AIUtil.GetMainModel().PlayText(clipboardText, AIUtil.GetMainPresenter().CurrentPresetName);
                            }));

                            clipboardTextLast = clipboardText;
                        }

                        if (bool.Parse(option.isClipboardSaveWave))
                        {
                            if (AIUtil.GetAppFramework().UserSettings.SaveWave.FilePathSelectionMode == AI.Framework.FilePathSelectionMode.FileSaveDialog)
                            {
                                MessageBox.Show("A.I.Voice Editorで[プロジェクト設定]->[音声ファイル保存]->[ファイル命名規則を指定して選択する]を選んでください");
                                clipboardTextLast = clipboardText;
                                Thread.Sleep(100);
                                continue;
                            }

                            //なぜかA.I.VOICE Editorで最後に出力したパスになってしまうので、ファイル名を用意する
                            var clipboardFileName = clipboardText;
                            //ファイル名に使えない文字は消す
                            foreach (char c in Path.GetInvalidFileNameChars())
                            {
                                clipboardFileName = clipboardFileName.Replace(c.ToString(), "");
                            }
                            if (clipboardFileName.Length > AIUtil.GetAppFramework().UserSettings.SaveWave.NamingRule.TextLength)
                            {
                                clipboardFileName = clipboardFileName.Substring(0, AIUtil.GetAppFramework().UserSettings.SaveWave.NamingRule.TextLength);
                            }

                            var fileName = AIUtil.GetAppFramework().UserSettings.SaveWave.NamingRule.NamingRule;
                            fileName = fileName.Replace("{yyyyMMdd}", DateTime.Now.ToString("yyyyMMdd"));
                            fileName = fileName.Replace("{HHmmss}", DateTime.Now.ToString("HHmmss"));
                            fileName = fileName.Replace("{VoicePreset}", AIUtil.GetMainPresenter().CurrentPresetName);
                            fileName = fileName.Replace("{Text}", clipboardFileName);

                            AIUtil.GetAppFramework().UserSettings.SaveWave.FilePath = AIUtil.GetAppFramework().UserSettings.SaveWave.NamingRule.OutDir + "\\" + fileName + ".wav";
                            AIUtil.GetMainModel().SaveWave(clipboardText, AIUtil.GetMainPresenter().CurrentPresetName, AIUtil.GetAppFramework().UserSettings.SaveWave).Wait();

                            clipboardTextLast = clipboardText;
                        }
                    }
                    catch (Exception)
                    {
                    }

                    Thread.Sleep(100);
                }
            }));
            threadClipboard.Start();
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
            string jsonStr = JsonConvert.SerializeObject(option, Formatting.Indented);
            File.WriteAllText(AppDomain.CurrentDomain.BaseDirectory + "VoiceHubOption.json", jsonStr);
        }

        private void readOption()
        {
            try
            {
                var jsonStr = File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "VoiceHubOption.json");
                option = JsonConvert.DeserializeObject<VoiceHubOption>(jsonStr);
                txtAIVoiceEditorPath.Text = option.aiVoiceEditorPath;
                txtHttpPort.Text = option.httpPort;
                txtSaveWaveEncode.Text = option.saveWaveEncode;
                cbClipboardPlay.IsChecked = bool.Parse(option.isClipboardPlay);
                cbClipboardSaveWave.IsChecked = bool.Parse(option.isClipboardSaveWave);
            }
            catch (Exception)
            {
                option = new VoiceHubOption();
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
            if (txtHttpPort.Text == "")
            {
                txtHttpPort.Text = "2119";
            }
            if (txtSaveWaveEncode.Text == "")
            {
                txtSaveWaveEncode.Text = "sjis";
            }
        }

        private void txtAIVoiceEditorPath_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            option.aiVoiceEditorPath = txtAIVoiceEditorPath.Text;
            saveOption();
        }

        private void txtHttpPort_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            option.httpPort = txtHttpPort.Text;
            saveOption();
        }

        private void txtSaveWaveEncode_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            option.saveWaveEncode = txtSaveWaveEncode.Text;
            saveOption();
        }

        private void cbClipboardPlay_Checked(object sender, RoutedEventArgs e)
        {
            option.isClipboardPlay = cbClipboardPlay.IsChecked.ToString();
            saveOption();
        }

        private void cbClipboardPlay_Unchecked(object sender, RoutedEventArgs e)
        {
            option.isClipboardPlay = cbClipboardPlay.IsChecked.ToString();
            saveOption();
        }

        private void cbClipboardSaveWave_Checked(object sender, RoutedEventArgs e)
        {
            option.isClipboardSaveWave = cbClipboardSaveWave.IsChecked.ToString();
            saveOption();
        }

        private void cbClipboardSaveWave_Unchecked(object sender, RoutedEventArgs e)
        {
            option.isClipboardSaveWave = cbClipboardSaveWave.IsChecked.ToString();
            saveOption();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (threadClipboard != null)
            {
                threadClipboard.Abort();
                threadClipboard = null;
            }
        }
    }
}
