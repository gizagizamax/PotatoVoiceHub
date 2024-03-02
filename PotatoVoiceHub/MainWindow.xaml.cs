using AI.Talk.Editor.Api;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Windows;
using System.Windows.Automation;

namespace PotatoVoiceHub
{
    public partial class MainWindow : Window
    {
        //A.I.VOICE1用
        TtsControl _ttsControlAiv1;

        //A.I.VOICE2用
        Aiv2EditorElem aiv2EditorElem;

        VoiceHubOption option;
        HttpListener listener;
        Thread threadClipboard;
        string clipboardTextLast = "";
        List<string> listLog = new List<string>();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            readOption();
        }

        private void btnConnect1_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WriteLog("A.I.VOICE 接続");

                _ttsControlAiv1 = new TtsControl();
                var availableHosts = _ttsControlAiv1.GetAvailableHostNames();
                if (availableHosts.Length == 0)
                {
                    WriteLog("A.I.VOICE のホストが見つかりません");
                    WriteLog("A.I.VOICE をインストール済みであり、起動出来るか確認してください。");
                    return;
                }
                _ttsControlAiv1.Initialize(availableHosts[0]);
                if (_ttsControlAiv1.Status == HostStatus.NotRunning)
                {
                    _ttsControlAiv1.StartHost();
                }
                if (_ttsControlAiv1.Status == HostStatus.NotConnected)
                {
                    _ttsControlAiv1.Connect();
                }

