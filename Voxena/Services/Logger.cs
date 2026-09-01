using System;
using System.IO;
using Voxena.Infrastructure;

namespace Voxena.Services
{
    internal static class Logger
    {
        private static readonly object Gate = new object();
        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(Path.Combine(AppPaths.Logs, "voxena.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
