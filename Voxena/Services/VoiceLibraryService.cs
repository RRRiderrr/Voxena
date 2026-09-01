using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Voxena.Infrastructure;
using Voxena.Models;

namespace Voxena.Services
{
    internal sealed class VoiceLibraryService
    {
        private readonly ModelManager _models; private readonly RuntimeBootstrapService _runtime; private readonly JavaScriptSerializer _json=new JavaScriptSerializer();
        public VoiceLibraryService(ModelManager models,RuntimeBootstrapService runtime){_models=models;_runtime=runtime;}
        public List<VoiceProfile> GetAll()
        {
            var result=new List<VoiceProfile>();
            foreach(var p in _models.GetProfiles().Where(x=>x.Installed)) foreach(string preset in p.PresetVoices??new List<string>())
                result.Add(new VoiceProfile{Id="preset-"+p.Id+"-"+preset.ToLowerInvariant().Replace("_","-"),ModelId=p.Id,ModelName=p.Name,Name=preset,Description="Built-in "+p.Name+" voice",Kind="preset",PresetId=preset,CreatedUtc=DateTime.MinValue,Available=true});
            if(Directory.Exists(AppPaths.CustomVoices)) foreach(string dir in Directory.GetDirectories(AppPaths.CustomVoices))
            {
                try{string meta=Path.Combine(dir,"voice.json");if(!File.Exists(meta))continue;var v=_json.Deserialize<VoiceProfile>(File.ReadAllText(meta));if(v==null)continue;var p=ModelCatalog.Get(v.ModelId);v.ModelName=p==null?v.ModelId:p.Name;v.Available=p!=null&&_models.IsInstalled(v.ModelId);if(!string.IsNullOrWhiteSpace(v.FilePath)&&!Path.IsPathRooted(v.FilePath))v.FilePath=Path.Combine(dir,v.FilePath);if(!string.IsNullOrWhiteSpace(v.PreparedPath)&&!Path.IsPathRooted(v.PreparedPath))v.PreparedPath=Path.Combine(dir,v.PreparedPath);result.Add(v);}catch(Exception ex){Logger.Write("Voice metadata error: "+ex.Message);}
            }
            return result.OrderBy(x=>x.Kind=="custom"?0:1).ThenBy(x=>x.ModelName).ThenBy(x=>x.Name).ToList();
        }
        public VoiceProfile Find(string id){return GetAll().FirstOrDefault(x=>x.Id==id);}
        public async Task<VoiceProfile> ImportAndPrepareAsync(string source,string name,string description,string modelId,string transcript,IProgress<DownloadProgress> progress,CancellationToken token,Action<string> line)
        {
            var p=_models.GetInstalled(modelId);if(p==null)throw new InvalidOperationException("Install "+(ModelCatalog.Get(modelId)?.Name??modelId)+" before cloning a voice.");
            if(p.CloneTranscriptRequired&&string.IsNullOrWhiteSpace(transcript))throw new InvalidOperationException(p.Name+" requires an exact transcript of the reference audio.");
            if(!File.Exists(source))throw new FileNotFoundException("Reference audio file was not found.",source);
            await _runtime.EnsureEngineAsync(p,progress,token,line).ConfigureAwait(false);
            string id="custom-"+Guid.NewGuid().ToString("N"),dir=Path.Combine(AppPaths.CustomVoices,id);Directory.CreateDirectory(dir);
            string ext=Path.GetExtension(source);if(string.IsNullOrWhiteSpace(ext))ext=".wav";string refFile=Path.Combine(dir,"reference"+ext);File.Copy(source,refFile,true);
            string prepared=p.PreparedExtension==".dir"?Path.Combine(dir,"prepared"):Path.Combine(dir,"prepared"+p.PreparedExtension);
            var voice=new VoiceProfile{Id=id,ModelId=p.Id,ModelName=p.Name,Name=string.IsNullOrWhiteSpace(name)?"Custom voice":name.Trim(),Description=description??"",Kind="custom",FilePath=Path.GetFileName(refFile),Transcript=transcript??"",PreparedPath=p.PreparedExtension==".dir"?"prepared":"prepared"+p.PreparedExtension,CreatedUtc=DateTime.UtcNow,PreparationVersion=p.VersionName};
            Save(dir,voice);
            try{
                string args="clone --audio "+ProcessRunner.Quote(refFile)+" --transcript "+ProcessRunner.Quote(transcript??"")+" --output "+ProcessRunner.Quote(prepared)+" --device auto";
                var r=await _runtime.RunEngineAsync(p,args,token,line).ConfigureAwait(false);if(r.ExitCode!=0)throw new InvalidOperationException("Voice preparation failed for "+p.Name+".\r\n===== STDOUT =====\r\n"+r.StdOut+"\r\n===== STDERR =====\r\n"+r.StdErr);
                voice.PreparedUtc=DateTime.UtcNow;Save(dir,voice);return Find(id);
            }catch{try{Directory.Delete(dir,true);}catch{}throw;}
        }
        public void Delete(string id){var v=Find(id);if(v==null||v.Kind!="custom")return;string dir=Path.Combine(AppPaths.CustomVoices,id);if(Directory.Exists(dir))Directory.Delete(dir,true);}
        private void Save(string dir,VoiceProfile v){File.WriteAllText(Path.Combine(dir,"voice.json"),_json.Serialize(v));}
    }
}
