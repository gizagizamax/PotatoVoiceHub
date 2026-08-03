using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace PotatoVoiceHub
{
    /// <summary>
    /// A.I.VOICE2 EditorのGUIを操作する。
    ///
    /// 2.13以前はWPF製でUI Automationから触れたが、2.14系はFlutter製に変わり
    /// UIAツリーが空になった(FindAll(Descendants)が0件)。そのためUIAではなく
    /// MSAA(IAccessible)でツリーを読み、テキストはWM_CHARで投入する。
    /// この方式ならSetForegroundWindow/SendKeysが不要で、フォアグラウンドを奪わない。
    ///
    /// 書き出しダイアログだけはEditor側がネイティブのコモンダイアログ(#32770)を
    /// 前面に出すため、そこはWin32のコントロールIDで操作する。
    /// </summary>
    public class Aiv2EditorElem
    {
        public const string NamePlay = "再生";
        public const string NameStop = "停止";
        public const string NameWrite = "書き出し";

        // キャラクター切り替えに使うメニューと、左のキャラクター一覧の行グループ
        const string NameMenuCharacter = "キャラクター";
        const string NameMenuAssign = "テキストブロックに割り当て";
        const string NameMenuNthCharacterSuffix = "番目のキャラクター";
        const string NameGroupTuning = "キャラクターチューニング";

        const string EditorWindowTitle = "A.I.VOICE2 Editor";
        const string EditorProcessName = "aivoice";
        const string FlutterViewClass = "FLUTTERVIEW";
        const string DialogClass = "#32770";
        const string ButtonClass = "Button";

        // コモンダイアログの標準コントロールID(ロケール非依存)
        const int IDOK = 1;
        const int IDCANCEL = 2;
        const int IDC_FILENAME_EDIT = 1001;

        /// <summary>ダイアログに押下を投げてから、閉じたかどうかを見るまでの待ち時間(ms)。</summary>
        const int PromptSettleMs = 300;

        const int MaxTreeDepth = 40;

        /// <summary>
        /// 再生ボタンを押し直す前の待ち時間(ms)。accDoDefaultActionは稀に空振りするので、
        /// 反応が無ければ押し直す。間を置かずに押し直しても同じく取りこぼされる。
        /// </summary>
        const int PlayRetrySettleMs = 200;

        /// <summary>
        /// Editor起動後の最初の走査で、Flutterがセマンティクスを組み上げるのを待つ回数と間隔。
        /// 待つのは一度も要素を引き当てられていない間だけ。
        /// </summary>
        const int SemanticsBuildRetries = 5;
        const int SemanticsBuildWaitMs = 200;

        /// <summary>書き出し完了待ちのポーリング間隔(ms)。ツリー全走査が約90msかかるので粗くてよい。</summary>
        const int WritePollMs = 100;

        /// <summary>A.I.VOICE2 Editorが文として分割する文字。実機で確認済み。
        /// 「、」「､」「．」「.」「改行」は分割しないので触らなくてよい。</summary>
        static readonly char[] SentenceTerminators = { '。', '｡', '？', '?', '！', '!' };

        Process cachedProcess;
        IntPtr cachedViewHandle = IntPtr.Zero;

        // MSAAノードは存在する限り参照が生きるのでキャッシュする。
        // クリップボード監視が100ms毎にIsEnabledPlay()を呼ぶため、
        // 毎回ツリー全体(80要素超)を走査すると重い。
        Aiv2Native.IAccessible cachePlay, cacheWrite, cacheSerif;

        // このEditorから一度でも要素を引き当てられたか。
        // Flutterはアクセシビリティの要求を受けて初めてセマンティクスを構築するため、
        // Editor起動後の最初の1回だけ、まだ組み上がっていないツリーが返ってくる。
        // 組み上がるまで待つのは最初だけでよい(以降の失敗はEditor側の異常)。
        bool hasSeenElements;

        readonly object sync = new object();

        // ---------------------------------------------------------------
        // プロセスとウィンドウ
        // ---------------------------------------------------------------

        public Process GetProcess()
        {
            var p = cachedProcess;
            if (p != null)
            {
                try
                {
                    if (!p.HasExited && p.MainWindowHandle != IntPtr.Zero) return p;
                }
                catch { }
                cachedProcess = null;
                Reset();
            }

            foreach (Process candidate in Process.GetProcesses())
            {
                try
                {
                    if (candidate.ProcessName == EditorProcessName &&
                        candidate.MainWindowTitle.Contains(EditorWindowTitle))
                    {
                        cachedProcess = candidate;
                        return candidate;
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>WM_CHARの宛先かつMSAAの起点になるFLUTTERVIEW子ウィンドウ。</summary>
        public IntPtr GetViewHandle()
        {
            if (cachedViewHandle != IntPtr.Zero && Aiv2Native.IsWindow(cachedViewHandle))
            {
                return cachedViewHandle;
            }
            Reset();

            var p = GetProcess();
            if (p == null) return IntPtr.Zero;

            IntPtr main;
            try { main = p.MainWindowHandle; }
            catch { return IntPtr.Zero; }
            if (main == IntPtr.Zero) return IntPtr.Zero;

            var view = Aiv2Native.FindChildByClass(main, FlutterViewClass);
            // 将来Flutter以外の構成に戻った場合に備え、見つからなければメインウィンドウを使う
            cachedViewHandle = view != IntPtr.Zero ? view : main;
            return cachedViewHandle;
        }

        void Reset()
        {
            cachedViewHandle = IntPtr.Zero;
            cachePlay = cacheWrite = cacheSerif = null;
            // Editorのプロセスが変わったらセマンティクスも作り直しになる
            hasSeenElements = false;
        }

        /// <summary>Editorを操作できない理由。</summary>
        public enum Availability
        {
            Ok,
            /// <summary>Editorのプロセスが見つからない。</summary>
            ProcessNotFound,
            /// <summary>プロセスはあるがウィンドウを取得できない。</summary>
            WindowNotFound,
            /// <summary>ウィンドウはあるが、操作に使う要素を認識できない。</summary>
            ElementsNotFound,
        }

        /// <summary>
        /// 操作可能かどうかを、駄目ならその理由つきで返す。
        ///
        /// この実装はEditor 2.14系(Flutter製)の画面構成を前提にしている。
        /// 構成の違うバージョンでは要素を引き当てられずElementsNotFoundになるが、
        /// それを黙って握り潰すと利用者からは「何も起きない」ようにしか見えない。
        /// 呼び出し側でログに出せるよう理由を返す。
        /// </summary>
        public Availability GetAvailability()
        {
            if (GetProcess() == null) return Availability.ProcessNotFound;
            if (GetViewHandle() == IntPtr.Zero) return Availability.WindowNotFound;
            return FindPlayButton() != null ? Availability.Ok : Availability.ElementsNotFound;
        }

        /// <summary>Editorが操作可能な状態か。再生ボタンが引き当てられることを条件にする。</summary>
        public bool IsAvailable()
        {
            return GetAvailability() == Availability.Ok;
        }

        // ---------------------------------------------------------------
        // 要素の引き当て
        // ---------------------------------------------------------------

        /// <summary>
        /// ツリー全体を1回走査して、使う要素をまとめてキャッシュに入れ直す。
        /// 全走査は実測で約90msかかるので、要素ごとに走査すると馬鹿にならない。
        ///
        /// Editor起動後の最初の走査だけは、Flutterがセマンティクスを構築し終える前の
        /// ツリーが返ってくることがある(実機で再現)。一度も引き当てられていない間に限り、
        /// 少し待って組み上がるのを待つ。以降の失敗はEditor側の異常なので待たない。
        /// </summary>
        void RefreshCache()
        {
            ScanTree();
            if (hasSeenElements) return;

            for (int i = 0; i < SemanticsBuildRetries && cachePlay == null; i++)
            {
                Thread.Sleep(SemanticsBuildWaitMs);
                ScanTree();
            }
        }

        void ScanTree()
        {
            var view = GetViewHandle();
            if (view == IntPtr.Zero) return;

            cachePlay = cacheWrite = cacheSerif = null;

            foreach (var node in Aiv2Native.Dump(view, MaxTreeDepth))
            {
                if (node.Role == Aiv2Native.RolePushButton)
                {
                    if (cachePlay == null && (node.Name == NamePlay || node.Name == NameStop)) cachePlay = node.Acc;
                    else if (cacheWrite == null && node.Name == NameWrite) cacheWrite = node.Acc;
                }
                else if (node.Role == Aiv2Native.RoleEditText)
                {
                    // nameが空のものがセリフ入力欄。nameを持つ方はキャラクター検索欄
                    if (cacheSerif == null && string.IsNullOrEmpty(node.Name)) cacheSerif = node.Acc;
                }
            }

            if (cachePlay != null) hasSeenElements = true;
        }

        /// <summary>キャッシュが今も同じボタンを指しているか確かめ、駄目なら走査し直す。</summary>
        Aiv2Native.IAccessible FindButton(Func<Aiv2Native.IAccessible> get, params string[] names)
        {
            var cached = get();
            if (cached != null)
            {
                var name = Aiv2Native.GetName(cached);
                if (name != null && Array.IndexOf(names, name) >= 0) return cached;
            }

            RefreshCache();
            cached = get();
            if (cached == null) return null;

            var refreshed = Aiv2Native.GetName(cached);
            return refreshed != null && Array.IndexOf(names, refreshed) >= 0 ? cached : null;
        }

        Aiv2Native.IAccessible FindPlayButton()
        {
            return FindButton(() => cachePlay, NamePlay, NameStop);
        }

        /// <summary>
        /// セリフ入力欄。role=ROLE_SYSTEM_TEXTかつnameが空という条件で引き当てる。
        /// nameを持つ編集可能テキストはキャラクター検索欄なので除外される。
        /// </summary>
        Aiv2Native.IAccessible FindSerifBox()
        {
            var cached = cacheSerif;
            if (cached != null)
            {
                var name = Aiv2Native.GetName(cached);
                if (name != null && name.Length == 0 && Aiv2Native.GetRole(cached) == Aiv2Native.RoleEditText)
                {
                    return cached;
                }
            }

            RefreshCache();
            return cacheSerif;
        }

        // ---------------------------------------------------------------
        // 状態の取得
        // ---------------------------------------------------------------

        enum PlayButtonState
        {
            /// <summary>再生ボタンが見つからない。Editorがエラー画面に切り替わった場合などに起きる。</summary>
            Missing,
            /// <summary>「再生」表示で押せる。読み上げ中ではない。</summary>
            Idle,
            /// <summary>「停止」表示、または「再生」だが押せない。読み上げ中とみなす。</summary>
            Busy,
        }

        PlayButtonState GetPlayButtonState()
        {
            var play = FindPlayButton();
            if (play == null) return PlayButtonState.Missing;

            var name = Aiv2Native.GetName(play);
            if (name == NameStop) return PlayButtonState.Busy;
            if (name != NamePlay) return PlayButtonState.Missing;

            // stateを読めなかった場合はBusy扱いにする。読めないことを「押せる」と解釈すると、
            // 読み上げ中に次の操作を始めてセリフを上書きしてしまう
            uint state;
            if (!Aiv2Native.TryGetState(play, out state)) return PlayButtonState.Busy;

            return (state & Aiv2Native.StateUnavailable) == 0
                ? PlayButtonState.Idle
                : PlayButtonState.Busy;
        }

        /// <summary>再生ボタンが「再生」表示で、かつ押せる状態か。停止中(=読み上げ中でない)の判定に使う。</summary>
        public bool IsEnabledPlay()
        {
            return GetPlayButtonState() == PlayButtonState.Idle;
        }

        /// <summary>セリフ入力欄の現在のテキスト。取得できない場合はnull。</summary>
        public string GetText()
        {
            var serif = FindSerifBox();
            if (serif == null) return null;
            return Aiv2Native.GetValue(serif);
        }

        /// <summary>
        /// Editorに読み上げさせられるテキストか。<b>再生・書き出しを押す前に必ず通すこと。</b>
        ///
        /// 読み上げるものが無いテキスト(「？」「...」「」など)を入れた状態で再生を押すと、
        /// Editorが範囲外アクセス(RangeError)でエラー画面に落ち、再起動が必要になる。
        /// 押しさえしなければ落ちないので、押す側で弾く。
        /// 詳細はdocs/findings-2026-07-29.md §21。
        ///
        /// 判定は「文字が1つでも入っているか」だけにしている。Editorの読み仮名ペインを
        /// 見れば「読めるか」を直接聞けるが、テキストを差し替えた直後はまだ前の文の
        /// 読み仮名が残っており、遅れて空になる。待ち時間に依存する判定は、
        /// 外したときの代償がEditorのクラッシュなので採らない。
        ///
        /// そのぶん「％」のように、記号だけでもEditorが読めるもの(「パーセント」と読む)を
        /// 取りこぼす。文全体が記号だけの場合に限られるので、読み上げないことで受け入れる。
        /// </summary>
        public static bool HasSomethingToSpeak(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var c in text)
            {
                if (char.IsLetterOrDigit(c)) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------
        // テキスト投入
        // ---------------------------------------------------------------

        /// <summary>
        /// セリフ入力欄を空にする。再生後はキャレットが先頭に戻ることがあり、
        /// BackSpaceだけでは1文字も消えない。Deleteも同数送って前後どちらでも消えるようにする。
        /// </summary>
        public void ClearText(int extraKeys)
        {
            var view = GetViewHandle();
            if (view == IntPtr.Zero) return;

            // これを打たないとWM_CHAR/WM_KEYDOWNが握り潰される。詳細はAiv2Native.TakeFocus
            FocusSerifBox();

            var current = GetText();
            int length = current == null ? 0 : current.Length;
            int count = length + Math.Max(0, extraKeys);
            if (count <= 0) return;

            Aiv2Native.PostKey(view, Aiv2Native.VK_BACK, count);
            Aiv2Native.PostKey(view, Aiv2Native.VK_DELETE, count);
        }

        /// <summary>
        /// セリフ入力欄を指定テキストに置き換える。
        /// WM_CHARは非同期に処理されるので、反映されるまでポーリングで待つ。
        ///
        /// timeoutMsは「消えるまで」「反映されるまで」1回ぶんの上限なので、
        /// この呼び出し全体はその倍まで伸びうる。締切のある経路からは
        /// 残り時間を渡すオーバーロードの方を使うこと。
        ///
        /// 「現在の文」の位置はここでは触らない。位置を先頭に保てるのは、
        /// 一度先頭になった後、呼び出し側が常に1文ずつ渡し、かつ利用者が
        /// Editor上で位置を動かさない間だけである。詳細はSplitSentences()を参照。
        /// </summary>
        public bool SetText(string text, int timeoutMs, int extraKeys)
        {
            // 締切を持たない呼び出し。個々の待ちにtimeoutMsをそのまま使う
            return SetText(text, Stopwatch.StartNew(), timeoutMs, int.MaxValue, extraKeys);
        }

        /// <summary>
        /// 締切つきのSetText。個々の待ちには「1回ぶんの上限」と「締切までの残り」の
        /// 短い方しか渡さないので、締切を跨いで待ち続けることがない。
        /// </summary>
        bool SetText(string text, Stopwatch elapsed, int timeoutMs, int totalTimeoutMs, int extraKeys)
        {
            lock (sync)
            {
                var view = GetViewHandle();
                if (view == IntPtr.Zero) return false;
                if (text == null) text = "";

                return ReplaceText(view, text, elapsed, timeoutMs, totalTimeoutMs, extraKeys);
            }
        }

        /// <summary>入力欄を空にしてから投入する。空になり切るまでは投入しない。</summary>
        bool ReplaceText(IntPtr view, string text, Stopwatch elapsed, int timeoutMs, int totalTimeoutMs,
            int extraKeys)
        {
            ClearText(extraKeys);
            if (!WaitForText("", Remaining(elapsed, timeoutMs, totalTimeoutMs)))
            {
                // 消え切らないまま投入すると前のセリフに連結されるので中断する
                return false;
            }

            Aiv2Native.PostText(view, text);
            return WaitForText(text, Remaining(elapsed, timeoutMs, totalTimeoutMs));
        }

        /// <summary>1回ぶんの上限と締切までの残りの、短い方。</summary>
        static int Remaining(Stopwatch elapsed, int timeoutMs, int totalTimeoutMs)
        {
            return Math.Min(timeoutMs, totalTimeoutMs - (int)elapsed.ElapsedMilliseconds);
        }

        /// <summary>
        /// セリフ入力欄へキーボードフォーカスを戻す。
        /// キーを投げる前に必ず通すこと。Flutter側のフォーカス処理はUIスレッドで
        /// 非同期に走るので、直後に投げたキーが間に合うよう少しだけ待つ。
        /// </summary>
        bool FocusSerifBox()
        {
            var serif = FindSerifBox();
            if (serif == null || !Aiv2Native.TakeFocus(serif)) return false;
            Thread.Sleep(FocusSettleMs);
            return true;
        }

        const int FocusSettleMs = 100;

        bool WaitForText(string expected, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                var current = GetText();
                if (current != null && current == expected) return true;
                if (sw.ElapsedMilliseconds >= timeoutMs) return false;
                Thread.Sleep(20);
            }
        }

        // ---------------------------------------------------------------
        // ボタン操作
        // ---------------------------------------------------------------

        public bool InvokePlay()
        {
            var play = FindPlayButton();
            return play != null && Aiv2Native.DoDefaultAction(play);
        }

        /// <summary>再生の押し直しを含めた試行回数。</summary>
        const int StartPlayAttempts = 2;

        /// <summary>
        /// 再生を開始し、実際に始まったことまで確認する。
        /// accDoDefaultActionは稀に取りこぼされるので1度だけ押し直す。
        ///
        /// startTimeoutMsは「押してから始まるまで」1回ぶんの上限なので、
        /// 押し直しを含めた全体はその倍まで伸びうる。締切のある経路からは
        /// 残り時間を渡すオーバーロードの方を使うこと。
        /// </summary>
        public bool StartPlay(int startTimeoutMs)
        {
            for (int attempt = 0; attempt < StartPlayAttempts; attempt++)
            {
                if (!InvokePlay()) return false;
                if (WaitUntilPlaying(startTimeoutMs)) return true;
                Thread.Sleep(PlayRetrySettleMs);
            }
            return false;
        }

        /// <summary>
        /// 締切つきのStartPlay。押し直しの前にも残り時間を計算し直すので、
        /// 試行回数が増えても締切ぶんより延びない。
        /// </summary>
        bool StartPlay(Stopwatch elapsed, int startTimeoutMs, int totalTimeoutMs)
        {
            for (int attempt = 0; attempt < StartPlayAttempts; attempt++)
            {
                var remaining = totalTimeoutMs - (int)elapsed.ElapsedMilliseconds;
                if (remaining <= 0) return false;

                if (!InvokePlay()) return false;
                if (WaitUntilPlaying(Math.Min(startTimeoutMs, remaining))) return true;

                // 待つのは押し直す前だけ。締切を跨いで眠らないよう残り時間で切る
                if (attempt + 1 >= StartPlayAttempts) break;
                var settle = Math.Min(PlayRetrySettleMs, totalTimeoutMs - (int)elapsed.ElapsedMilliseconds);
                if (settle <= 0) break;
                Thread.Sleep(settle);
            }
            return false;
        }

        public bool InvokeWrite()
        {
            var write = FindButton(() => cacheWrite, NameWrite);
            return write != null && Aiv2Native.DoDefaultAction(write);
        }

        /// <summary>再生ボタンが「停止」に変わる(=読み上げ開始)まで待つ。</summary>
        public bool WaitUntilPlaying(int timeoutMs)
        {
            return WaitForPlayButton(PlayButtonState.Busy, timeoutMs, 10);
        }

        /// <summary>再生ボタンが「再生」に戻る(=読み上げ終了)まで待つ。</summary>
        public bool WaitUntilIdle(int timeoutMs)
        {
            return WaitForPlayButton(PlayButtonState.Idle, timeoutMs, 20);
        }

        /// <summary>
        /// 書き出しが終わってEditorを再び操作できるようになるまで待つ。
        ///
        /// 保存ダイアログが閉じても書き出しはまだ終わっていない。実測では
        /// ダイアログが閉じてから約1.8秒後にファイルが現れ、その間Editorは進捗表示に
        /// 切り替わってMSAAツリーが8要素まで縮む(ボタンが1つも見えなくなる)。
        /// この状態で次の要求を処理しようとすると、書き出しボタンを引き当てられず
        /// 「ダイアログが出なかった」として失敗する。
        ///
        /// ボタンが見えない状態はEditorが落ちたときと区別がつかないので、
        /// 進捗表示が出るのを待つ時間はgraceMsで区切る。出ないまま過ぎたら
        /// 「もう終わっている」とみなす。
        /// </summary>
        public bool WaitUntilWriteFinished(int graceMs, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < graceMs)
            {
                if (GetPlayButtonState() == PlayButtonState.Missing) break;
                Thread.Sleep(WritePollMs);
            }

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (GetPlayButtonState() == PlayButtonState.Idle) return true;
                Thread.Sleep(WritePollMs);
            }
            return false;
        }

        bool WaitForPlayButton(PlayButtonState wanted, int timeoutMs, int pollMs)
        {
            var sw = Stopwatch.StartNew();
            int missing = 0;
            while (true)
            {
                var state = GetPlayButtonState();
                if (state == wanted) return true;

                // 再生ボタンごと消えたらEditorが落ちている。タイムアウトまで粘らず抜ける
                if (state == PlayButtonState.Missing)
                {
                    if (++missing >= 3) return false;
                }
                else
                {
                    missing = 0;
                }

                if (sw.ElapsedMilliseconds >= timeoutMs) return false;
                Thread.Sleep(pollMs);
            }
        }

        // ---------------------------------------------------------------
        // 文の分割
        // ---------------------------------------------------------------

        /// <summary>
        /// Editorが1文として扱う単位に分割する。区切り文字は文の側に残す。
        /// 空文字列は「空の1文」として1要素を返す(呼び出し側の分岐を増やさないため)。
        ///
        /// 再生ボタンは「現在の文」しか読まないので、句読点を残したまま全文を
        /// 読ませるには繰り返しが要る。かつてはEditorに全文を入れて「次の文」で
        /// 送っていたが、この方法はEditorを壊す。Editorは「現在の文」の位置を
        /// テキストの差し替えをまたいで保持する一方、位置を読み出す手段が無いため、
        /// 差し替えのたびに「前の文」を多めに押して先頭へ戻すしかなかった。
        /// その余分な押下が先頭を通り越すと、Editorが範囲外アクセスで
        /// エラー表示に崩れて以降の操作を一切受け付けなくなる
        /// (実測では普通の3文を2回読ませただけで再現した)。
        ///
        /// そこで位置を動かす操作そのものをやめた。常に1文だけを入力欄へ入れれば
        /// 位置は先頭から動かず、「前の文」「次の文」を一度も押す必要がない。
        /// 巻き戻しが要らないぶん速くもなった(同じ3文で約11.9秒→約8.2秒)。
        /// 詳細はdocs/findings-2026-07-29.md §20。
        ///
        /// 残る穴として、利用者がEditor上で「次の文」を押したり入力欄をクリックしたり
        /// すると位置は動く。位置は読めず、戻す操作は戻し過ぎると壊すので、
        /// こちら側では防げない。自動読み上げ中はEditorを触らない運用でお願いする。
        /// </summary>
        public static IList<string> SplitSentences(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                result.Add("");
                return result;
            }

            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (Array.IndexOf(SentenceTerminators, text[i]) < 0) continue;
                result.Add(text.Substring(start, i - start + 1));
                start = i + 1;
            }
            // 区切り文字で終わっていない末尾も1文として扱われる
            if (start < text.Length) result.Add(text.Substring(start));

            if (result.Count == 0) result.Add("");
            return result;
        }

        /// <summary>PlayAllSentences()が最後まで行けなかった理由。</summary>
        public enum PlayAllResult
        {
            Ok,
            /// <summary>セリフ入力欄に文を反映できなかった。</summary>
            TextFailed,
            /// <summary>再生を開始できなかった、または読み上げの終了を確認できなかった。</summary>
            PlayFailed,
            /// <summary>全体の締切に掛かった。</summary>
            Timeout,
        }

        /// <summary>
        /// 句読点を残したまま全文を読ませる。1文ずつ入力欄へ入れては再生し、
        /// 終わるまで待って次の文へ進む。
        ///
        /// Editorに渡すのは常に1文だけで、「前の文」「次の文」は一度も押さない。
        /// なぜそうするのかはSplitSentences()を参照。
        ///
        /// 読み上げるものが無い文(区切り文字だけになった文など)は再生を押さずに飛ばす。
        /// 押すとEditorが落ちるため。判定はHasSomethingToSpeak()を参照。
        ///
        /// 1文ごとに「投入＋開始待ち＋読み上げ終了待ち」が入るため、文数に比例して
        /// 時間がかかる。区切り文字はいくつでも含められるので、文数だけでは上限に
        /// ならない。totalTimeoutMsがこの呼び出し全体の締切で、個々の待ちには
        /// 「1回ぶんの上限」と「締切までの残り」の短い方しか渡さない。
        /// 最後の1文だけ青天井になると締切の意味がなくなるため。
        ///
        /// 締切は待ちに入る前に確認するので、待っている最中に来た締切には
        /// その待ちが終わってから気づく。ポーリング間隔(20ms)ぶんは超えうる。
        ///
        /// 途中で抜けても、入力欄に半端な文が残るだけでEditorは壊れない。
        /// 位置を動かしていないので、次の要求はそのまま処理できる。
        /// </summary>
        public PlayAllResult PlayAllSentences(string text, int textTimeoutMs, int extraKeys,
            int startTimeoutMs, int playTimeoutMs, int totalTimeoutMs)
        {
            var elapsed = Stopwatch.StartNew();

            foreach (var sentence in SplitSentences(text))
            {
                if (totalTimeoutMs - (int)elapsed.ElapsedMilliseconds <= 0) return PlayAllResult.Timeout;

                // 「本当！？」は「本当！」と「？」に割れる。読むものが無い文は投入もせず飛ばす。
                // 飛ばしても音は変わらない(Editor自身も「本当！」までしか読まない)
                if (!HasSomethingToSpeak(sentence)) continue;

                // SetTextも締切に掛かればfalseを返すので、StartPlayと同じく理由を見分ける
                if (!SetText(sentence, elapsed, textTimeoutMs, totalTimeoutMs, extraKeys))
                {
                    return totalTimeoutMs - (int)elapsed.ElapsedMilliseconds <= 0
                        ? PlayAllResult.Timeout : PlayAllResult.TextFailed;
                }

                // StartPlayは締切に掛かった場合も押せなかった場合もfalseなので、
                // 締切の方が先に来ていたならそちらを理由にする
                if (!StartPlay(elapsed, startTimeoutMs, totalTimeoutMs))
                {
                    return totalTimeoutMs - (int)elapsed.ElapsedMilliseconds <= 0
                        ? PlayAllResult.Timeout : PlayAllResult.PlayFailed;
                }

                var remaining = totalTimeoutMs - (int)elapsed.ElapsedMilliseconds;
                if (remaining <= 0) return PlayAllResult.Timeout;
                if (!WaitUntilIdle(Math.Min(playTimeoutMs, remaining)))
                {
                    return totalTimeoutMs - (int)elapsed.ElapsedMilliseconds <= 0
                        ? PlayAllResult.Timeout : PlayAllResult.PlayFailed;
                }
            }
            return PlayAllResult.Ok;
        }

        // ---------------------------------------------------------------
        // キャラクター(プリセット)の切り替え
        // ---------------------------------------------------------------

        /// <summary>
        /// メニューを1段開いてから項目が現れるまでの待ち(ms)。
        /// accDoDefaultActionが成功した直後はまだツリーに反映されていない。
        /// </summary>
        const int MenuSettleMs = 400;

        /// <summary>
        /// メニューが割り当てられるキャラクターの上限。
        /// 「1番目のキャラクター」〜「10番目のキャラクター」の10項目しかない。
        /// </summary>
        const int MaxAssignableCharacters = 10;

        /// <summary>MSAAの名前は複数行で返るので「A / B」の1行に均す。区切りは表示に合わせた。</summary>
        const string NameSeparator = " / ";

        static string Flatten(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", NameSeparator);
        }

        /// <summary>「キャラクター名 / プリセット名」からキャラクター名だけを取り出す。</summary>
        static string FirstSegment(string flattened)
        {
            if (string.IsNullOrEmpty(flattened)) return flattened ?? "";
            int end = flattened.IndexOf(NameSeparator, StringComparison.Ordinal);
            return end < 0 ? flattened : flattened.Substring(0, end);
        }

        /// <summary>
        /// 左のキャラクター一覧を、上から順に「キャラクター名 / プリセット名」で返す。
        /// ここで返した文字列はそのまま SetVoicePreset に渡せる。
        /// A.I.VOICE 1.xの TtsControl.VoicePresetNames に相当する。
        /// </summary>
        public List<string> GetVoicePresetNames()
        {
            var names = new List<string>();

            var view = GetViewHandle();
            if (view == IntPtr.Zero) return names;

            // Dumpは先行順なので、グループ「キャラクターチューニング」の直後に来る
            // グラフィックがその行の表示名になる
            bool inTuningRow = false;
            foreach (var node in Aiv2Native.Dump(view, MaxTreeDepth))
            {
                if (node.Role == Aiv2Native.RoleGrouping && node.Name == NameGroupTuning)
                {
                    inTuningRow = true;
                }
                else if (inTuningRow && node.Role == Aiv2Native.RoleGraphic)
                {
                    names.Add(Flatten(node.Name));
                    inTuningRow = false;
                }
            }
            return names;
        }

        /// <summary>
        /// 今テキストブロックに割り当たっているキャラクター名。取得できない場合はnull。
        /// ブロック行のグラフィックがセリフ入力欄の親なので、先行順では直前に現れる。
        /// </summary>
        public string GetCurrentVoicePresetName()
        {
            var view = GetViewHandle();
            if (view == IntPtr.Zero) return null;

            string lastGraphicName = null;
            foreach (var node in Aiv2Native.Dump(view, MaxTreeDepth))
            {
                if (node.Role == Aiv2Native.RoleGraphic)
                {
                    lastGraphicName = node.Name;
                }
                else if (node.Role == Aiv2Native.RoleEditText && string.IsNullOrEmpty(node.Name))
                {
                    return lastGraphicName == null ? null : Flatten(lastGraphicName);
                }
            }
            return null;
        }

        /// <summary>
        /// テキストブロックに割り当てるキャラクターを切り替える。
        ///
        /// ショートカット(Ctrl+1〜Ctrl+0 / Ctrl+Q)はPostMessageでは効かない。
        /// 投げたメッセージでは修飾キーの状態が更新されないため、Editor側はCtrlが
        /// 押されていないものとして扱う(実機で確認)。よってメニューをたどるしかない。
        ///   [キャラクター] → [テキストブロックに割り当て] → [N番目のキャラクター]
        /// どれもaccDoDefaultActionなのでフォアグラウンドは奪わない。
        /// </summary>
        /// <param name="presetName">
        /// 一覧の表示名そのもの、またはその「キャラクター名」部分のどちらでもよい。
        /// </param>
        public bool SetVoicePreset(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return false;

            lock (sync)
            {
                var names = GetVoicePresetNames();
                int index = IndexOfPreset(names, presetName);
                if (index < 0 || index >= MaxAssignableCharacters) return false;

                // 既に割り当たっているなら触らない。メニューを3段たどると2秒近くかかるうえ、
                // 棒読みちゃんは1行ごとにpresetを送ってくる
                if (GetCurrentVoicePresetName() == FirstSegment(names[index])) return true;

                if (!InvokeMenuItem(NameMenuCharacter)) return false;

                if (!InvokeMenuItem(NameMenuAssign) ||
                    !InvokeMenuItem((index + 1) + NameMenuNthCharacterSuffix))
                {
                    // 開いたままだと以降の操作を全部食われるので必ず閉じる
                    CloseMenu();
                    return false;
                }
                return true;
            }
        }

        /// <summary>一覧の何番目かを返す。見つからなければ-1。</summary>
        static int IndexOfPreset(List<string> names, string wanted)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (MatchesPreset(names[i], wanted)) return i;
            }
            return -1;
        }

        /// <summary>
        /// 「キャラクター名 / プリセット名」に対する照合。
        /// 全体一致のほか、キャラクター名だけの指定も受け付ける。
        /// URLに載せる都合で区切りの空白は落とされうるので、そこも吸収する。
        /// </summary>
        static bool MatchesPreset(string displayName, string wanted)
        {
            if (displayName == null || wanted == null) return false;
            if (displayName == wanted) return true;
            if (FirstSegment(displayName) == wanted) return true;
            return displayName.Replace(NameSeparator, "/") == wanted;
        }

        /// <summary>
        /// メニュー項目を名前で引き当てて押す。
        /// 項目名は「1番目のキャラクター / Ctrl+1」のようにショートカットが続くので、
        /// 先頭部分だけで照合する。
        /// </summary>
        bool InvokeMenuItem(string itemName)
        {
            var view = GetViewHandle();
            if (view == IntPtr.Zero) return false;

            foreach (var node in Aiv2Native.Dump(view, MaxTreeDepth))
            {
                if (node.Role != Aiv2Native.RolePushButton) continue;
                if (FirstSegment(Flatten(node.Name)) != itemName) continue;

                if (!Aiv2Native.DoDefaultAction(node.Acc)) return false;
                Thread.Sleep(MenuSettleMs);
                return true;
            }
            return false;
        }

        /// <summary>開いてしまったメニューをEscapeで閉じる。</summary>
        void CloseMenu()
        {
            var view = GetViewHandle();
            if (view == IntPtr.Zero) return;

            // 階層ぶん押す。閉じ切っていてもEscapeは無害
            Aiv2Native.PostKey(view, Aiv2Native.VK_ESCAPE, 3);
            Thread.Sleep(MenuSettleMs);
        }

        // ---------------------------------------------------------------
        // 書き出しダイアログ(ネイティブの #32770)
        // ---------------------------------------------------------------

        /// <summary>
        /// 書き出しボタンを押し、ダイアログが出るまで待つ。
        /// 押下が取りこぼされることがあるので1度だけ押し直す。
        /// </summary>
        public IntPtr StartWrite(int timeoutMs)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!InvokeWrite()) return IntPtr.Zero;

                // 1回目は短めに見切って押し直す
                var wait = attempt == 0 ? Math.Min(timeoutMs, 4000) : timeoutMs;
                var dialog = WaitForSaveDialog(wait);
                if (dialog != IntPtr.Zero) return dialog;
            }
            return IntPtr.Zero;
        }

        /// <summary>書き出しダイアログが出るまで待ち、そのハンドルを返す。出なければIntPtr.Zero。</summary>
        public IntPtr WaitForSaveDialog(int timeoutMs)
        {
            var p = GetProcess();
            if (p == null) return IntPtr.Zero;

            var sw = Stopwatch.StartNew();
            while (true)
            {
                var dlg = Aiv2Native.FindTopLevelByClass(p.Id, DialogClass);
                if (dlg != IntPtr.Zero && Aiv2Native.GetDlgItem(dlg, IDOK) != IntPtr.Zero)
                {
                    return dlg;
                }
                if (sw.ElapsedMilliseconds >= timeoutMs) return IntPtr.Zero;
                Thread.Sleep(50);
            }
        }

        /// <summary>ダイアログにあらかじめ入っているファイル名。ログ用。</summary>
        public string GetSaveDialogFileName(IntPtr dialog)
        {
            var edit = FindSaveDialogFileNameEdit(dialog);
            if (edit == IntPtr.Zero) return "";
            return Aiv2Native.GetWindowTextByMessage(edit);
        }

        /// <summary>
        /// 保存先をダイアログのファイル名欄へ書き込む。フルパスを入れれば
        /// 共通ダイアログがそのまま解決してくれるので、フォルダを開き直す必要はない。
        /// 書き込んだ内容が読み戻せることまで確認する。
        /// </summary>
        public bool SetSaveDialogFileName(IntPtr dialog, string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            var edit = FindSaveDialogFileNameEdit(dialog);
            if (edit == IntPtr.Zero) return false;

            if (!Aiv2Native.SetWindowTextByMessage(edit, path)) return false;
            return Aiv2Native.GetWindowTextByMessage(edit) == path;
        }

        /// <summary>
        /// ファイル名欄(ctrl id 1001)。ComboBoxEx32配下の孫なのでGetDlgItemでは取れず、
        /// 再帰で探す必要がある。コントロールIDはロケール非依存で安定している。
        /// </summary>
        IntPtr FindSaveDialogFileNameEdit(IntPtr dialog)
        {
            return Aiv2Native.FindDescendantByCtrlId(dialog, IDC_FILENAME_EDIT, "Edit");
        }

        /// <summary>
        /// 保存(IDOK)を押してダイアログが閉じるまで待つ。
        /// </summary>
        /// <param name="allowOverwrite">
        /// 同名ファイルがあると共通ダイアログが「名前を付けて保存の確認」を挟む。
        /// 保存先に既にファイルがあることを呼び出し側が確認しているときだけtrueにする。
        /// falseのまま確認が出てきたら、それは想定していない別のダイアログなので
        /// 肯定せずに閉じ、保存自体を諦める(false を返す)。
        /// </param>
        public bool AcceptSaveDialog(IntPtr dialog, int timeoutMs, bool allowOverwrite)
        {
            var ok = Aiv2Native.GetDlgItem(dialog, IDOK);
            if (ok == IntPtr.Zero) return false;
            Aiv2Native.PostClick(ok);

            var sw = Stopwatch.StartNew();
            while (Aiv2Native.IsWindow(dialog) && Aiv2Native.IsWindowVisible(dialog))
            {
                var prompt = Aiv2Native.FindOwnedByClass(dialog, DialogClass);
                if (prompt != IntPtr.Zero && !(allowOverwrite && ConfirmOverwrite(prompt)))
                {
                    // 何のダイアログか分からないものを肯定しない。WM_CLOSEはTaskDialogでは
                    // 否定(キャンセル)なので、押し間違いにならない
                    Aiv2Native.PostClose(prompt);
                    WaitForDialogClosed(prompt, timeoutMs);
                    return false;
                }

                if (sw.ElapsedMilliseconds >= timeoutMs) return false;
                Thread.Sleep(50);
            }
            return true;
        }

        /// <summary>
        /// 上書き確認の「はい」を押す。押せたらtrue。
        ///
        /// この確認はTaskDialogなので、ボタンは実HWND(class Button)なのに
        /// GetDlgCtrlIDが0を返し、GetDlgItemでは引けない。
        /// WM_COMMAND(IDYES)もTDM_CLICK_BUTTONも効かない(実機確認済み)ため、
        /// Buttonの子孫を直接BM_CLICKする。
        /// TaskDialogのボタンは[はい][いいえ]の順に作られるので先頭が「はい」。
        ///
        /// 別種のダイアログを取り違えて肯定しないよう、ボタンがちょうど2つのときだけ押す。
        /// ただしボタンの数は種類の判別にはならないので、これだけに頼らず、
        /// 呼び出し側で「上書きになるはず」と分かっているときにしか呼ばないこと。
        /// 押下手段をこれ一本に絞っているのは、ボタンIDを指定する経路を残すと
        /// 数を数える前に別のダイアログを肯定してしまうため。
        /// </summary>
        bool ConfirmOverwrite(IntPtr prompt)
        {
            var buttons = Aiv2Native.FindChildrenByClass(prompt, ButtonClass);
            if (buttons.Count != 2) return false;

            Aiv2Native.PostClick(buttons[0]);
            Thread.Sleep(PromptSettleMs);
            return true;
        }

        /// <summary>キャンセル(IDCANCEL)を押す。押せなければWM_CLOSEで閉じる。</summary>
        public bool CancelSaveDialog(IntPtr dialog, int timeoutMs)
        {
            var cancel = Aiv2Native.GetDlgItem(dialog, IDCANCEL);
            if (cancel != IntPtr.Zero) Aiv2Native.PostClick(cancel);
            else Aiv2Native.PostClose(dialog);
            return WaitForDialogClosed(dialog, timeoutMs);
        }

        bool WaitForDialogClosed(IntPtr dialog, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (Aiv2Native.IsWindow(dialog) && Aiv2Native.IsWindowVisible(dialog))
            {
                if (sw.ElapsedMilliseconds >= timeoutMs) return false;
                Thread.Sleep(50);
            }
            return true;
        }
    }
}
