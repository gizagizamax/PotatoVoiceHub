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
                            response = _ttsControlAiv1.Status == HostStatus.Busy ? "{\"status\":\"busy\"}" : "{\"status\":\"idle\"}";
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
                            response = !aiv2EditorElem.IsEnabledPlay() ? "{\"status\":\"busy\"}" : "{\"status\":\"idle\"}";
                            break;

                        case "/saveAudio":
                            if (!aiv2EditorElem.IsEnabledPlay())
                            {
                                response = "{\"status\":\"busy\"}";
                            }
                            else
                            {
                                var queryString = HttpUtility.ParseQueryString(context.Request.Url.Query, Encoding.GetEncoding(option.saveAudioEncode));
                                var sendKeysText = SanitizeText(queryString["text"]);

                                //プリセットの機能は無し

                                SaveAudioAiv2(sendKeysText);

                                response = "{\"status\":\"ok\"}";
                            }
                            break;

                        case "/play":
                            if (!aiv2EditorElem.IsEnabledPlay())
                            {
                                response = "{\"status\":\"busy\"}";
                            }
                            else
                            {
                                var queryString = HttpUtility.ParseQueryString(context.Request.Url.Query);
                                var sendKeysText = SanitizeText(queryString["text"]);

                                //プリセットの機能は無し

                                PlayAiv2(sendKeysText);

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

        private string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // エスケープが必要な文字は消す
            var sanitized = text
                .Replace("{", "").Replace("}", "")
                .Replace("~", "")
                .Replace("+", "")
                .Replace("^", "")
                .Replace("%", "")
                .Replace("\r", "").Replace("\n", "")
                .Replace("\"", "")
                .Replace("(", "").Replace(")", "");

            // 句読点があるとA.I.VOICE2が止まるのでカンマにする
            sanitized = sanitized
                .Replace("。", ", ").Replace("｡", ", ")
                .Replace("、", ", ").Replace("､", ", ")
                .Replace("？", ", ").Replace("?", ", ")
                .Replace("！", ", ").Replace("!", ", ")
                .Replace("．", ", ").Replace(".", ", ");

            return sanitized;
        }

        private string GetClipboardTextSafe()
        {
            try
            {
                string text = "";
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        text = Clipboard.GetText().Trim();
                    }
                    catch { }
                });
                return text;
            }
            catch
            {
                return "";
            }
        }

        private void initClipboard()
        {
            if (threadClipboard != null) return;

            // 初期値取得
            clipboardTextLast = GetClipboardTextSafe();

            threadClipboard = new Thread(new ThreadStart(() =>
            {
                while (IsVisible)
                {
                    Thread.Sleep(100);

                    // どちらのエンジンも有効でない、またはBusyならスキップ
                    // (個別判定したいが、ここでは簡易的に両方チェック)
                    bool isAiv1Busy = _ttsControlAiv1 != null && _ttsControlAiv1.Status == HostStatus.Busy;
                    bool isAiv2Busy = aiv2EditorElem != null && !aiv2EditorElem.IsEnabledPlay();

                    if (isAiv1Busy || isAiv2Busy)
                    {
                        continue;
                    }

                    var clipboardText = GetClipboardTextSafe();
                    if (string.IsNullOrEmpty(clipboardText) || clipboardText == clipboardTextLast)
                    {
                        continue;
                    }

                    // A.I.VOICE1処理
                    if (_ttsControlAiv1 != null)
                    {
                        // 接続確認
                        if (_ttsControlAiv1.Status == HostStatus.NotConnected)
                        {
                            try { _ttsControlAiv1.Connect(); } catch { }
                        }

                        if (bool.Parse(option.isClipboardSaveAudio))
                        {
                            var fileName = option.saveAudioPath
                                .Replace("{yyyyMMdd}", DateTime.Now.ToString("yyyyMMdd"))
                                .Replace("{HHmmss}", DateTime.Now.ToString("HHmmss"))
                                .Replace("{VoicePreset}", _ttsControlAiv1.CurrentVoicePresetName)
                                .Replace("{Text}", clipboardText.Length > 10 ? clipboardText.Substring(0, 10) : clipboardText);

                            if (fileName.Length > 256) fileName = fileName.Substring(0, 256);
                            try
                            {
                                new FileInfo(fileName).Directory.Create();
                                _ttsControlAiv1.Text = clipboardText;
                                _ttsControlAiv1.SaveAudioToFile(fileName);
                            }
                            catch (Exception ex) { WriteLog(ex.Message); }
                        }

                        if (bool.Parse(option.isClipboardPlay))
                        {
                            try
                            {
                                _ttsControlAiv1.Text = clipboardText;
                                _ttsControlAiv1.Play();
                            }
                            catch { }
                        }
                    }

                    // A.I.VOICE2処理
                    if (aiv2EditorElem != null)
                    {
                        var sanitizedText = SanitizeText(clipboardText);

                        if (bool.Parse(option.isClipboardSaveAudio))
                        {
                            SaveAudioAiv2(sanitizedText);
                        }

                        if (bool.Parse(option.isClipboardPlay))
                        {
                            PlayAiv2(sanitizedText);
                        }
                    }

                    clipboardTextLast = clipboardText;
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
                txtAiv2SendKeysSleep.Text = option.aiv2SendKeysSleep;
                txtAiv2DelCount.Text = option.aiv2DelCount;
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

            txtAiv2SendKeysSleep.Text =
                string.IsNullOrEmpty(option.aiv2SendKeysSleep) ?
                "1000" :
                option.aiv2SendKeysSleep;

            txtAiv2DelCount.Text =
                string.IsNullOrEmpty(option.aiv2DelCount) ?
                "10" :
                option.aiv2DelCount;

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

        private void txtAiv2SendKeysSleep_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                option.aiv2SendKeysSleep = int.Parse(txtAiv2SendKeysSleep.Text).ToString();
            }
            catch (Exception)
            {
                option.aiv2SendKeysSleep = "1000";
            }
            saveOption();
        }

        private void txtAiv2DelCount_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                option.aiv2DelCount = int.Parse(txtAiv2DelCount.Text).ToString();
            }
            catch (Exception)
            {
                option.aiv2DelCount = "10";
            }
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

        private void PlayAiv2(string text)
        {
            var foregroundWindowHwd = Win32Api.GetForegroundWindow();
            Win32Api.SetForegroundWindow(aiv2EditorElem.GetHandle());
            aiv2EditorElem.SetFocusMainWindow();

            // 意味不明だが100回繰り返すと文章を消せる確率が大幅アップする
            System.Windows.Forms.SendKeys.SendWait("^z");
            //for (int i = 0; i < int.Parse(option.aiv2DelCount); i++)
            //{
            //    System.Windows.Forms.SendKeys.SendWait("{LEFT}");
            //    System.Windows.Forms.SendKeys.SendWait("{RIGHT}");
            //    System.Windows.Forms.SendKeys.SendWait("^a");
            //    Thread.Sleep(1);
            //}

            System.Windows.Forms.SendKeys.SendWait(text);
            //再生ボタンが押せるようになるまで待つ
            Thread.Sleep(int.Parse(option.aiv2SendKeysSleep));
            aiv2EditorElem.InvokePlay();

            Win32Api.SetForegroundWindow(foregroundWindowHwd);

            //再生ボタンが停止ボタンになる前に次のメッセージが来ると、メッセージを上書きして停止ボタンを押してしまうので、
            //再生後は停止ボタンになるまで待つ
            for (DateTime dt = DateTime.Now; dt > DateTime.Now.AddSeconds(-3);)
            {
                if (aiv2EditorElem.IsEnabledPlay())
                {
                    Thread.Sleep(10);
                    continue;
                }
                break;
            }
        }

        private void SaveAudioAiv2(string text)
        {
            var foregroundWindowHwd = Win32Api.GetForegroundWindow();
            Win32Api.SetForegroundWindow(aiv2EditorElem.GetHandle());
            aiv2EditorElem.SetFocusMainWindow();

            // 意味不明だが100回繰り返すと文章を消せる確率が大幅アップする
            System.Windows.Forms.SendKeys.SendWait("^z");
            //for (int i = 0; i < int.Parse(option.aiv2DelCount); i++)
            //{
            //    System.Windows.Forms.SendKeys.SendWait("{LEFT}");
            //    System.Windows.Forms.SendKeys.SendWait("{RIGHT}");
            //    System.Windows.Forms.SendKeys.SendWait("^a");
            //    Thread.Sleep(1);
            //}

            System.Windows.Forms.SendKeys.SendWait(text);
            //再生ボタンが押せるようになるまで待つ
            Thread.Sleep(int.Parse(option.aiv2SendKeysSleep));
            aiv2EditorElem.InvokeWrite1();

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
                aiv2EditorElem.InvokeWrite2();
            }

            Win32Api.SetForegroundWindow(foregroundWindowHwd);
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
