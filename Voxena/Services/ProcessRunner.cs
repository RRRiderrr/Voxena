using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Voxena.Services
{
    internal sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; }
        public string StdErr { get; set; }
    }

    internal static class ProcessRunner
    {
        // Keep enough tail output for diagnostics without retaining many megabytes of
        // tqdm/pip/HuggingFace progress text in memory during multi-GB model installs.
        private const int MaxCapturedChars = 2 * 1024 * 1024;

        public static async Task<ProcessResult> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken token, Action<string> onLine = null, IDictionary<string,string> environment = null)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stdoutClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stderrClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? "",
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                if (environment != null)
                    foreach (var kv in environment) process.StartInfo.EnvironmentVariables[kv.Key] = kv.Value ?? "";

                process.EnableRaisingEvents = true;
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) { stdoutClosed.TrySetResult(true); return; }
                    try { AppendBounded(stdout,e.Data); SafeNotify(onLine,e.Data); Logger.Write("[process] " + e.Data); }
                    catch(Exception ex){ Logger.Write("stdout callback failure: "+ex); }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) { stderrClosed.TrySetResult(true); return; }
                    try { AppendBounded(stderr,e.Data); SafeNotify(onLine,e.Data); Logger.Write("[process:err] " + e.Data); }
                    catch(Exception ex){ Logger.Write("stderr callback failure: "+ex); }
                };
                process.Exited += (s, e) =>
                {
                    try { exitTcs.TrySetResult(process.ExitCode); }
                    catch(Exception ex){ Logger.Write("Process exit callback failure: "+ex); exitTcs.TrySetException(ex); }
                };

                Logger.Write("Starting: " + fileName + " " + arguments);
                if (!process.Start()) throw new InvalidOperationException("Failed to start process: " + fileName);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (token.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(); }
                    catch(Exception ex){ Logger.Write("Could not terminate cancelled child process: "+ex.Message); }
                }))
                {
                    int code = await exitTcs.Task.ConfigureAwait(false);
                    try { process.WaitForExit(); } catch { }
                    // Give redirected streams a short chance to deliver their final lines.
                    try { await Task.WhenAny(Task.WhenAll(stdoutClosed.Task,stderrClosed.Task),Task.Delay(1500)).ConfigureAwait(false); } catch { }
                    token.ThrowIfCancellationRequested();
                    return new ProcessResult { ExitCode = code, StdOut = stdout.ToString(), StdErr = stderr.ToString() };
                }
            }
        }

        private static void SafeNotify(Action<string> callback,string text)
        {
            if(callback==null)return;
            try{callback(text);}catch(Exception ex){Logger.Write("Process output consumer failed: "+ex);}
        }
        private static void AppendBounded(StringBuilder sb,string line)
        {
            if(line==null)return;
            sb.AppendLine(line);
            if(sb.Length>MaxCapturedChars)
            {
                int remove=sb.Length-(MaxCapturedChars/2);
                if(remove>0)sb.Remove(0,remove);
            }
        }

        public static string Quote(string value)
        {
            if (value == null || value.Length == 0) return "\"\"";
            var sb = new StringBuilder(); sb.Append('"'); int backslashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1); sb.Append('"'); backslashes = 0; continue; }
                if (backslashes > 0) { sb.Append('\\', backslashes); backslashes = 0; }
                sb.Append(c);
            }
            if (backslashes > 0) sb.Append('\\', backslashes * 2);
            sb.Append('"'); return sb.ToString();
        }
    }
}
