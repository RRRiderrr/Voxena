using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Voxena.Infrastructure;
using Voxena.Models;

namespace Voxena.Services
{
    internal sealed class RussianStressService
    {
        private static readonly Regex Cyrillic=new Regex("[А-Яа-яЁёІіЇїЄєҐґ]",RegexOptions.Compiled);
        private static readonly Regex RussianWord=new Regex("[А-Яа-яЁё\\u0301]+",RegexOptions.Compiled);
        private const char CombiningAcute='\u0301';
        private const char CombiningAcuteTone='\u0341';
        private readonly RuntimeBootstrapService _runtime;
        public RussianStressService(RuntimeBootstrapService runtime){_runtime=runtime;}

        public async Task<string> TransformAsync(string text,bool enabled,IProgress<DownloadProgress> progress,CancellationToken token,Action<string> line)
        {
            var r=await TransformManyAsync(new List<string>{text??""},enabled,progress,token,line).ConfigureAwait(false);
            return r.Count>0?r[0]:text??"";
        }

        public async Task<List<string>> TransformManyAsync(IList<string> texts,bool enabled,IProgress<DownloadProgress> progress,CancellationToken token,Action<string> line)
        {
            var src=(texts??new List<string>()).Select(x=>x??"").ToList();
            if(src.Count==0)return new List<string>();
            if(!enabled||!src.Any(x=>Cyrillic.IsMatch(x)))return src.Select(ApplyManualFallback).ToList();
            try
            {
                await _runtime.EnsureStressAsync(progress,token,line).ConfigureAwait(false);
                string input=Path.Combine(AppPaths.Temp,"stress-batch-"+Guid.NewGuid().ToString("N")+".json"),output=input+".out";
                File.WriteAllText(input,new JavaScriptSerializer().Serialize(src),new UTF8Encoding(false));
                try
                {
                    var r=await _runtime.RunStressAsync("transform-batch --model-dir "+ProcessRunner.Quote(_runtime.StressModelDirectory)+" --input "+ProcessRunner.Quote(input)+" --output "+ProcessRunner.Quote(output),token,line).ConfigureAwait(false);
                    if(r.ExitCode==0&&File.Exists(output))
                    {
                        var values=new JavaScriptSerializer().Deserialize<List<string>>(File.ReadAllText(output,new UTF8Encoding(false)));
                        if(values!=null&&values.Count==src.Count)
                        {
                            for(int i=0;i<values.Count;i++)if(string.IsNullOrWhiteSpace(values[i])&&!string.IsNullOrWhiteSpace(src[i]))values[i]=ApplyManualFallback(src[i]);
                            return values;
                        }
                    }
                    Logger.Write("Stress helper batch fallback: "+r.StdErr);
                    return src.Select(ApplyManualFallback).ToList();
                }
                finally{TryDelete(input);TryDelete(output);}
            }
            catch(Exception ex)
            {
                Logger.Write("Stress helper batch fallback: "+ex);
                return src.Select(ApplyManualFallback).ToList();
            }
        }

        private static string ApplyManualFallback(string text)
        {
            if(string.IsNullOrEmpty(text)||(text.IndexOf(CombiningAcute)<0&&text.IndexOf(CombiningAcuteTone)<0))return text;
            return RussianWord.Replace(text,m=>NormalizeManualWord(m.Value));
        }

        private static string NormalizeManualWord(string word)
        {
            var clean=new StringBuilder(word.Length);int stress=-1;
            for(int i=0;i<word.Length;i++)
            {
                char c=word[i];
                if(c==CombiningAcute||c==CombiningAcuteTone)
                {
                    if(clean.Length>0){stress=clean.Length-1;clean[stress]=char.ToUpperInvariant(clean[stress]);}
                    continue;
                }
                clean.Append(c);
            }
            if(stress<0)return clean.ToString();
            char[] chars=clean.ToString().ToCharArray();
            for(int i=0;i<chars.Length;i++)
            {
                if(i==stress)continue;
                if(chars[i]=='о')chars[i]='а';else if(chars[i]=='О')chars[i]='А';
            }
            string lower=new string(chars).ToLowerInvariant();
            if(lower.StartsWith("что",StringComparison.Ordinal)&&chars.Length>=3)chars[0]=char.IsUpper(chars[0])?'Ш':'ш';
            // Preserve manual position with the private marker used by engine adapters.
            var outText=new StringBuilder(chars.Length+1);
            for(int i=0;i<chars.Length;i++)
            {
                char c=chars[i];
                if(i==stress)c=char.ToLowerInvariant(c);
                outText.Append(c);
                if(i==stress)outText.Append('\ue000');
            }
            return outText.ToString();
        }
        private static void TryDelete(string p){try{if(File.Exists(p))File.Delete(p);}catch{}}
    }
}
