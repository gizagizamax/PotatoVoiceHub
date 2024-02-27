using System;
using System.Runtime.InteropServices;

namespace PotatoVoiceHub
{
    public class Win32Api
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
