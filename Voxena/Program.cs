using System;
using System.Windows.Forms;
using Voxena.Infrastructure;
using Voxena.Services;

namespace Voxena
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            AppPaths.EnsureAll();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Force Windows Forms callback exceptions through ThreadException instead
            // of allowing a native user-callback failure to terminate the process.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Logger.Write("UI exception: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Logger.Write("Unhandled exception: " + e.ExceptionObject);
            Application.Run(new MainForm());
        }
    }
}
