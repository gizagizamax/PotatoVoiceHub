using AI.Talk.Editor.Api;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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
        private TtsControl _ttsControl = new TtsControl();
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

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WriteLog("A.I.VOICE 接続");

                var availableHosts = _ttsControl.GetAvailableHostNames();
                if (availableHosts.Length == 0)
                {
                    WriteLog("A.I.VOICE のホストが見つかりません");
                    return;
                }
                _ttsControl.Initialize(availableHosts[0]);
                if (_ttsControl.Status == HostStatus.NotRunning)
                {
                    _ttsControl.StartHost();
                }
                _ttsControl.Connect();

                initHttp();
                initClipboard();
                btnConnect.IsEnabled = false;
            }
            catch (Exception exc)
            {
                WriteLog(exc.Message + "\n" + exc.StackTrace);
            }
        }

        private void initHttp()
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://+:" + option.httpPort + "/");
            listener.Start();
            startLoop(listener);
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
                        if (_ttsControl.Status == HostStatus.Busy)
                        {
                            response = "{\"status\":\"busy\"}";
                        }
                        else
                        {
                            response = "{\"status\":\"idle\"}";
                        }
                        break;
                    case "/saveAudio":
                        if (_ttsControl.Status == HostStatus.Busy)
                        {
                            response = "{\"status\":\"busy\"}";
                        }
                        else
                        {
                            var queryString = HttpUtility.ParseQueryString(context.Request.Url.Query, Encoding.GetEncoding(option.saveAudioEncode));

                            if (queryString["preset"] != null)
                            {
                                foreach (var voicePreset in _ttsControl.VoicePresetNames)
                                {
                                    if (queryString["preset"] == voicePreset)
                                    {
                                        _ttsControl.CurrentVoicePresetName = voicePreset;
                                        break;
                                    }
                                }
                            }

                            _ttsControl.Text = queryString["text"];

                            try
                            {
                                _ttsControl.SaveAudioToFile(queryString["path"]);
                            }
                            catch (Exception ex)
                            {
                                WriteLog(ex.Message);
                            }

                            response = "{\"status\":\"ok\"}";
                        }
                        break;
                    case "/play":
                        if (_ttsControl.Status == HostStatus.Busy)
                        {
                            response = "{\"status\":\"busy\"}";
                        }
                        else
                        {
                            var queryString = HttpUtility.ParseQueryString(context.Request.Url.Query);

                            if (queryString["preset"] != null)
                            {
                                foreach (var voicePreset in _ttsControl.VoicePresetNames)
                                {
                                    if (queryString["preset"] == voicePreset)
                                    {
                                        _ttsControl.CurrentVoicePresetName = voicePreset;
                                        break;
                                    }
                                }
                            }

                            _ttsControl.Text = queryString["text"];

                            _ttsControl.Play();

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
                        if (_ttsControl.Status == HostStatus.Busy)
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

                        if (bool.Parse(option.isClipboardSaveAudio))
                        {
                            //ファイル名に使えない文字は消す.円マークまで消える。実装が面倒なので辞める
                            //foreach (char c in Path.GetInvalidFileNameChars())
                            //{
                            //    fileName = fileName.Replace(c.ToString(), "");
                            //}

                            var fileName = option.saveAudioPath;
                            fileName = fileName.Replace("{yyyyMMdd}", DateTime.Now.ToString("yyyyMMdd"));
                            fileName = fileName.Replace("{HHmmss}", DateTime.Now.ToString("HHmmss"));
                            fileName = fileName.Replace("{VoicePreset}", _ttsControl.CurrentVoicePresetName);
                            fileName = fileName.Replace("{Text}", clipboardText.Length > 10 ? clipboardText.Substring(0, 10): clipboardText);

                            if (fileName.Length > 256)
                            {
                                fileName = fileName.Substring(0, 256);
                            }
                            new FileInfo(fileName).Directory.Create();

                            _ttsControl.Text = clipboardText;

                            try
                            {
                                _ttsControl.SaveAudioToFile(fileName);
                            }
                            catch (Exception ex)
                            {
                                WriteLog(ex.Message);
                            }

                            clipboardTextLast = clipboardText;
                        }

                        if (bool.Parse(option.isClipboardPlay))
                        {
                            _ttsControl.Text = clipboardText;

                            _ttsControl.Play();

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
                txtSaveAudioPath.Text = option.saveAudioPath;
                txtHttpPort.Text = option.httpPort;
                txtSaveAudioEncode.Text = option.saveAudioEncode;
                cbClipboardPlay.IsChecked = bool.Parse(option.isClipboardPlay);
                cbClipboardSaveAudio.IsChecked = bool.Parse(option.isClipboardSaveAudio);
            }
            catch (Exception)
            {
                option = new VoiceHubOption();
            }

            //値が無かったらデフォルト値を埋めていく。changeイベントでoptionを保存する
            txtSaveAudioPath.Text =
                string.IsNullOrEmpty(option.saveAudioPath) ?
                AppDomain.CurrentDomain.BaseDirectory + @"clipboard\{yyyyMMdd}_{HHmmss}_{VoicePreset}_{Text}" :
                txtSaveAudioPath.Text = option.saveAudioPath;

            txtHttpPort.Text =
                string.IsNullOrEmpty(option.httpPort) ?
                "2119" :
                option.httpPort;

            txtSaveAudioEncode.Text =
                string.IsNullOrEmpty(option.saveAudioEncode) ?
                "sjis" :
                option.saveAudioEncode;

            cbClipboardPlay.IsChecked =
                string.IsNullOrEmpty(option.isClipboardPlay) ?
                false:
                bool.Parse(option.isClipboardPlay);

            cbClipboardSaveAudio.IsChecked =
                string.IsNullOrEmpty(option.isClipboardSaveAudio) ?
                false:
                bool.Parse(option.isClipboardSaveAudio);
        }

        private void txtSaveAudioPath_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            option.saveAudioPath = txtSaveAudioPath.Text;
            saveOption();
        }

        private void txtHttpPort_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            option.httpPort = txtHttpPort.Text;
            saveOption();
        }

        private void txtSaveAudioEncode_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            option.saveAudioEncode = txtSaveAudioEncode.Text;
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

        private void cbClipboardSaveAudio_Checked(object sender, RoutedEventArgs e)
        {
            option.isClipboardSaveAudio = cbClipboardSaveAudio.IsChecked.ToString();
            saveOption();
        }

        private void cbClipboardSaveAudio_Unchecked(object sender, RoutedEventArgs e)
        {
            option.isClipboardSaveAudio = cbClipboardSaveAudio.IsChecked.ToString();
            saveOption();
        }

        private void btnSaveAudioPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WriteLog("保存先の参照");

                var saveFileDialog = new SaveFileDialog();
                try
                {
                    saveFileDialog.InitialDirectory = new FileInfo(option.saveAudioPath).DirectoryName;
                }
                catch (Exception)
                {
                }
                var dialogResult = saveFileDialog.ShowDialog();
                if (dialogResult != null && dialogResult.Value)
                {
                    txtSaveAudioPath.Text = saveFileDialog.FileName;
                }
            }
            catch (Exception exc)
            {
                WriteLog(exc.Message + "\n" + exc.StackTrace);
            }
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
