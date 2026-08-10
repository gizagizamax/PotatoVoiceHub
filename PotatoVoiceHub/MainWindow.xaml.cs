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

        // 直前に報告したA.I.VOICE2の状態。同じ理由を繰り返し出さないために持つ
        Aiv2EditorElem.Availability aiv2AvailabilityLast = Aiv2EditorElem.Availability.Ok;

        /// <summary>
        /// A.I.VOICE2の操作を直列化する。
        ///
        /// HTTPの応答スレッドとクリップボード監視スレッドは、Editorという1つのGUIを共有する。
        /// 「空いているか確認 → キャラクター切替 → セリフ投入 → 再生開始(または保存完了)」を
        /// ひとまとまりにしないと、両方が確認を通り抜けたあとで互いのセリフを
        /// 上書きしてしまう。Aiv2EditorElem側のロックは各メソッドの内側しか守らない。
        /// </summary>
        readonly object aiv2Sync = new object();

        VoiceHubOption option;
        HttpListener listener;
        Thread threadClipboard;
        volatile bool isClipboardThreadRunning;
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
                aiv2EditorElem = null;
                return false;
            }
            if (!aiv2EditorElem.IsAvailable())
            {
                WriteLog("A.I.VOICE2 Editor の画面要素を取得できませんでした。");
                WriteLog("Editor が最小化されていないか、起動直後で描画が済んでいないか確認してください。");
                aiv2EditorElem = null;
                return false;
            }

            // 右ペインが「アクセント」だと、入力欄の1キーごとにEditorが
            // モーラぶんの画面要素を組み直すため、長い文の消去が桁違いに遅くなる
            // (実測: 300文字で15.9秒 → 1.1秒)。読み上げには使わないタブなので寄せておく。
            // 寄せられなくても読み上げ自体はできるので、接続は成功として扱う。
            if (!aiv2EditorElem.SelectVoiceEffectTab())
            {
                WriteLog("A.I.VOICE2 Editor の「音声効果」タブを選べませんでした。");
                WriteLog("「アクセント」タブを開いたままだと、長い文の読み上げが遅くなります。");
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
                    // 確認から操作完了までを、クリップボード監視と奪い合わないよう直列化する
                    lock (aiv2Sync)
                    {
                        ReportAiv2State();

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

                                    // 失敗を握り潰してokを返すと、呼び出し側からは
                                    // 「成功したのにファイルが無い」ようにしか見えない。
                                    // キャラクターを切り替えられなかった場合も、別の声で
                                    // 書き出したものを正常なファイルとして扱われてしまう
                                    response = SetPresetAiv2(queryString["preset"])
                                            && SaveAudioAiv2(sendKeysText, queryString["path"])
                                        ? "{\"status\":\"ok\"}"
                                        : "{\"status\":\"error\"}";
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

                                    // 指定されたキャラクターに切り替えられないまま読み上げると、
                                    // 直前の別のキャラクターの声で喋ったまま成功を返してしまう
                                    response = SetPresetAiv2(queryString["preset"])
                                            && PlayAiv2(sendKeysText)
                                        ? "{\"status\":\"ok\"}"
                                        : "{\"status\":\"error\"}";
                                }
                                break;

                            default:
                                response = "{\"status\":\"\"}";
                                break;
                        }
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

        /// <summary>
        /// A.I.VOICE2を操作できない場合に、その理由をログへ出す。
        ///
        /// 要素を引き当てられないと再生も書き出しも黙って何もしないまま
        /// busyを返し続けるため、利用者からは原因が分からなくなる。
        /// 状態が変わったときだけ出力する(棒読みちゃんは1行ごとにリクエストを送ってくる)。
        /// </summary>
        private void ReportAiv2State()
        {
            var availability = aiv2EditorElem.GetAvailability();
            if (availability == aiv2AvailabilityLast) return;
            aiv2AvailabilityLast = availability;

            switch (availability)
            {
                case Aiv2EditorElem.Availability.Ok:
                    WriteLog("A.I.VOICE2 Editor を認識しました。");
                    break;

                case Aiv2EditorElem.Availability.ProcessNotFound:
                    WriteLog("A.I.VOICE2 Editor が見つかりません。Editor を起動してください。");
                    break;

                case Aiv2EditorElem.Availability.WindowNotFound:
                    WriteLog("A.I.VOICE2 Editor のウィンドウを取得できません。");
                    break;

                case Aiv2EditorElem.Availability.ElementsNotFound:
                    WriteLog("A.I.VOICE2 Editor の画面を認識できないため、操作できません。"
                        + "動作を確認しているのは Editor 2.14 系です。");
                    break;
            }
        }

        private bool IsAiv2KeepPunctuation()
        {
            bool keep;
            return bool.TryParse(option.aiv2KeepPunctuation, out keep) && keep;
        }

        private string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // WM_CHARで直接投入するようになったので、SendKeysのメタ文字({}~+^%()")を
            // 落とす必要は無くなった。改行は\nに揃えるだけでそのまま通る。
            var sanitized = text.Replace("\r\n", "\n").Replace("\r", "\n");

            if (IsAiv2KeepPunctuation())
            {
                return sanitized;
            }

            // A.I.VOICE2の再生ボタンは「現在の文」しか読まないため、
            // 文を分割する文字だけをカンマに置き換えて全体を1文にまとめる。
            // 「、」「､」「．」「.」は文を分割しないので、置換せずそのまま残す。
            return sanitized
                .Replace("。", ", ").Replace("｡", ", ")
                .Replace("？", ", ").Replace("?", ", ")
                .Replace("！", ", ").Replace("!", ", ");
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

            isClipboardThreadRunning = true;
            threadClipboard = new Thread(new ThreadStart(() =>
            {
                // 終了はフラグで伝える(Thread.Abort は使わない)。
                // 旧実装の while (IsVisible) だと、Window_Closing の時点では
                // まだ IsVisible が true なのでそこで Join しても抜けられない
                // (実測: Join(2000)が2000ms丸ごとタイムアウトし、false になるのは
                // Closed になってから)。リスナを閉じる前に確実に止めたいので、
                // 監視を続けるかどうかはウィンドウの可視状態と切り離す。
                while (isClipboardThreadRunning)
                {
                    Thread.Sleep(100);

                    try
                    {
                    // どちらのエンジンも有効でない、またはBusyならスキップ
                    // (個別判定したいが、ここでは簡易的に両方チェック)
                    bool isAiv1Busy = _ttsControlAiv1 != null && _ttsControlAiv1.Status == HostStatus.Busy;
                    bool isAiv2Busy = false;
                    if (aiv2EditorElem != null)
                    {
                        // 状態の読み取りは内部のキャッシュを更新するので、
                        // ロックの外でやるとHTTP側の操作と並行してしまう
                        lock (aiv2Sync)
                        {
                            ReportAiv2State();
                            isAiv2Busy = !aiv2EditorElem.IsEnabledPlay();
                        }
                    }

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
                    bool isAiv2Deferred = false;
                    if (aiv2EditorElem != null)
                    {
                        lock (aiv2Sync)
                        {
                            // 上のbusy判定からここまでの間にHTTP側が操作を始めていることが
                            // あるので、ロックを取ってから見直す
                            if (aiv2EditorElem.IsEnabledPlay())
                            {
                                var sanitizedText = SanitizeText(clipboardText);

                                if (bool.Parse(option.isClipboardSaveAudio))
                                {
                                    SaveAudioAiv2(sanitizedText, BuildClipboardSavePathAiv2(clipboardText));
                                }

                                if (bool.Parse(option.isClipboardPlay))
                                {
                                    PlayAiv2(sanitizedText);
                                }
                            }
                            else
                            {
                                // 処理していないので既読にしない。ここで記録してしまうと、
                                // 手が空いたあとも同じクリップボード内容は二度と読まれない。
                                // ただしA.I.VOICE1が既にこのテキストを処理していたら、
                                // 次の周回でそちらがもう一度読んでしまうので既読にする
                                isAiv2Deferred = _ttsControlAiv1 == null;
                            }
                        }
                    }

                    if (!isAiv2Deferred)
                    {
                        clipboardTextLast = clipboardText;
                    }
                    }
                    catch (Exception ex)
                    {
                        // 1回の失敗で監視ごと死なせない
                        WriteLog("クリップボード監視: " + ex.Message);
                    }
                }
            }));
            threadClipboard.IsBackground = true;
            threadClipboard.Start();
        }

        private void WriteLog(string log)
        {
            // クリップボード監視スレッドからも呼ばれるのでUIスレッドへ回す。
            // 直接触ると例外になり、握り潰されてログが出ないままになる。
            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.Invoke(new Action<string>(WriteLog), log); }
                catch (Exception) { }
                return;
            }

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
                // aiv2KeepPunctuationはここで読まない。
                // 旧バージョンの設定ファイルにはこの項目が無く、bool.Parse(null)で
                // 例外になると下のcatchで設定全体が初期値に戻ってしまう。
                // 既定値の解決は下のTryParse側でまとめて行う。
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

            bool keepPunctuation;
            cbAiv2KeepPunctuation.IsChecked =
                bool.TryParse(option.aiv2KeepPunctuation, out keepPunctuation) && keepPunctuation;
            option.aiv2KeepPunctuation = cbAiv2KeepPunctuation.IsChecked.ToString();
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

        private void cbAiv2KeepPunctuation_Checked(object sender, RoutedEventArgs e)
        {
            option.aiv2KeepPunctuation = cbAiv2KeepPunctuation.IsChecked.ToString();
            saveOption();
        }

        private void cbAiv2KeepPunctuation_Unchecked(object sender, RoutedEventArgs e)
        {
            option.aiv2KeepPunctuation = cbAiv2KeepPunctuation.IsChecked.ToString();
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

        // 再生開始(再生→停止の切り替わり)を待つ上限
        const int Aiv2PlayStartTimeoutMs = 3000;
        // 1文の読み上げ終了を待つ上限
        const int Aiv2PlayFinishTimeoutMs = 120000;
        // 句読点を残すモードで、全文を読み終えるのを待つ上限。
        // 文数に上限が無いため、1文ずつの上限だけでは要求全体の長さを縛れない
        const int Aiv2PlayAllTimeoutMs = 300000;
        // 書き出しダイアログの表示・終了を待つ上限
        const int Aiv2SaveDialogTimeoutMs = 10000;
        // ダイアログが閉じたあと、書き出しの進捗表示が出てくるのを待つ上限。
        // 実測では600ms以内に出るが、出ないまま過ぎたら完了済みとみなす
        const int Aiv2WriteGraceMs = 2000;
        // 書き出しそのものの完了を待つ上限。実測は約2.4秒
        const int Aiv2WriteFinishTimeoutMs = 60000;

        /// <summary>
        /// セリフの反映が1文字も進まないまま待てる時間(ms)。全体の予算ではない。
        /// 反映が進んでいる間は待ち続けるので、テキストが長いというだけでは打ち切らない。
        /// </summary>
        private int GetAiv2TextStallMs()
        {
            int ms;
            return int.TryParse(option.aiv2SendKeysSleep, out ms) && ms > 0 ? ms : 1000;
        }

        private int GetAiv2DelCount()
        {
            int count;
            return int.TryParse(option.aiv2DelCount, out count) && count >= 0 ? count : 10;
        }

        /// <summary>
        /// セリフ入力欄を指定テキストに差し替える。
        /// MSAA+WM_CHARで行うため、フォアグラウンドは奪わない。
        /// </summary>
        private bool SetTextAiv2(string text)
        {
            return ReportAiv2SetText(
                aiv2EditorElem.SetText(text, GetAiv2TextStallMs(), GetAiv2DelCount()));
        }

        // 連続して反映に失敗した回数。同じ長い案内でログが埋まるのを防ぐために数える
        private int aiv2SetTextFailures;

        /// <summary>
        /// SetTextの結果をログに落とす。成功ならtrue。
        ///
        /// 失敗したときは「どの段階で止まったか」「入力欄に何文字残っていたか」を必ず残す。
        /// これが無いと、報告を受けても消去でこけたのか投入でこけたのかを区別できない。
        /// 長い対処案内は連続失敗の1回目だけにして、2回目以降は1行に落とす。
        /// </summary>
        private bool ReportAiv2SetText(Aiv2EditorElem.SetTextResult result)
        {
            if (result.Ok)
            {
                aiv2SetTextFailures = 0;
                return true;
            }

            if (aiv2SetTextFailures++ > 0)
            {
                WriteLog("A.I.VOICE2 反映失敗 " + result.Describe());
                return false;
            }

            if (result.Phase == Aiv2EditorElem.SetTextPhase.Focus)
            {
                WriteLog("A.I.VOICE2 のセリフ入力欄にフォーカスを移せませんでした。"
                    + result.Describe()
                    + " Editorで設定画面などの別ウィンドウが開いていると操作を受け付けません。"
                    + "閉じてから再度お試しください。");
                return false;
            }

            WriteLog("A.I.VOICE2 のセリフ入力欄にテキストを反映できませんでした。"
                + result.Describe()
                + " 反映が止まったまま戻りませんでした。Editor側でセリフ入力欄を空にしてください。"
                + "Editorの表示が崩れて再生ボタンが見えなくなっている場合は、"
                + "空にしないと復帰しません。");
            return false;
        }

        /// <summary>
        /// テキストブロックに割り当てるキャラクターを切り替える。presetがnullなら何もしない。
        /// A.I.VOICE 1.xの CurrentVoicePresetName に相当する。
        ///
        /// 切り替えられなかった場合はfalse。呼び出し側はそこで止めること。
        /// 続けると直前に選ばれていた別のキャラクターの声で読み上げ・保存してしまい、
        /// 呼び出し側からは指定どおりに処理されたようにしか見えない。
        /// </summary>
        private bool SetPresetAiv2(string preset)
        {
            if (string.IsNullOrEmpty(preset)) return true;

            if (!aiv2EditorElem.SetVoicePreset(preset))
            {
                WriteLog("A.I.VOICE2 のキャラクター「" + preset + "」に切り替えられませんでした。"
                    + "指定できるのはキャラクター一覧の10番目までです。");
                return false;
            }
            return true;
        }

        private bool PlayAiv2(string text)
        {
            if (IsAiv2KeepPunctuation())
            {
                // 句読点を残すと再生ボタンは「現在の文」しか読まないので、
                // 1文ずつ入力欄へ入れては再生する。全文を入れて「次の文」で送る方法は
                // Editorを壊すので使わない(理由はAiv2EditorElem.SplitSentences)。
                // 各文の終了を待つ必要があるため、通常モードより長くブロックする。
                Aiv2EditorElem.SetTextResult textResult;
                var result = aiv2EditorElem.PlayAllSentences(text,
                    GetAiv2TextStallMs(), GetAiv2DelCount(),
                    Aiv2PlayStartTimeoutMs, Aiv2PlayFinishTimeoutMs, Aiv2PlayAllTimeoutMs,
                    out textResult);

                switch (result)
                {
                    case Aiv2EditorElem.PlayAllResult.Ok:
                        aiv2SetTextFailures = 0;
                        return true;
                    case Aiv2EditorElem.PlayAllResult.TextFailed:
                        // 失敗の詳細をログに残す。結果はfalse固定
                        return ReportAiv2SetText(textResult);
                    default:
                        WriteLog("A.I.VOICE2 の文ごと再生が最後まで完了しませんでした。");
                        return false;
                }
            }

            // 「...」や「!!」だけのように読み上げるものが無いテキストで再生を押すと
            // Editorが落ちる。読ませるものが無いだけなので、何もせず成功として返す
            if (!Aiv2EditorElem.HasSomethingToSpeak(text)) return true;

            if (!SetTextAiv2(text)) return false;

            // 停止ボタンに変わる前に次のメッセージが来ると、セリフを上書きした上で
            // 停止ボタンを押してしまうので、再生が始まるまで待つ
            if (!aiv2EditorElem.StartPlay(Aiv2PlayStartTimeoutMs))
            {
                WriteLog("A.I.VOICE2 の再生を開始できませんでした。");
                return false;
            }
            return true;
        }

        /// <param name="path">
        /// 保存先。nullまたは空なら、Editorがダイアログに入れてきた既定のファイル名を
        /// そのまま使う(「ファイル命名規則で指定」に任せる従来どおりの動作)。
        /// </param>
        private bool SaveAudioAiv2(string text, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                WriteLog("保存先が指定されていないため、Editorの命名規則に任せます。"
                    + "場所を指定する場合は path にフルパスを渡してください。");
            }
            else
            {
                // 相対パスのままだと、フォルダ作成はこちらの実行ディレクトリ基準、
                // ファイル名欄の解決は共通ダイアログの現在フォルダ基準になり、
                // 書き込んだ内容を読み戻せても同じ場所を指している保証がない
                try
                {
                    path = Path.GetFullPath(path);
                }
                catch (Exception ex)
                {
                    WriteLog("保存先として使えないパスです: " + path + " (" + ex.Message + ")");
                    return false;
                }
            }

            // 読み上げるものが無いテキストのまま操作するとEditorが落ちうる(再生では実機確認済み)。
            // 書き出しても中身の無い音声にしかならないので、ここで止める
            if (!Aiv2EditorElem.HasSomethingToSpeak(text))
            {
                WriteLog("A.I.VOICE2 に読み上げるものが無いため、書き出しませんでした: " + text);
                return false;
            }

            if (!SetTextAiv2(text)) return false;

            // 書き出しダイアログはEditorが前面に出す。こちらからは制御できないので、
            // 元々前面だったウィンドウを覚えておいて後で戻す。
            var foregroundWindowHwd = Win32Api.GetForegroundWindow();

            var dialog = aiv2EditorElem.StartWrite(Aiv2SaveDialogTimeoutMs);
            if (dialog == IntPtr.Zero)
            {
                WriteLog("書き出しダイアログが表示されませんでした。"
                    + "書き出し設定を「ファイル命名規則で指定」にしてください。");
                return false;
            }

            if (!string.IsNullOrEmpty(path) && !SetSaveAudioPathAiv2(dialog, path))
            {
                // 保存先を指定できないまま保存すると、意図しない場所に書き出されてしまう
                aiv2EditorElem.CancelSaveDialog(dialog, Aiv2SaveDialogTimeoutMs);
                Win32Api.SetForegroundWindow(foregroundWindowHwd);
                return false;
            }

            bool saved = true;
            var fileName = aiv2EditorElem.GetSaveDialogFileName(dialog);

            // 同名ファイルがあると共通ダイアログが確認を挟む。ここで存在を確かめておいて、
            // 上書きになると分かっているときだけ自動で肯定させる。
            // 「ボタンが2つのダイアログ」というだけで押すと、別の警告まで肯定してしまう
            var overwrite = IsExistingFilePath(fileName);
            if (overwrite)
            {
                WriteLog("上書きします: " + fileName);
            }

            if (!aiv2EditorElem.AcceptSaveDialog(dialog, Aiv2SaveDialogTimeoutMs, overwrite))
            {
                // 開いたままにするとEditorが以降の操作を受け付けなくなるので畳んでおく
                WriteLog("書き出しダイアログを閉じられませんでした。"
                    + "想定していない確認ダイアログが出た場合も、肯定せずにここへ来ます。");
                aiv2EditorElem.CancelSaveDialog(dialog, Aiv2SaveDialogTimeoutMs);
                saved = false;
            }
            else
            {
                // ダイアログが閉じても書き出しはまだ終わっていない。完了を待たずに返すと、
                // 直後の要求が「書き出し中」のEditorにぶつかって失敗する
                if (!aiv2EditorElem.WaitUntilWriteFinished(Aiv2WriteGraceMs, Aiv2WriteFinishTimeoutMs))
                {
                    WriteLog("書き出しが完了したことを確認できませんでした。");
                    saved = false;
                }
                else if (!string.IsNullOrEmpty(fileName))
                {
                    WriteLog("書き出し: " + fileName);
                }
            }

            Win32Api.SetForegroundWindow(foregroundWindowHwd);
            return saved;
        }

        /// <summary>
        /// そのパスに既にファイルがあるか。ダイアログに入っている名前は、こちらで
        /// 指定しなかった場合はフォルダを含まないことがあり、その場合どこを指すのか
        /// 分からないので「無い」として扱う。
        /// </summary>
        private static bool IsExistingFilePath(string path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) && Path.IsPathRooted(path) && File.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// クリップボード監視で使う保存先を「保存先」設定の命名規則から組み立てる。
        /// A.I.VOICE1側と同じ規則を使う。組み立てられなければnull(=Editor既定のファイル名)。
        /// </summary>
        private string BuildClipboardSavePathAiv2(string clipboardText)
        {
            if (string.IsNullOrEmpty(option.saveAudioPath)) return null;

            var preset = aiv2EditorElem.GetCurrentVoicePresetName();

            var path = option.saveAudioPath
                .Replace("{yyyyMMdd}", DateTime.Now.ToString("yyyyMMdd"))
                .Replace("{HHmmss}", DateTime.Now.ToString("HHmmss"))
                .Replace("{VoicePreset}", preset ?? "")
                .Replace("{Text}", clipboardText.Length > 10 ? clipboardText.Substring(0, 10) : clipboardText);

            if (path.Length > 256) path = path.Substring(0, 256);
            return path;
        }

        /// <summary>
        /// 書き出しダイアログに保存先を入れる。共通ダイアログは存在しないフォルダを
        /// 受け付けないので、先に作っておく。
        /// </summary>
        private bool SetSaveAudioPathAiv2(IntPtr dialog, string path)
        {
            try
            {
                var directory = new FileInfo(path).Directory;
                if (directory != null) directory.Create();
            }
            catch (Exception ex)
            {
                WriteLog("保存先フォルダを作成できませんでした: " + ex.Message);
                return false;
            }

            if (!aiv2EditorElem.SetSaveDialogFileName(dialog, path))
            {
                WriteLog("書き出しダイアログに保存先を指定できませんでした: " + path);
                return false;
            }
            return true;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Thread.Abort は .NET Core 以降で使えず、途中の状態も壊すのでフラグで止める
            isClipboardThreadRunning = false;
            if (threadClipboard != null)
            {
                threadClipboard.Join(2000);
                threadClipboard = null;
            }

            if (listener != null)
            {
                try { listener.Close(); } catch (Exception) { }
                listener = null;
            }
        }
    }
}
