using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PotatoVoiceHub
{
    /// <summary>
    /// A.I.VOICE2 Editor 2.14系を操作するための相互運用層。
    ///
    /// 2.14系のEditorはFlutter製で、トップレベルのFLUTTER_RUNNER_WIN32_WINDOWと
    /// 子のFLUTTERVIEWという構成になっている。FLUTTERVIEWはUI Automationの
    /// ネイティブプロバイダを公開しないためUIAツリーは空になるが、MSAA(IAccessible)
    /// には完全なセマンティクスツリーを出す。そのためここではMSAAを使う。
    ///
    /// テキスト投入は公開されたウィンドウメッセージ(WM_CHAR / WM_KEYDOWN)で行う。
    /// いずれもOS公開のアクセシビリティAPIとウィンドウメッセージのみで、
    /// 対象アプリの内部解析は一切行わない。
    /// </summary>
    internal static class Aiv2Native
    {
        // ---------------------------------------------------------------
        // MSAA
        // ---------------------------------------------------------------

        [ComImport, Guid("618736e0-3c3d-11cf-810c-00aa00389b71"),
         InterfaceType(ComInterfaceType.InterfaceIsDual)]
        public interface IAccessible
        {
            // IDispatchの7メソッド分はInterfaceIsDualが面倒を見る
            [return: MarshalAs(UnmanagedType.IDispatch)] object get_accParent();
            int get_accChildCount();
            [return: MarshalAs(UnmanagedType.IDispatch)] object get_accChild([MarshalAs(UnmanagedType.Struct)] object varChild);
            [return: MarshalAs(UnmanagedType.BStr)] string get_accName([MarshalAs(UnmanagedType.Struct)] object varChild);
            [return: MarshalAs(UnmanagedType.BStr)] string get_accValue([MarshalAs(UnmanagedType.Struct)] object varChild);
            [return: MarshalAs(UnmanagedType.BStr)] string get_accDescription([MarshalAs(UnmanagedType.Struct)] object varChild);
            [return: MarshalAs(UnmanagedType.Struct)] object get_accRole([MarshalAs(UnmanagedType.Struct)] object varChild);
            [return: MarshalAs(UnmanagedType.Struct)] object get_accState([MarshalAs(UnmanagedType.Struct)] object varChild);
            [return: MarshalAs(UnmanagedType.BStr)] string get_accHelp([MarshalAs(UnmanagedType.Struct)] object varChild);
            int get_accHelpTopic([MarshalAs(UnmanagedType.BStr)] out string pszHelpFile, [MarshalAs(UnmanagedType.Struct)] object varChild);
            [return: MarshalAs(UnmanagedType.BStr)] string get_accKeyboardShortcut([MarshalAs(UnmanagedType.Struct)] object varChild);
            [return: MarshalAs(UnmanagedType.Struct)] object get_accFocus();
            [return: MarshalAs(UnmanagedType.Struct)] object get_accSelection();
            [return: MarshalAs(UnmanagedType.BStr)] string get_accDefaultAction([MarshalAs(UnmanagedType.Struct)] object varChild);
            void accSelect(int flagsSelect, [MarshalAs(UnmanagedType.Struct)] object varChild);
            void accLocation(out int l, out int t, out int w, out int h, [MarshalAs(UnmanagedType.Struct)] object varChild);
            [return: MarshalAs(UnmanagedType.Struct)] object accNavigate(int navDir, [MarshalAs(UnmanagedType.Struct)] object varStart);
            [return: MarshalAs(UnmanagedType.Struct)] object accHitTest(int x, int y);
            void accDoDefaultAction([MarshalAs(UnmanagedType.Struct)] object varChild);
            void set_accName([MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] string s);
            void set_accValue([MarshalAs(UnmanagedType.Struct)] object varChild, [MarshalAs(UnmanagedType.BStr)] string s);
        }

        [DllImport("oleacc.dll")]
        static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint id, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IAccessible acc);

        [DllImport("oleacc.dll")]
        static extern int AccessibleChildren(IAccessible container, int start, int count,
            [Out] object[] children, out int obtained);

        public const int OBJID_CLIENT = -4;
        public static readonly object ChildIdSelf = (int)0;

        // MSAAのROLE_SYSTEM_*
        // roleの文字列表現はロケールで英語/日本語に振れるので、判定は必ずこの数値で行う。
        public const int RoleGrouping = 20;     // ROLE_SYSTEM_GROUPING
        public const int RoleGraphic = 40;      // ROLE_SYSTEM_GRAPHIC
        public const int RoleEditText = 42;     // ROLE_SYSTEM_TEXT
        public const int RolePushButton = 43;   // ROLE_SYSTEM_PUSHBUTTON

        public const uint StateUnavailable = 0x00000001; // STATE_SYSTEM_UNAVAILABLE

        /// <summary>
        /// MSAAツリーの1ノード。Accは生きている限り再利用でき、毎回の走査を省ける。
        ///
        /// 持たせるのは要素の特定に使うnameとroleだけにしてある。プロパティ1つが
        /// プロセス境界を越えるCOM呼び出しなので、走査のたびに全ノードぶん積み上がる。
        /// valueやstateが要る場面は特定の1要素に対してだけなので、
        /// そのときAccから直接読むこと(GetValue / TryGetState)。
        /// </summary>
        public class Node
        {
            public IAccessible Acc;
            public string Name;
            public int Role;
        }

        public static IAccessible AccessibleFromWindow(IntPtr hwnd)
        {
            var iid = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");
            IAccessible acc;
            // OBJID_CLIENTを要求することでFlutter側がセマンティクスツリーを構築する
            int hr = AccessibleObjectFromWindow(hwnd, unchecked((uint)OBJID_CLIENT), ref iid, out acc);
            if (hr != 0 || acc == null) return null;
            return acc;
        }

        public static string GetName(IAccessible a)
        {
            try { return a.get_accName(ChildIdSelf) ?? ""; }
            catch { return null; }
        }

        public static string GetValue(IAccessible a)
        {
            try { return a.get_accValue(ChildIdSelf) ?? ""; }
            catch { return null; }
        }

        public static int GetRole(IAccessible a)
        {
            try { return Convert.ToInt32(a.get_accRole(ChildIdSelf)); }
            catch { return -1; }
        }

        /// <summary>
        /// stateを取得する。取得できなければfalseを返す。
        ///
        /// 例外時に0(=フラグが何も立っていない)を返すと、呼び出し側からは
        /// 「操作できる状態」と区別がつかない。Editorがツリーを更新している最中などに
        /// 取得だけ失敗することがあるので、失敗を成功と誤読しないようにしておく。
        /// </summary>
        public static bool TryGetState(IAccessible a, out uint state)
        {
            try { state = Convert.ToUInt32(a.get_accState(ChildIdSelf)); return true; }
            catch { state = 0; return false; }
        }

        public static bool DoDefaultAction(IAccessible a)
        {
            try { a.accDoDefaultAction(ChildIdSelf); return true; }
            catch { return false; }
        }

        public const int SelflagTakeFocus = 1; // SELFLAG_TAKEFOCUS

        /// <summary>
        /// ノードへキーボードフォーカスを移す。
        ///
        /// FlutterはMSAAのstateにFOCUSEDを立てたまま内部のtext input clientを
        /// 手放していることがあり、その間はPostMessageしたWM_CHARが握り潰される。
        /// PostMessage自体は成功を返し、ウィンドウをフォアグラウンドに出しても
        /// 直らないので、状態からは見分けがつかない。accSelectを打てば復帰する。
        /// </summary>
        public static bool TakeFocus(IAccessible a)
        {
            try { a.accSelect(SelflagTakeFocus, ChildIdSelf); return true; }
            catch { return false; }
        }

        /// <summary>ウィンドウ配下のMSAAツリーを平坦化して返す。</summary>
        public static List<Node> Dump(IntPtr hwnd, int maxDepth)
        {
            var list = new List<Node>();
            var root = AccessibleFromWindow(hwnd);
            if (root == null) return list;
            Walk(root, 0, maxDepth, list);
            return list;
        }

        static void Walk(IAccessible a, int depth, int maxDepth, List<Node> list)
        {
            var n = new Node { Acc = a };
            n.Name = GetName(a) ?? "";
            n.Role = GetRole(a);
            list.Add(n);

            if (depth >= maxDepth) return;
            foreach (var k in Children(a)) Walk(k, depth + 1, maxDepth, list);
        }

        /// <summary>直接の子IAccessibleを返す。</summary>
        public static List<IAccessible> Children(IAccessible parent)
        {
            var result = new List<IAccessible>();
            int count;
            try { count = parent.get_accChildCount(); }
            catch { return result; }
            if (count <= 0) return result;

            var buf = new object[count];
            int got;
            try
            {
                if (AccessibleChildren(parent, 0, count, buf, out got) != 0) return result;
            }
            catch { return result; }

            for (int i = 0; i < got; i++)
            {
                if (buf[i] == null) continue;
                var child = buf[i] as IAccessible;
                if (child == null)
                {
                    // 単純子(child id)は親から辿り直す
                    try { child = parent.get_accChild(buf[i]) as IAccessible; }
                    catch { }
                }
                if (child != null) result.Add(child);
            }
            return result;
        }

        // ---------------------------------------------------------------
        // Win32
        // ---------------------------------------------------------------

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);
        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll")]
        static extern int GetDlgCtrlID(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
        static extern IntPtr SendMessageTimeoutText(IntPtr hWnd, uint msg, IntPtr wParam,
            StringBuilder lParam, uint flags, uint timeoutMs, out IntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
        static extern IntPtr SendMessageTimeoutString(IntPtr hWnd, uint msg, IntPtr wParam,
            string lParam, uint flags, uint timeoutMs, out IntPtr result);

        public const uint WM_CLOSE = 0x0010;
        public const uint WM_SETTEXT = 0x000C;
        public const uint WM_GETTEXT = 0x000D;
        public const uint WM_KEYDOWN = 0x0100;
        public const uint WM_KEYUP = 0x0101;
        public const uint WM_CHAR = 0x0102;
        public const uint BM_CLICK = 0x00F5;

        public const int VK_BACK = 0x08;
        public const int VK_ESCAPE = 0x1B;
        public const int VK_DELETE = 0x2E;

        const uint SMTO_ABORTIFHUNG = 0x0002;

        /// <summary>指定クラス名の子ウィンドウを再帰的に探す。</summary>
        public static IntPtr FindChildByClass(IntPtr parent, string className)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindowsProc cb = null;
            cb = (h, l) =>
            {
                if (GetClassName(h) == className) { found = h; return false; }
                var deep = FindChildByClass(h, className);
                if (deep != IntPtr.Zero) { found = deep; return false; }
                return true;
            };
            EnumChildWindows(parent, cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return found;
        }

        /// <summary>指定プロセスが持つ、指定クラス名のトップレベルウィンドウを探す。</summary>
        public static IntPtr FindTopLevelByClass(int processId, string className)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindowsProc cb = null;
            cb = (h, l) =>
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid != (uint)processId) return true;
                if (!IsWindowVisible(h)) return true;
                if (GetClassName(h) != className) return true;
                found = h;
                return false;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return found;
        }

        const uint GW_OWNER = 4;

        /// <summary>
        /// 指定ウィンドウが所有するトップレベルウィンドウを、クラス名で探す。
        /// 共通ダイアログが挟むメッセージボックス(上書き確認など)を拾うのに使う。
        /// </summary>
        public static IntPtr FindOwnedByClass(IntPtr owner, string className)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindowsProc cb = null;
            cb = (h, l) =>
            {
                if (GetWindow(h, GW_OWNER) != owner) return true;
                if (!IsWindowVisible(h)) return true;
                if (GetClassName(h) != className) return true;
                found = h;
                return false;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return found;
        }

        /// <summary>指定クラス名の子孫ウィンドウを列挙する(EnumChildWindowsの発見順)。</summary>
        public static List<IntPtr> FindChildrenByClass(IntPtr parent, string className)
        {
            var found = new List<IntPtr>();
            EnumWindowsProc cb = null;
            cb = (h, l) =>
            {
                if (GetClassName(h) == className) found.Add(h);
                return true;
            };
            EnumChildWindows(parent, cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return found;
        }

        /// <summary>ダイアログ配下から、指定コントロールIDかつ指定クラス名の子を再帰的に探す。</summary>
        public static IntPtr FindDescendantByCtrlId(IntPtr parent, int ctrlId, string className)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindowsProc cb = null;
            cb = (h, l) =>
            {
                if (GetDlgCtrlID(h) == ctrlId && GetClassName(h) == className) { found = h; return false; }
                var deep = FindDescendantByCtrlId(h, ctrlId, className);
                if (deep != IntPtr.Zero) { found = deep; return false; }
                return true;
            };
            EnumChildWindows(parent, cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return found;
        }

        public static string GetClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetClassNameW(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>別プロセスの標準コントロールからテキストを読む(WM_GETTEXTはシステムがマーシャルする)。</summary>
        public static string GetWindowTextByMessage(IntPtr hwnd)
        {
            var sb = new StringBuilder(1024);
            IntPtr result;
            if (SendMessageTimeoutText(hwnd, WM_GETTEXT, (IntPtr)sb.Capacity, sb,
                    SMTO_ABORTIFHUNG, 2000, out result) == IntPtr.Zero)
            {
                return "";
            }
            return sb.ToString();
        }

        /// <summary>
        /// 別プロセスの標準コントロールへテキストを書き込む。
        /// WM_SETTEXTもWM_GETTEXTと同じくシステムが文字列をマーシャルしてくれるので、
        /// 相手のプロセスにメモリを確保しなくてよい。PostMessageでは文字列の寿命を
        /// 保証できないため必ずSendMessage側を使うこと。
        /// </summary>
        public static bool SetWindowTextByMessage(IntPtr hwnd, string text)
        {
            IntPtr result;
            return SendMessageTimeoutString(hwnd, WM_SETTEXT, IntPtr.Zero, text ?? "",
                       SMTO_ABORTIFHUNG, 2000, out result) != IntPtr.Zero;
        }

        /// <summary>WM_CHARを1文字ずつ投げる。フォアグラウンドを奪わずに文字を入れられる。</summary>
        public static void PostText(IntPtr hwnd, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (char c in text)
            {
                PostMessageW(hwnd, WM_CHAR, (IntPtr)c, (IntPtr)1);
            }
        }

        /// <summary>仮想キーの押下/解放をcount回投げる。</summary>
        public static void PostKey(IntPtr hwnd, int virtualKey, int count)
        {
            // lParamのbit30(前回状態)とbit31(遷移状態)を立てたWM_KEYUP
            IntPtr up = unchecked((IntPtr)(int)0xC0000001);
            for (int i = 0; i < count; i++)
            {
                PostMessageW(hwnd, WM_KEYDOWN, (IntPtr)virtualKey, (IntPtr)1);
                PostMessageW(hwnd, WM_KEYUP, (IntPtr)virtualKey, up);
            }
        }

        public static void PostClick(IntPtr hwnd)
        {
            PostMessageW(hwnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        }

        public static void PostClose(IntPtr hwnd)
        {
            PostMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
