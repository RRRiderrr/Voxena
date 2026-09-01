using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Voxena.Infrastructure;
using Voxena.Models;

namespace Voxena.Services
{
    internal sealed class RuntimeBootstrapService
    {
        // Bump these whenever package/source bootstrap logic changes. Existing model
        // weights stay in place; only the lightweight isolated runtime is refreshed.
        private const string RuntimeSchema = "voxena-runtime-0.3.3-r1";
        private const string SourceSchema = "voxena-source-0.3.1-r2";
        private const string StressSchema = "voxena-stress-0.3.1-r2";

        private readonly DownloadService _downloads;
        private readonly string _toolsDir = Path.Combine(AppPaths.Runtime, "Tools");
        private readonly string _uvDir = Path.Combine(AppPaths.Runtime, "Tools", "uv");
        private readonly string _ffmpegDir = Path.Combine(AppPaths.Runtime, "Tools", "ffmpeg");
        private readonly string _stressDir = AppPaths.EngineDirectory("_stress");
        private readonly string _stressModelDir = AppPaths.ModelDirectory("_stress_gemma4");
        private readonly HashSet<string> _sessionValidated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public RuntimeBootstrapService(DownloadService downloads) { _downloads = downloads; }
        public string StressModelDirectory { get { return _stressModelDir; } }
        public bool StressReady
        {
            get
            {
                string ready=Path.Combine(_stressModelDir,".ready");
                return File.Exists(ready) && SafeRead(ready)==StressSchema && File.Exists(GetPython(_stressDir));
            }
        }

