using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Voxena.Infrastructure;
using Voxena.Models;

namespace Voxena.Services
{
    internal sealed class ModelManager
    {
        private readonly RuntimeBootstrapService _runtime;
        public ModelManager(RuntimeBootstrapService runtime) { _runtime = runtime; }
        public List<ModelProfile> GetProfiles()
        {
            var list=ModelCatalog.Create();
            foreach(var p in list) p.Installed=IsInstalled(p.Id);
            return list;
        }
        public ModelProfile GetInstalled(string id) { return GetProfiles().FirstOrDefault(x=>x.Id==id && x.Installed); }
        public bool IsInstalled(string id) { return File.Exists(Path.Combine(AppPaths.ModelDirectory(id),".installed.json")); }
        public async Task InstallManyAsync(IEnumerable<string> ids,IProgress<DownloadProgress> progress,CancellationToken token,Action<string> line)
        {
            var selected=(ids??Enumerable.Empty<string>()).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(selected.Count==0) throw new InvalidOperationException("Select at least one model.");
            var failures=new List<string>();
            foreach(string id in selected)
            {
                token.ThrowIfCancellationRequested();
                try{await InstallAsync(id,progress,token,line).ConfigureAwait(false);}
                catch(OperationCanceledException){throw;}
                catch(Exception ex)
                {
                    var p=ModelCatalog.Get(id);
                    failures.Add((p==null?id:p.Name)+": "+FirstLine(ex.Message));
                    Logger.Write("Model install failed for "+id+": "+ex);
                }
            }
            if(selected.Any(IsInstalled))
            {
                try{await _runtime.EnsureStressAsync(progress,token,line).ConfigureAwait(false);}
                catch(OperationCanceledException){throw;}
                catch(Exception ex){failures.Add("Russian stress helper: "+FirstLine(ex.Message));Logger.Write("Stress helper setup failed: "+ex);}
            }
            if(failures.Count>0)
                throw new InvalidOperationException("Some selected components could not be prepared. Successfully installed models were kept. You can retry the failed items later.\r\n\r\n- "+string.Join("\r\n- ",failures));
        }
        public async Task InstallAsync(string id,IProgress<DownloadProgress> progress,CancellationToken token,Action<string> line)
        {
            var p=ModelCatalog.Get(id); if(p==null) throw new InvalidOperationException("Unknown model: "+id);
            if(IsInstalled(id)) return;
            Directory.CreateDirectory(AppPaths.ModelDirectory(id));
            await _runtime.EnsureEngineAsync(p,progress,token,line).ConfigureAwait(false);
            var r=await _runtime.RunEngineAsync(p,"prepare --device auto",token,line).ConfigureAwait(false);
            if(r.ExitCode!=0) throw new InvalidOperationException("Model setup failed for "+p.Name+".\r\n===== STDOUT =====\r\n"+r.StdOut+"\r\n===== STDERR =====\r\n"+r.StdErr);
            File.WriteAllText(Path.Combine(AppPaths.ModelDirectory(id),".installed.json"),"{\"installedUtc\":\""+DateTime.UtcNow.ToString("O")+"\",\"version\":\""+Escape(p.VersionName)+"\"}");
        }
        public void Remove(string id)
        {
            var p=ModelCatalog.Get(id); if(p==null) return;
            string model=AppPaths.ModelDirectory(id), env=AppPaths.EngineDirectory(id), source=Path.Combine(AppPaths.RuntimeSources,id);
            if(Directory.Exists(model)) Directory.Delete(model,true);
            if(Directory.Exists(env)) Directory.Delete(env,true);
            if(Directory.Exists(source)) Directory.Delete(source,true);
        }
        private static string FirstLine(string value){if(string.IsNullOrWhiteSpace(value))return "Unknown error.";int i=value.IndexOfAny(new[]{'\r','\n'});return i<0?value.Trim():value.Substring(0,i).Trim();}
        private static string Escape(string v){return (v??"").Replace("\\","\\\\").Replace("\"","\\\"");}
    }
}
