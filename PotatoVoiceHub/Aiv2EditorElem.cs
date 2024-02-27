using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace PotatoVoiceHub
{
    public class Aiv2EditorElem
    {
        Process processAiv2;
        AutomationElement elemAiv2MainWindow;
        AutomationElement elemAiv2Play;
        AutomationElement elemAiv2Write1;
        AutomationElement elemAiv2Write2;
        static readonly Regex regexPlay = new Regex("再生|停止");
        static readonly Regex regexWrite1 = new Regex("書き出し");

        public Process GetProcess()
        {
            foreach (Process p in Process.GetProcesses())
            {
                if (p.MainWindowTitle.Contains("A.I.VOICE2 Editor"))
                {
                    processAiv2 = p;
                    return processAiv2;
                }
            }

            processAiv2 = null;
            return processAiv2;
        }

        public AutomationElement GetElemMainWindow()
        {
            if (elemAiv2MainWindow != null && elemAiv2MainWindow.Current.Name != "")
            {
                return elemAiv2MainWindow;
            }
            return elemAiv2MainWindow = AutomationElement.FromHandle(processAiv2.MainWindowHandle);

        }
        public AutomationElement GetElemAiv2Play()
        {
            //ポップアップが開くとElementが触れなくなり、Nameがブランクになるためチェック
            if (elemAiv2Play != null && elemAiv2Play.Current.Name != "")
            {
                return elemAiv2Play;
            }

            AutomationElement elemResult = null;
            foreach (var elem in GetElemMainWindow().FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>())
            {
                if (regexPlay.IsMatch(elem.Current.Name))
                {
                    elemResult = elem;
                }
            }
            return elemAiv2Play = elemResult;
        }
        public InvokePattern GetInvokeAiv2Play()
        {
            return GetElemAiv2Play().GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
        }
        public AutomationElement GetElemAiv2Write1()
        {
            //ポップアップが開くとElementが触れなくなり、Nameがブランクになるためチェック
            if (elemAiv2Write1 != null && elemAiv2Write1.Current.Name != "")
            {
                return elemAiv2Write1;
            }

            AutomationElement elemResult = null;
            foreach (var elem in elemAiv2MainWindow.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>())
            {
                if (regexWrite1.IsMatch(elem.Current.Name))
                {
                    elemResult = elem;
                }
            }
            return elemAiv2Write1 = elemResult;
        }
        public InvokePattern GetInvokeAiv2Write1()
        {
            return GetElemAiv2Write1().GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
        }
        public AutomationElement GetElemAiv2Write2()
        {
            //ポップアップが開くとElementが触れなくなり、Nameがブランクになるためチェック
            if (elemAiv2Write2 != null && elemAiv2Write2.Current.Name != "")
            {
                return elemAiv2Write2;
            }

            var elemWrite2List = GetElemMainWindow().FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "書き出しを実行")).Cast<AutomationElement>();
            if (elemWrite2List.Count() > 0)
            {
                elemAiv2Write2 = elemWrite2List.First();
            }
            else
            {
                elemAiv2Write2 = null;
            }
            return elemAiv2Write2;
        }
        public InvokePattern GetInvokeAiv2Write2()
        {
            return GetElemAiv2Write2().GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
        }
    }
}