        public async Task<string> EnsureUvAsync(IProgress<DownloadProgress> progress, CancellationToken token)
        {
            string exe = Path.Combine(_uvDir, "uv.exe");
            if (File.Exists(exe)) return exe;
            Directory.CreateDirectory(_toolsDir);
            string zip = Path.Combine(AppPaths.Temp, "uv-win64.zip");
            await _downloads.DownloadAsync(new DownloadItem { Url = "https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip", RelativePath = "uv.zip" }, zip, "Runtime · uv", progress, token).ConfigureAwait(false);
            ResetDirectory(_uvDir); ZipFile.ExtractToDirectory(zip, _uvDir); TryDelete(zip);
            exe = Directory.GetFiles(_uvDir, "uv.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(exe)) throw new FileNotFoundException("uv.exe was not found after extracting the runtime package.");
            string final = Path.Combine(_uvDir, "uv.exe");
            if (!exe.Equals(final, StringComparison.OrdinalIgnoreCase)) File.Copy(exe, final, true);
            return final;
        }

        public async Task<string> EnsureFfmpegAsync(IProgress<DownloadProgress> progress, CancellationToken token)
        {
            string exe = Path.Combine(_ffmpegDir, "ffmpeg.exe");
            if (File.Exists(exe)) return exe;
            string zip = Path.Combine(AppPaths.Temp, "ffmpeg-essentials.zip");
            await _downloads.DownloadAsync(new DownloadItem { Url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip", RelativePath = "ffmpeg.zip" }, zip, "Runtime · FFmpeg", progress, token).ConfigureAwait(false);
            ResetDirectory(_ffmpegDir); ZipFile.ExtractToDirectory(zip, _ffmpegDir); TryDelete(zip);
            string found = Directory.GetFiles(_ffmpegDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(found)) throw new FileNotFoundException("ffmpeg.exe was not found after extracting the runtime package.");
            File.Copy(found, exe, true); return exe;
        }

        public async Task EnsureEngineAsync(ModelProfile profile, IProgress<DownloadProgress> progress, CancellationToken token, Action<string> onLine)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            string env = AppPaths.EngineDirectory(profile.Id);
            string python = GetPython(env);
            string ready = Path.Combine(env, ".packages-ready");
            string doctorReady = Path.Combine(env, ".doctor-ready");
            string expected = RuntimeFingerprint(profile);
            if (File.Exists(python) && File.Exists(ready) && File.Exists(doctorReady) &&
                string.Equals(SafeRead(ready),expected,StringComparison.Ordinal) &&
                string.Equals(SafeRead(doctorReady),expected,StringComparison.Ordinal))
            {
                await EnsureSourceAsync(profile, progress, token, onLine).ConfigureAwait(false);
                // A ready marker proves that this environment once passed validation, but a
                // manually updated/corrupted package can still invalidate it later. Run one
                // lightweight import doctor per engine per Voxena session before first use.
                if (!_sessionValidated.Contains(profile.Id))
                {
                    string uvExisting = await EnsureUvAsync(progress, token).ConfigureAwait(false);
                    await ValidateEngineRuntimeAsync(profile, uvExisting, env, token, onLine).ConfigureAwait(false);
                    File.WriteAllText(doctorReady, expected);
                    _sessionValidated.Add(profile.Id);
                }
                return;
            }

            string uv = await EnsureUvAsync(progress, token).ConfigureAwait(false);
            Directory.CreateDirectory(env);
            await RunChecked(uv, "python install " + ProcessRunner.Quote(profile.PythonVersion), AppPaths.Runtime, token, onLine).ConfigureAwait(false);
            if (!File.Exists(python))
                await RunChecked(uv, "venv --python " + ProcessRunner.Quote(profile.PythonVersion) + " " + ProcessRunner.Quote(env), AppPaths.Runtime, token, onLine).ConfigureAwait(false);
            python = GetPython(env);
            if (!File.Exists(python)) throw new FileNotFoundException("The managed Python environment was not created for " + profile.Name + ".");

            await InstallPackagesAsync(profile, uv, env, progress, token, onLine).ConfigureAwait(false);
            await EnsureSourceAsync(profile, progress, token, onLine).ConfigureAwait(false);
            await ValidateEngineRuntimeAsync(profile, uv, env, token, onLine).ConfigureAwait(false);
            File.WriteAllText(ready, expected);
            File.WriteAllText(doctorReady, expected);
            _sessionValidated.Add(profile.Id);
        }

        private async Task InstallPackagesAsync(ModelProfile profile, string uv, string env, IProgress<DownloadProgress> progress, CancellationToken token, Action<string> onLine)
        {
            string p = "pip install --python " + ProcessRunner.Quote(GetPython(env)) + " ";
            if (profile.Id == "cosyvoice3")
            {
                await RunChecked(uv, p + "torch==2.3.1 torchaudio==2.3.1 --index-url https://download.pytorch.org/whl/cu121", AppPaths.Runtime, token, onLine).ConfigureAwait(false);
                await RunChecked(uv, p + JoinPackages(profile.Packages), AppPaths.Runtime, token, onLine).ConfigureAwait(false);
            }
            else if (profile.Id == "fish-s2-pro")
            {
                // Fish S2 pins torch/torchaudio 2.8.0. Install CUDA wheels directly,
                // then the inference-only dependencies (no PyAudio/WebUI dependency),
                // and finally the downloaded source without requiring Git on end-user PCs.
                var torch=await ProcessRunner.RunAsync(uv,p+"torch==2.8.0 torchaudio==2.8.0 --index-url https://download.pytorch.org/whl/cu128",AppPaths.Runtime,token,onLine).ConfigureAwait(false);
                if(torch.ExitCode!=0) await RunChecked(uv,p+"torch==2.8.0 torchaudio==2.8.0",AppPaths.Runtime,token,onLine).ConfigureAwait(false);
                if(profile.Packages!=null&&profile.Packages.Count>0)
                    await RunChecked(uv,p+JoinPackages(profile.Packages),AppPaths.Runtime,token,onLine).ConfigureAwait(false);
                string source=await EnsureSourceAsync(profile,progress,token,onLine).ConfigureAwait(false);
                await RunChecked(uv,p+"-e "+ProcessRunner.Quote(source)+" --no-deps",AppPaths.Runtime,token,onLine).ConfigureAwait(false);
                // descript-audiotools has an obsolete protobuf cap; Fish S2's generated
                // schema needs a modern protobuf. Mirror upstream's uv override.
                await RunChecked(uv,p+"protobuf>=4.25,<6 --no-deps",AppPaths.Runtime,token,onLine).ConfigureAwait(false);
            }
            else if (profile.Id == "chatterbox-v3")
            {
                // Current V3 support exists in the upstream source tree while its
                // published package version number still matches an older upload.
                // Download the source ZIP ourselves so users never need Git.
                var torch=await ProcessRunner.RunAsync(uv,p+"torch==2.6.0 torchaudio==2.6.0 --index-url https://download.pytorch.org/whl/cu124",AppPaths.Runtime,token,onLine).ConfigureAwait(false);
                if(torch.ExitCode!=0) await RunChecked(uv,p+"torch==2.6.0 torchaudio==2.6.0",AppPaths.Runtime,token,onLine).ConfigureAwait(false);
                await RunChecked(uv,p+JoinPackages(profile.Packages),AppPaths.Runtime,token,onLine).ConfigureAwait(false);
                string source=await EnsureSourceAsync(profile,progress,token,onLine).ConfigureAwait(false);
                await RunChecked(uv,p+"-e "+ProcessRunner.Quote(source)+" --no-deps",AppPaths.Runtime,token,onLine).ConfigureAwait(false);
            }
            else if (profile.Id == "xtts-v2" || profile.Id == "f5-tts-ru" || profile.Id == "qwen3-tts")
            {
                // Pin the shared PyTorch base as well: letting this float to a future
                // major release can break otherwise stable TTS packages months later.
                var torch = await ProcessRunner.RunAsync(uv, p + "torch==2.8.0 torchaudio==2.8.0 --index-url https://download.pytorch.org/whl/cu128", AppPaths.Runtime, token, onLine).ConfigureAwait(false);
                if (torch.ExitCode != 0) await RunChecked(uv, p + "torch==2.8.0 torchaudio==2.8.0", AppPaths.Runtime, token, onLine).ConfigureAwait(false);
                if (profile.Packages != null && profile.Packages.Count > 0)
                    await RunChecked(uv, p + JoinPackages(profile.Packages), AppPaths.Runtime, token, onLine).ConfigureAwait(false);
            }
            else
            {
                var torch = await ProcessRunner.RunAsync(uv, p + "torch torchaudio --index-url https://download.pytorch.org/whl/cu128", AppPaths.Runtime, token, onLine).ConfigureAwait(false);
                if (torch.ExitCode != 0) await RunChecked(uv, p + "torch torchaudio", AppPaths.Runtime, token, onLine).ConfigureAwait(false);
                if (profile.Packages != null && profile.Packages.Count > 0)
                    await RunChecked(uv, p + JoinPackages(profile.Packages), AppPaths.Runtime, token, onLine).ConfigureAwait(false);
            }
        }

        private async Task ValidateEngineRuntimeAsync(ModelProfile profile, string uv, string env, CancellationToken token, Action<string> onLine)
        {
            var first = await RunDoctorAsync(profile, env, token, onLine).ConfigureAwait(false);
            if (first.ExitCode == 0) return;

            if (onLine != null) onLine("Compatibility check failed; repairing pinned dependencies for " + profile.Name + "…");
            await ApplyCompatibilityRepairAsync(profile, uv, env, token, onLine).ConfigureAwait(false);
            var second = await RunDoctorAsync(profile, env, token, onLine).ConfigureAwait(false);
            if (second.ExitCode != 0)
                throw BuildProcessError("Runtime compatibility check failed for " + profile.Name + ".", second);
        }

        private async Task<ProcessResult> RunDoctorAsync(ModelProfile profile, string env, CancellationToken token, Action<string> onLine)
        {
            string source = (profile.Id == "cosyvoice3" || profile.Id == "fish-s2-pro" || profile.Id == "chatterbox-v3") ? Path.Combine(AppPaths.RuntimeSources, profile.Id) : "";
            string args = ProcessRunner.Quote(Path.Combine(AppPaths.RuntimeScripts, "engine_host.py")) +
                          " doctor --model-id " + ProcessRunner.Quote(profile.Id) +
                          " --model-dir " + ProcessRunner.Quote(AppPaths.ModelDirectory(profile.Id)) +
                          " --source-dir " + ProcessRunner.Quote(source) + " --device cpu";
            return await ProcessRunner.RunAsync(GetPython(env), args, AppPaths.RuntimeScripts, token, onLine, BuildEnvironment(AppPaths.ModelDirectory(profile.Id))).ConfigureAwait(false);
        }

        private async Task ApplyCompatibilityRepairAsync(ModelProfile profile, string uv, string env, CancellationToken token, Action<string> onLine)
        {
            string p = "pip install --python " + ProcessRunner.Quote(GetPython(env)) + " --upgrade ";
            string packages;
            switch (profile.Id)
            {
                case "xtts-v2":
                    packages = "coqui-tts==0.27.5 transformers==4.57.6 huggingface_hub>=0.34 soundfile>=0.12";
                    break;
                case "cosyvoice3":
                    packages = "numpy==1.26.4 transformers==4.51.3 protobuf>=4.25,<6";
                    break;
                case "fish-s2-pro":
                    packages = "transformers==4.57.3 pydantic==2.9.2 protobuf>=4.25,<6";
                    break;
                case "f5-tts-ru":
                    packages = "f5-tts==1.1.22 huggingface_hub>=0.34 soundfile>=0.12";
                    break;
                case "qwen3-tts":
                    packages = "qwen-tts==0.1.1 transformers==4.57.3 accelerate==1.12.0 huggingface_hub>=0.34 soundfile>=0.12";
                    break;
                case "chatterbox-v3":
                    packages = "numpy>=1.24,<2 transformers==5.2.0 diffusers==0.29.0 safetensors==0.5.3";
                    break;
                default:
                    packages = JoinPackages(profile.Packages ?? new List<string>());
                    break;
            }
            if (!string.IsNullOrWhiteSpace(packages))
                await RunChecked(uv, p + packages, AppPaths.Runtime, token, onLine).ConfigureAwait(false);
        }

        private async Task<string> EnsureSourceAsync(ModelProfile profile, IProgress<DownloadProgress> progress, CancellationToken token, Action<string> onLine)
        {
            string repo = null;
            if (profile.Id == "cosyvoice3") repo = "https://github.com/FunAudioLLM/CosyVoice/archive/refs/heads/main.zip";
            if (profile.Id == "fish-s2-pro") repo = "https://github.com/fishaudio/fish-speech/archive/refs/heads/main.zip";
            if (profile.Id == "chatterbox-v3") repo = "https://github.com/resemble-ai/chatterbox/archive/refs/heads/master.zip";
            if (repo == null) return "";
            string target = Path.Combine(AppPaths.RuntimeSources, profile.Id);
            string marker = Path.Combine(target, ".source-ready");
            string expected=SourceSchema+"|"+repo;
            if (Directory.Exists(target) && File.Exists(marker) && string.Equals(SafeRead(marker),expected,StringComparison.Ordinal))
            {
                if(profile.Id=="cosyvoice3") await EnsureCosySubmoduleAsync(target,progress,token).ConfigureAwait(false);
                return target;
            }
            string zip = Path.Combine(AppPaths.Temp, profile.Id + "-source.zip");
            await _downloads.DownloadAsync(new DownloadItem { Url = repo }, zip, "Source · " + profile.Name, progress, token).ConfigureAwait(false);
            string unpack = target + ".unpack"; DeleteDirectory(unpack); Directory.CreateDirectory(unpack); ZipFile.ExtractToDirectory(zip, unpack); TryDelete(zip);
            string top = Directory.GetDirectories(unpack).FirstOrDefault() ?? unpack;
            DeleteDirectory(target); Directory.CreateDirectory(target); CopyDirectory(top, target); DeleteDirectory(unpack);
            if (profile.Id == "cosyvoice3") await EnsureCosySubmoduleAsync(target, progress, token).ConfigureAwait(false);
            File.WriteAllText(marker,expected);
            return target;
        }

        private async Task EnsureCosySubmoduleAsync(string source, IProgress<DownloadProgress> progress, CancellationToken token)
        {
            string matcha = Path.Combine(source, "third_party", "Matcha-TTS");
            if (Directory.Exists(matcha) && Directory.GetFiles(matcha, "*.py", SearchOption.AllDirectories).Length > 0) return;
            string zip = Path.Combine(AppPaths.Temp, "matcha-tts.zip");
            await _downloads.DownloadAsync(new DownloadItem { Url = "https://github.com/shivammehta25/Matcha-TTS/archive/refs/heads/main.zip" }, zip, "Source · Matcha-TTS", progress, token).ConfigureAwait(false);
            string unpack = matcha + ".unpack"; DeleteDirectory(unpack); Directory.CreateDirectory(unpack); ZipFile.ExtractToDirectory(zip, unpack); TryDelete(zip);
            string top = Directory.GetDirectories(unpack).FirstOrDefault() ?? unpack; DeleteDirectory(matcha); Directory.CreateDirectory(matcha); CopyDirectory(top, matcha); DeleteDirectory(unpack);
        }

        public async Task EnsureStressAsync(IProgress<DownloadProgress> progress, CancellationToken token, Action<string> onLine)
        {
            if (StressReady) return;
            string uv = await EnsureUvAsync(progress, token).ConfigureAwait(false);
            await RunChecked(uv, "python install 3.11", AppPaths.Runtime, token, onLine).ConfigureAwait(false);
            if (!File.Exists(GetPython(_stressDir))) await RunChecked(uv, "venv --python 3.11 " + ProcessRunner.Quote(_stressDir), AppPaths.Runtime, token, onLine).ConfigureAwait(false);
            string pip = "pip install --python " + ProcessRunner.Quote(GetPython(_stressDir)) + " ";
            string packageReady=Path.Combine(_stressDir,".packages-ready");
            if (!File.Exists(packageReady) || !string.Equals(SafeRead(packageReady),StressSchema,StringComparison.Ordinal))
            {
                var torch = await ProcessRunner.RunAsync(uv, pip + "torch --index-url https://download.pytorch.org/whl/cu128", AppPaths.Runtime, token, onLine).ConfigureAwait(false);
                if (torch.ExitCode != 0) await RunChecked(uv, pip + "torch", AppPaths.Runtime, token, onLine).ConfigureAwait(false);
                await RunChecked(uv, pip + "transformers>=5.6.2,<6 huggingface_hub>=0.34 safetensors sentencepiece", AppPaths.Runtime, token, onLine).ConfigureAwait(false);
                File.WriteAllText(packageReady,StressSchema);
            }
            Directory.CreateDirectory(_stressModelDir);
            var result = await ProcessRunner.RunAsync(GetPython(_stressDir), ProcessRunner.Quote(Path.Combine(AppPaths.RuntimeScripts, "stress_gemma4.py")) + " prepare --model-dir " + ProcessRunner.Quote(_stressModelDir), AppPaths.RuntimeScripts, token, onLine, BuildEnvironment(_stressModelDir)).ConfigureAwait(false);
            if (result.ExitCode != 0) throw BuildProcessError("The Russian stress helper could not be prepared.", result);
            File.WriteAllText(Path.Combine(_stressModelDir, ".ready"), StressSchema);
        }

        public async Task<ProcessResult> RunEngineAsync(ModelProfile profile, string arguments, CancellationToken token, Action<string> onLine)
        {
            string source = (profile.Id == "cosyvoice3" || profile.Id == "fish-s2-pro" || profile.Id == "chatterbox-v3") ? Path.Combine(AppPaths.RuntimeSources, profile.Id) : "";
            string args = ProcessRunner.Quote(Path.Combine(AppPaths.RuntimeScripts, "engine_host.py")) + " " + arguments +
                          " --model-id " + ProcessRunner.Quote(profile.Id) + " --model-dir " + ProcessRunner.Quote(AppPaths.ModelDirectory(profile.Id)) +
                          " --source-dir " + ProcessRunner.Quote(source);
            return await ProcessRunner.RunAsync(GetPython(AppPaths.EngineDirectory(profile.Id)), args, AppPaths.RuntimeScripts, token, onLine, BuildEnvironment(AppPaths.ModelDirectory(profile.Id))).ConfigureAwait(false);
        }

        public async Task<ProcessResult> RunStressAsync(string arguments, CancellationToken token, Action<string> onLine)
        {
            return await ProcessRunner.RunAsync(GetPython(_stressDir), ProcessRunner.Quote(Path.Combine(AppPaths.RuntimeScripts, "stress_gemma4.py")) + " " + arguments,
                AppPaths.RuntimeScripts, token, onLine, BuildEnvironment(_stressModelDir)).ConfigureAwait(false);
        }

        public string GetFfmpegPath() { return Path.Combine(_ffmpegDir, "ffmpeg.exe"); }
        private static string GetPython(string env) { return Path.Combine(env, "Scripts", "python.exe"); }
        private static string RuntimeFingerprint(ModelProfile p)
        {
            // Package pins are part of the fingerprint so changing a compatibility pin in
            // ModelCatalog automatically refreshes an existing isolated environment.
            string packages=p.Packages==null?"":string.Join(";",p.Packages);
            return RuntimeSchema+"|"+SourceSchema+"|"+p.Id+"|"+(p.VersionName??"")+"|py="+(p.PythonVersion??"")+"|"+packages;
        }
        private static IDictionary<string,string> BuildEnvironment(string modelDir)
        {
            return new Dictionary<string,string> {
                {"PYTHONUTF8","1"},{"PYTHONIOENCODING","utf-8"},{"HF_HOME",Path.Combine(modelDir,"_hf")},
                {"HF_HUB_CACHE",Path.Combine(modelDir,"_hf","hub")},{"TORCH_HOME",Path.Combine(modelDir,"_torch")},
                {"XDG_CACHE_HOME",Path.Combine(modelDir,"_cache")},{"TOKENIZERS_PARALLELISM","false"},
                {"PYTORCH_ENABLE_MPS_FALLBACK","1"},{"HF_HUB_DISABLE_SYMLINKS_WARNING","1"}
            };
        }
        private static string JoinPackages(IEnumerable<string> packages) { return string.Join(" ", packages.Select(ProcessRunner.Quote)); }
        private static async Task RunChecked(string exe, string args, string cwd, CancellationToken token, Action<string> line)
        {
            var r = await ProcessRunner.RunAsync(exe,args,cwd,token,line).ConfigureAwait(false);
            if (r.ExitCode != 0) throw BuildProcessError("Runtime command failed.", r);
        }
        private static Exception BuildProcessError(string prefix, ProcessResult r)
        {
            return new InvalidOperationException(prefix + "\r\nExit code: " + r.ExitCode + "\r\n===== STDOUT =====\r\n" + r.StdOut + "\r\n===== STDERR =====\r\n" + r.StdErr);
        }
        private static string SafeRead(string p){try{return File.ReadAllText(p).Trim();}catch{return "";}}
        private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
        private static void DeleteDirectory(string p){try{if(Directory.Exists(p))Directory.Delete(p,true);}catch{}}
        private static void ResetDirectory(string p) { DeleteDirectory(p); Directory.CreateDirectory(p); }
        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
            foreach (var dir in Directory.GetDirectories(source)) CopyDirectory(dir,Path.Combine(target,Path.GetFileName(dir)));
        }
    }
}
