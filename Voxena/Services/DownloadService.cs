using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Voxena.Models;

namespace Voxena.Services
{
    internal sealed class DownloadService : IDisposable
    {
        private readonly HttpClient _client;
        public DownloadService()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromHours(24) };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("Voxena/0.3.2");
        }

        public async Task DownloadAsync(DownloadItem item, string destination, string stage, IProgress<DownloadProgress> progress, CancellationToken token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string part = destination + ".part";
            long existing = File.Exists(part) ? new FileInfo(part).Length : 0L;
            using (var request = new HttpRequestMessage(HttpMethod.Get, item.Url))
            {
                if (existing > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
                using (var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if (existing > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                    {
                        existing = 0; try { File.Delete(part); } catch { }
                    }
                    response.EnsureSuccessStatusCode();
                    long bodyLength = response.Content.Headers.ContentLength ?? 0L;
                    long total = bodyLength > 0 ? bodyLength + existing : item.ExpectedBytes;
                    var sw = Stopwatch.StartNew(); long received = existing; long checkpointBytes = received; long checkpointMs = 0; double speed = 0;
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(part, FileMode.Append, FileAccess.Write, FileShare.Read, 1024 * 1024, true))
                    {
                        var buffer = new byte[1024 * 1024];
                        while (true)
                        {
                            int read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                            if (read <= 0) break;
                            await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false); received += read;
                            long ms = sw.ElapsedMilliseconds;
                            if (ms - checkpointMs >= 700)
                            {
                                long delta = received - checkpointBytes; long deltaMs = ms - checkpointMs;
                                if (deltaMs > 0) speed = delta * 1000.0 / deltaMs;
                                checkpointBytes = received; checkpointMs = ms;
                                if (progress != null) progress.Report(new DownloadProgress { Stage = stage, FileName = Path.GetFileName(destination), BytesReceived = received, TotalBytes = total, Percent = total > 0 ? received * 100.0 / total : 0, BytesPerSecond = speed });
                            }
                        }
                    }
                }
            }
            if (File.Exists(destination)) File.Delete(destination);
            File.Move(part, destination);
            if (!string.IsNullOrWhiteSpace(item.Sha256))
            {
                string actual = ComputeSha256(destination);
                if (!actual.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(destination); throw new InvalidDataException("SHA-256 mismatch for " + Path.GetFileName(destination)); }
            }
            if (progress != null) progress.Report(new DownloadProgress { Stage = stage, FileName = Path.GetFileName(destination), BytesReceived = new FileInfo(destination).Length, TotalBytes = new FileInfo(destination).Length, Percent = 100 });
        }

        private static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create()) using (var fs = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }
        public void Dispose() { _client.Dispose(); }
    }
}
