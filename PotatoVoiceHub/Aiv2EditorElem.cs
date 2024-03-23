using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace PotatoVoiceHub
{
    public class Aiv2EditorElem
    {
        static readonly Regex regexPlay = new Regex("再生|停止");
        static readonly Regex regexWrite1 = new Regex("書き出し");

        public Process GetProcess()
        {
            foreach (Process p in Process.GetProcesses())
            {
                if (p.MainWindowTitle.Contains("A.I.VOICE2 Editor") && p.ProcessName == "aivoice")
                {
                    return p;
                }
            }

            return null;
        }

        AutomationElement GetElemMainWindow()
        {
            return AutomationElement.FromHandle(GetProcess().MainWindowHandle);
        }
        AutomationElement GetElemAiv2Play()
        {
            foreach (var elem in GetElemMainWindow().FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>())
            {
                if (regexPlay.IsMatch(elem.Current.Name))
                {
                    return elem;
                }
            }
            return null;
        }
        AutomationElement GetElemAiv2Write1()
        {
            foreach (var elem in GetElemMainWindow().FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>())
            {
                if (regexWrite1.IsMatch(elem.Current.Name))
                {
                    return elem;
                }
            }
            return null;
        }
        public AutomationElement GetElemAiv2Write2()
        {
            var elemWrite2List = GetElemMainWindow().FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "書き出しを実行")).Cast<AutomationElement>();
            if (elemWrite2List.Count() > 0)
            {
                return elemWrite2List.First();
            }
            return null;
        }
        public InvokePattern GetInvokeAiv2Write2()
        {
            return GetElemAiv2Write2().GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
        }
        public IntPtr GetHandle()
        {
            try
            {
                return GetProcess().Handle;

            }
            catch (System.Exception)
            {
                return IntPtr.Zero;
            }
        }
        public void SetFocusMainWindow()
        {
            try
            {
                GetElemMainWindow().SetFocus();

            }
            catch (System.Exception)
            {
            }
        }
        public bool IsEnabledPlay()
        {
            try
            {
                var elem = GetElemAiv2Play();
                return elem != null && elem.Current.Name == "再生" && elem.Current.IsEnabled;

            }
            catch (System.Exception)
            {
                return false;
            }
        }
        public void InvokeWrite1()
        {
            try
            {
                ((InvokePattern)GetElemAiv2Write1().GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            }
            catch (System.Exception)
            {
            }
        }
        public void InvokeWrite2()
        {
            try
            {
                GetInvokeAiv2Write2().Invoke();
            }
            catch (System.Exception)
            {
            }
        }
        public void InvokePlay()
        {
            try
            {
                ((InvokePattern)GetElemAiv2Play().GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            }
            catch (System.Exception)
            {
            }
        }
    }
}
