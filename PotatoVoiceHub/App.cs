using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace PotatoVoiceHub
{
    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            new MainWindow().ShowDialog();
        }
    }
}
