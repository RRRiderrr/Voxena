using System;
using System.Windows.Forms;
using System.Threading.Tasks;
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
            Application.ThreadException += (s, e) =>
            {
                Logger.Write("UI exception: " + e.Exception);
                ShowCrash("A UI error was caught. Voxena will try to continue.", e.Exception);
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Logger.Write("Unobserved task exception: " + e.Exception);
                e.SetObserved();
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex=e.ExceptionObject as Exception;
                Logger.Write("Unhandled exception: " + e.ExceptionObject);
                ShowCrash("Voxena encountered a background error.", ex);
            };
            Application.Run(new MainForm());
        }

        private static void ShowCrash(string title, Exception ex)
        {
            try
            {
                string detail=ex==null?"Unknown error.":ex.Message;
                MessageBox.Show(title+"\n\n"+detail+"\n\nDetails were written to Logs\\voxena.log.","Voxena",MessageBoxButtons.OK,MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
