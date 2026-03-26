using System;
using System.Windows.Forms;
using ReleasePrepTool.UI;

namespace ReleasePrepTool
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}