                initHttp();
                initClipboard();
            }
            catch (Exception exc)
            {
                WriteLog(exc.Message + "\n" + exc.StackTrace);
            }
        }

        private void btnConnect2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WriteLog("A.I.VOICE2 接続");

                if (!initAiv2())
                {
                    return;
                }

                initHttp();
                initClipboard();
            }
            catch (Exception exc)
            {
                WriteLog(exc.Message + "\n" + exc.StackTrace);
            }
        }

        bool initAiv2()
        {
            aiv2EditorElem = new Aiv2EditorElem();
            if (aiv2EditorElem.GetProcess() == null)
            {
                WriteLog("A.I.VOICE2 が見つかりません");
                WriteLog("A.I.VOICE2 を起動しておいてください。");
                return false;
            }
            return true;
        }
        private void initHttp()
        {
            if (listener == null)
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://+:" + option.httpPort + "/");
                listener.Start();
                startLoop(listener);
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
                    _listener = null;
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

            string response = null;

            // A.I.VOICEの処理
            if (_ttsControlAiv1 != null)
            {
                try
                {
                    //APIドキュメントに記載が無いが、どうも時間経過で接続が切れるっぽい
                    if (_ttsControlAiv1.Status == HostStatus.NotConnected)
                    {
                        _ttsControlAiv1.Connect();
                    }
                    var api = context.Request.Url.AbsolutePath;
                    switch (api)
                    {
                        case "/getStatus":
                            if (_ttsControlAiv1.Status == HostStatus.Busy)
                            {
                                response = "{\"status\":\"busy\"}";
                            }
                            else
                            {
                                response = "{\"status\":\"idle\"}";
                            }
                            break;
                        case "/saveAudio":
                            if (_ttsControlAiv1.Status == HostStatus.Busy)
                            {
                                response = "{\"status\":\"busy\"}";
                            }
                            else
                            {
                                var queryString = HttpUtility.ParseQueryString(context.Request.Url.Query, Encoding.GetEncoding(option.saveAudioEncode));

                                if (queryString["preset"] != null)
                                {
                                    foreach (var voicePreset in _ttsControlAiv1.VoicePresetNames)
                                    {
                                        if (queryString["preset"] == voicePreset)
                                        {
                                            _ttsControlAiv1.CurrentVoicePresetName = voicePreset;
                                            break;
                                        }
                                    }
                                }

                                _ttsControlAiv1.Text = queryString["text"];

                                try
                                {
                                    _ttsControlAiv1.SaveAudioToFile(queryString["path"]);
                                }
                                catch (Exception ex)
                                {
                                    WriteLog(ex.Message);
                                }

                                response = "{\"status\":\"ok\"}";
                            }
                            break;
                        case "/play":
                            if (_ttsControlAiv1.Status == HostStatus.Busy)
                            {
                                response = "{\"status\":\"busy\"}";
                            }
                            else
                            {
                                var queryString = HttpUtility.ParseQueryString(context.Request.Url.Query);

                                if (queryString["preset"] != null)
                                {
                                    foreach (var voicePreset in _ttsControlAiv1.VoicePresetNames)
                                    {
                                        if (queryString["preset"] == voicePreset)
                                        {
                                            _ttsControlAiv1.CurrentVoicePresetName = voicePreset;
                                            break;
                                        }
                                    }
                                }

                                _ttsControlAiv1.Text = queryString["text"];

                                _ttsControlAiv1.Play();

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
            }

            // A.I.VOICE2の処理
            if (aiv2EditorElem != null)
            {
                try
                {
                    var api = context.Request.Url.AbsolutePath;
                    switch (api)
                    {
                        case "/getStatus":
                            if (aiv2EditorElem.GetElemAiv2Play().Current.Name != "再生" || !aiv2EditorElem.GetElemAiv2Play().Current.IsEnabled)
                            {
                                response = "{\"status\":\"busy\"}";
                            }
                            else
                            {
                                response = "{\"status\":\"idle\"}";
                            }
                            break;
                        case "/saveAudio":
                            if (aiv2EditorElem.GetElemAiv2Play().Current.Name != "再生" || !aiv2EditorElem.GetElemAiv2Play().Current.IsEnabled)
                            {
                                response = "{\"status\":\"busy\"}";
                            }
                            else
                            {
                                var queryString = HttpUtility.ParseQueryString(context.Request.Url.Query, Encoding.GetEncoding(option.saveAudioEncode));
                                // エスケープが必要な文字は消す
                                var sendKeysText = queryString["text"];
                                if (sendKeysText == null)
                                {
                                    sendKeysText = "";
                                }
                                sendKeysText = sendKeysText
                                    .Replace("{", "").Replace("}", "")
                                    .Replace("~", "")
                                    .Replace("+", "")
                                    .Replace("^", "")
                                    .Replace("%", "")
                                    .Replace("\r", "").Replace("\n", "")
                                    .Replace("\"", "")
                                    .Replace("(", "").Replace(")", "");
                                //句読点があるとA.I.VOICE2が止まるのでピリオドにする
                                sendKeysText = sendKeysText
                                    .Replace("。", ". ")
                                    .Replace("、", ". ")
                                    .Replace("？", ". ");

                                //プリセットの機能は無し

                                var foregroundWindowHwd = Win32Api.GetForegroundWindow();
                                aiv2EditorElem.GetElemMainWindow().SetFocus();

                                System.Windows.Forms.SendKeys.SendWait("^a");
                                System.Windows.Forms.SendKeys.SendWait("{DEL}");

                                //再生ボタンが押せなくなるのを待つ
                                for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-3);)
                                {
                                    if (!aiv2EditorElem.GetElemAiv2Play().Current.IsEnabled)
                                    {
                                        Thread.Sleep(10);
                                        continue;
                                    }
                                    break;
                                }

                                System.Windows.Forms.SendKeys.SendWait(sendKeysText);

                                //再生ボタンが押せるようになるのを待つ
                                for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-10);)
                                {
                                    if (aiv2EditorElem.GetElemAiv2Play().Current.IsEnabled)
                                    {
                                        break;
                                    }
                                }

                                try
                                {
                                    aiv2EditorElem.GetInvokeAiv2Write1().Invoke();

                                    for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-10);)
                                    {
                                        if (aiv2EditorElem.GetElemAiv2Write2() != null)
                                        {
                                            break;
                                        }
                                    }
                                    if (aiv2EditorElem.GetElemAiv2Write2() == null)
                                    {
                                        WriteLog("10秒以内に書き出しを実行ボタンが見つかりませんでした。");
                                    }
                                    else
                                    {
                                        aiv2EditorElem.GetInvokeAiv2Write2().Invoke();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    WriteLog(ex.Message);
                                }

                                Win32Api.SetForegroundWindow(foregroundWindowHwd);

                                response = "{\"status\":\"ok\"}";
                            }
                            break;
                        case "/play":
                            if (aiv2EditorElem.GetElemAiv2Play().Current.Name != "再生" || !aiv2EditorElem.GetElemAiv2Play().Current.IsEnabled)
                            {
                                response = "{\"status\":\"busy\"}";
                            }
                            else
                            {
                                var queryString = HttpUtility.ParseQueryString(context.Request.Url.Query);
                                // エスケープが必要な文字は消す
                                var sendKeysText = queryString["text"];
                                if (sendKeysText == null)
                                {
                                    sendKeysText = "";
                                }
                                sendKeysText = sendKeysText
                                    .Replace("{", "").Replace("}", "")
                                    .Replace("~", "")
                                    .Replace("+", "")
                                    .Replace("^", "")
                                    .Replace("%", "")
                                    .Replace("\r", "").Replace("\n", "")
                                    .Replace("\"", "")
                                    .Replace("(", "").Replace(")", "");
                                //句読点があるとA.I.VOICE2が止まるのでピリオドにする
                                sendKeysText = sendKeysText
                                    .Replace("。", ". ")
                                    .Replace("、", ". ")
                                    .Replace("？", ". ");

                                //プリセットの機能は無し

                                var foregroundWindowHwd = Win32Api.GetForegroundWindow();
                                aiv2EditorElem.GetElemMainWindow().SetFocus();

                                System.Windows.Forms.SendKeys.SendWait("^a");
                                System.Windows.Forms.SendKeys.SendWait("{DEL}");

                                //再生ボタンが押せなくなるのを待つ
                                for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-3);)
                                {
                                    if (!aiv2EditorElem.GetElemAiv2Play().Current.IsEnabled)
                                    {
                                        Thread.Sleep(10);
                                        continue;
                                    }
                                    break;
                                }

                                System.Windows.Forms.SendKeys.SendWait(sendKeysText);

                                //再生ボタンが押せるようになるのを待つ
                                for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-10);)
                                {
                                    if (aiv2EditorElem.GetElemAiv2Play().Current.IsEnabled)
                                    {
                                        break;
                                    }
                                }
                                aiv2EditorElem.GetInvokeAiv2Play().Invoke();

                                Win32Api.SetForegroundWindow(foregroundWindowHwd);

                                //再生ボタンが停止ボタンになる前に次のメッセージが来ると、メッセージを上書きして停止ボタンを押してしまうので、
                                //再生後は停止ボタンになるまで待つ
                                for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-3);)
                                {
                                    if (aiv2EditorElem.GetElemAiv2Play().Current.Name == "再生")
                                    {
                                        Thread.Sleep(10);
                                        continue;
                                    }
                                    break;
                                }

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

            try
            {
                clipboardTextLast = Clipboard.GetText().Trim();
            }
            catch (Exception)
            {
            }

            threadClipboard = new Thread(new ThreadStart(() =>
            {
                // Windowが生きてる間はポーリングする
                while (IsVisible)
                {
                    // A.I.VOICE1の処理
                    if (_ttsControlAiv1 != null)
                    {
                        try
                        {
                            if (_ttsControlAiv1.Status == HostStatus.Busy)
                            {
                                Thread.Sleep(100);
                                continue;
                            }

                            var clipboardText = "";
                            Dispatcher.Invoke((() =>
                            {
                                try
                                {
                                    clipboardText = Clipboard.GetText().Trim();
                                }
                                catch (Exception)
                                {
                                }
                            }));
                            if (clipboardText == "" || clipboardText == clipboardTextLast)
                            {
                                clipboardTextLast = clipboardText;
                                Thread.Sleep(100);
                                continue;
                            }

                            if (bool.Parse(option.isClipboardSaveAudio))
                            {
                                //APIドキュメントに記載が無いが、どうも時間経過で接続が切れるっぽい
                                if (_ttsControlAiv1.Status == HostStatus.NotConnected)
                                {
                                    _ttsControlAiv1.Connect();
                                }

                                //ファイル名に使えない文字は消す.円マークまで消える。実装が面倒なので辞める
                                //foreach (char c in Path.GetInvalidFileNameChars())
                                //{
                                //    fileName = fileName.Replace(c.ToString(), "");
                                //}

                                var fileName = option.saveAudioPath;
                                fileName = fileName.Replace("{yyyyMMdd}", DateTime.Now.ToString("yyyyMMdd"));
                                fileName = fileName.Replace("{HHmmss}", DateTime.Now.ToString("HHmmss"));
                                fileName = fileName.Replace("{VoicePreset}", _ttsControlAiv1.CurrentVoicePresetName);
                                fileName = fileName.Replace("{Text}", clipboardText.Length > 10 ? clipboardText.Substring(0, 10): clipboardText);

                                if (fileName.Length > 256)
                                {
                                    fileName = fileName.Substring(0, 256);
                                }
                                new FileInfo(fileName).Directory.Create();

                                _ttsControlAiv1.Text = clipboardText;

                                try
                                {
                                    _ttsControlAiv1.SaveAudioToFile(fileName);
                                }
                                catch (Exception ex)
                                {
                                    WriteLog(ex.Message);
                                }

                                clipboardTextLast = clipboardText;
                            }

                            if (bool.Parse(option.isClipboardPlay))
                            {
                                //APIドキュメントに記載が無いが、どうも時間経過で接続が切れるっぽい
                                if (_ttsControlAiv1.Status == HostStatus.NotConnected)
                                {
                                    _ttsControlAiv1.Connect();
                                }

                                _ttsControlAiv1.Text = clipboardText;

                                _ttsControlAiv1.Play();

                                clipboardTextLast = clipboardText;
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }

                    // A.I.VOICE2の処理
                    if (aiv2EditorElem != null)
                    {
                        try
                        {
                            if (aiv2EditorElem.GetElemAiv2Play() == null || aiv2EditorElem.GetElemAiv2Play().Current.Name != "再生" || !aiv2EditorElem.GetElemAiv2Play().Current.IsEnabled)
                            {
                                Thread.Sleep(100);
                                continue;
                            }

                            var clipboardText = "";
                            Dispatcher.Invoke((() =>
                            {
                                try
                                {
                                    clipboardText = Clipboard.GetText().Trim();
                                }
                                catch (Exception)
                                {
                                }
                            }));
                            if (clipboardText == "" || clipboardText == clipboardTextLast)
                            {
                                clipboardTextLast = clipboardText;
                                Thread.Sleep(100);
                                continue;
                            }

                            // エスケープが必要な文字は消す
                            var sendKeysText = clipboardText;
                            if (sendKeysText == null)
                            {
                                sendKeysText = "";
                            }
                            sendKeysText = sendKeysText
                                .Replace("{", "").Replace("}", "")
                                .Replace("~", "")
                                .Replace("+", "")
                                .Replace("^", "")
                                .Replace("%", "")
                                .Replace("\r", "").Replace("\n", "")
                                .Replace("\"", "")
                                .Replace("(", "").Replace(")", "");
                            //句読点があるとA.I.VOICE2が止まるのでピリオドにする
                            sendKeysText = sendKeysText
                                .Replace("。", ". ")
                                .Replace("、", ". ")
                                .Replace("？", ". ");

                            if (bool.Parse(option.isClipboardSaveAudio))
                            {
                                //ファイル名はA.I.VOICE2が決めるため処理しない

                                var foregroundWindowHwd = Win32Api.GetForegroundWindow();
                                aiv2EditorElem.GetElemMainWindow().SetFocus();

                                System.Windows.Forms.SendKeys.SendWait("^a");
                                System.Windows.Forms.SendKeys.SendWait("{DEL}");

                                //再生ボタンが押せなくなるのを待つ
                                for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-3);)
                                {
                                    if (!aiv2EditorElem.GetElemAiv2Play().Current.IsEnabled)
                                    {
                                        Thread.Sleep(10);
                                        continue;
                                    }
                                    break;
                                }

                                System.Windows.Forms.SendKeys.SendWait(sendKeysText);

                                //再生ボタンが押せるようになるのを待つ
                                for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-10);)
                                {
                                    if (aiv2EditorElem.GetElemAiv2Play().Current.IsEnabled)
                                    {
                                        break;
                                    }
                                }
                                aiv2EditorElem.GetInvokeAiv2Write1().Invoke();

                                for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-10);)
                                {
                                    if (aiv2EditorElem.GetElemAiv2Write2() != null)
                                    {
                                        break;
                                    }
                                }
                                if (aiv2EditorElem.GetElemAiv2Write2() == null)
                                {
                                    WriteLog("10秒以内に書き出しを実行ボタンが見つかりませんでした。");
                                }
                                else
                                {
                                    aiv2EditorElem.GetInvokeAiv2Write2().Invoke();
                                }

                                Win32Api.SetForegroundWindow(foregroundWindowHwd);

                                clipboardTextLast = clipboardText;
                            }

                            if (bool.Parse(option.isClipboardPlay))
                            {
                                var foregroundWindowHwd = Win32Api.GetForegroundWindow();
                                aiv2EditorElem.GetElemMainWindow().SetFocus();

                                System.Windows.Forms.SendKeys.SendWait("^a");
                                System.Windows.Forms.SendKeys.SendWait("{DEL}");

                                //再生ボタンが押せなくなるのを待つ
                                for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-3);)
                                {
                                    if (!aiv2EditorElem.GetElemAiv2Play().Current.IsEnabled)
                                    {
                                        Thread.Sleep(10);
                                        continue;
                                    }
                                    break;
                                }

                                System.Windows.Forms.SendKeys.SendWait(sendKeysText);

                                //再生ボタンが押せるようになるのを待つ
                                for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-10);)
                                {
                                    if (aiv2EditorElem.GetElemAiv2Play().Current.IsEnabled)
                                    {
                                        break;
                                    }
                                }

                                for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-10);)
                                {
                                    try
                                    {
                                        aiv2EditorElem.GetInvokeAiv2Play().Invoke();
                                        break;
                                    }
                                    catch (Exception)
                                    {
                                    }
                                }

                                Win32Api.SetForegroundWindow(foregroundWindowHwd);

                                //再生ボタンが停止ボタンになる前に次のメッセージが来ると、メッセージを上書きして停止ボタンを押してしまうので、
                                //再生後は停止ボタンになるまで待つ
                                for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-3);)
                                {
                                    if (aiv2EditorElem.GetElemAiv2Play().Current.Name == "再生")
                                    {
                                        Thread.Sleep(10);
                                        continue;
                                    }
                                    break;
                                }

                                clipboardTextLast = clipboardText;
                            }
                        }
                        catch (Exception)
                        {
                        }
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
                option.saveAudioPath;

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
            // チェックボックスはOFF→OFFだと変わらないので、イベントに頼らない
            option.isClipboardPlay = cbClipboardPlay.IsChecked.ToString();

            cbClipboardSaveAudio.IsChecked =
                string.IsNullOrEmpty(option.isClipboardSaveAudio) ?
                false:
                bool.Parse(option.isClipboardSaveAudio);
            option.isClipboardSaveAudio = cbClipboardSaveAudio.IsChecked.ToString();
            saveOption();
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
