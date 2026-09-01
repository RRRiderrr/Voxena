using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Voxena.Infrastructure;
using Voxena.Models;

namespace Voxena.Services
{
    internal sealed class TtsEngine
    {
        private readonly ModelManager _models;
        private readonly VoiceLibraryService _voices;
        private readonly RuntimeBootstrapService _runtime;
        private readonly RussianStressService _stress;
        private readonly AudioPostProcessor _post;

        public TtsEngine(ModelManager models, VoiceLibraryService voices, RuntimeBootstrapService runtime, RussianStressService stress, AudioPostProcessor post)
        { _models=models; _voices=voices; _runtime=runtime; _stress=stress; _post=post; }

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request, AppSettings settings, IProgress<DownloadProgress> progress, CancellationToken token, Action<string> line)
        {
            var many=await GenerateVariantsAsync(request,settings,new[]{request.Seed},progress,token,line).ConfigureAwait(false);
            return many.Count>0?many[0]:new GenerationResult{Success=false,Error="No audio was generated."};
        }

        public async Task<List<GenerationResult>> GenerateVariantsAsync(GenerationRequest request, AppSettings settings, int[] seeds, IProgress<DownloadProgress> progress, CancellationToken token, Action<string> line)
        {
            var results=new List<GenerationResult>();
            try
            {
                var voice=_voices.Find(request.VoiceId);
                if(voice==null)throw new InvalidOperationException("Select a voice first.");
                var model=_models.GetInstalled(voice.ModelId);
                if(model==null)throw new InvalidOperationException("The model for this voice is not installed: "+voice.ModelName);
                await _runtime.EnsureEngineAsync(model,progress,token,line).ConfigureAwait(false);

                var parsed=TagParser.ParseSegments(request.Text);
                var spoken=parsed.Where(x=>x!=null&&!x.IsPause&&!x.IsEvent&&!string.IsNullOrWhiteSpace(x.Text)).ToList();
                if(spoken.Count==0&&!parsed.Any(x=>x!=null&&x.IsEvent))throw new InvalidOperationException("Enter some text first.");

                // Stress every spoken span in one Gemma process. The tag boundaries are kept out
                // of the linguistic prompt while Gemma itself is loaded only once.
                var transformed=await _stress.TransformManyAsync(spoken.Select(x=>x.Text).ToList(),settings.StressRussian,progress,token,line).ConfigureAwait(false);
                int ti=0;
                foreach(var segment in parsed)
                {
                    if(segment==null||segment.IsPause||segment.IsEvent||string.IsNullOrWhiteSpace(segment.Text))continue;
                    segment.Text=transformed[ti++];
                }
                Logger.Write("Segmented tags: "+parsed.Count+" spans/events, "+spoken.Count+" spoken spans.");

                AppPaths.ClearGeneratedCache();
                int[] actualSeeds=(seeds==null||seeds.Length==0)?new[]{request.Seed}:seeds;
                for(int variant=0;variant<actualSeeds.Length;variant++)
                {
                    token.ThrowIfCancellationRequested();
                    int seed=actualSeeds[variant];
                    string job=Guid.NewGuid().ToString("N");
                    string sequenceFile=Path.Combine(AppPaths.Temp,job+"-sequence.json");
                    string merged=Path.Combine(AppPaths.Temp,job+"-merged.wav");
                    var rawFiles=new List<string>();
                    var processedFiles=new List<string>();
                    try
                    {
                        var sequence=new List<Dictionary<string,object>>();
                        int synthIndex=0;
                        foreach(var segment in parsed)
                        {
                            if(segment==null||segment.IsPause)continue;
                            bool isEvent=segment.IsEvent&&!string.IsNullOrWhiteSpace(segment.EventName);
                            if(!isEvent&&string.IsNullOrWhiteSpace(segment.Text))continue;
                            string raw=Path.Combine(AppPaths.Temp,job+"-seg"+synthIndex+"-raw.wav");
                            rawFiles.Add(raw);
                            var st=segment.Style??new TagStyle{SpeedMultiplier=1.0};
                            double temp=Math.Max(.2,Math.Min(1.4,.45+(1-request.Stability)*.55+st.TemperatureDelta));
                            double expression=Math.Max(0.0,Math.Min(1.0,request.Expressiveness+st.ExpressivenessDelta));
                            int segmentSeed=DeriveSegmentSeed(seed,synthIndex);
                            string eventName=isEvent?(segment.EventName??""):"";
                            string synthText=isEvent?EventFallbackText(request.Text,eventName):(segment.Text??"");
                            sequence.Add(new Dictionary<string,object>{
                                {"text",synthText},{"output",raw},{"temperature",temp},{"speed",1.0},
                                {"seed",segmentSeed},{"delivery",st.DeliveryInstruction??""},
                                {"native_tags",st.NativeTags??""},{"event",eventName},
                                {"expressiveness",expression},{"stability",request.Stability}
                            });
                            synthIndex++;
                        }
                        File.WriteAllText(sequenceFile,new JavaScriptSerializer().Serialize(sequence),new System.Text.UTF8Encoding(false));

                        if(line!=null)line("Rendering variant "+(variant+1)+"/"+actualSeeds.Length+" · "+sequence.Count+" tagged segment"+(sequence.Count==1?"":"s")+"…");
                        string args="generate-sequence --voice-kind "+ProcessRunner.Quote(voice.Kind)+
                            " --voice "+ProcessRunner.Quote(voice.PreparedPath??"")+
                            " --preset-id "+ProcessRunner.Quote(voice.PresetId??"")+
                            " --transcript "+ProcessRunner.Quote(voice.Transcript??"")+
                            " --sequence-file "+ProcessRunner.Quote(sequenceFile)+
                            " --device "+ProcessRunner.Quote(settings.DevicePreference??"auto");
                        var rr=await _runtime.RunEngineAsync(model,args,token,line).ConfigureAwait(false);
                        if(rr.ExitCode!=0)throw new InvalidOperationException(model.Name+" segmented synthesis failed.\r\n===== STDOUT =====\r\n"+rr.StdOut+"\r\n===== STDERR =====\r\n"+rr.StdErr);
                        foreach(string raw in rawFiles)if(!File.Exists(raw))throw new FileNotFoundException(model.Name+" did not create every tagged audio segment: "+raw);

                        // Rebuild the original sequence, inserting real silence events where requested,
                        // and apply DSP only to the span whose tags requested it.
                        int rawIndex=0;
                        int outIndex=0;
                        foreach(var segment in parsed)
                        {
                            if(segment==null)continue;
                            string processed=Path.Combine(AppPaths.Temp,job+"-seg"+outIndex+"-processed.wav");
                            if(segment.IsPause)
                                await _post.CreateSilenceAsync(processed,segment.PauseSeconds,request.SampleRate,progress,token,line).ConfigureAwait(false);
                            else if(segment.IsEvent||!string.IsNullOrWhiteSpace(segment.Text))
                                await _post.ProcessSegmentAsync(rawFiles[rawIndex++],processed,request,segment.Style,progress,token,line).ConfigureAwait(false);
                            else continue;
                            processedFiles.Add(processed);outIndex++;
                        }

                        await _post.ConcatAsync(processedFiles,merged,request.SampleRate,progress,token,line).ConfigureAwait(false);
                        string fmt=(request.Format??"mp3").ToLowerInvariant();
                        string letter=variant==0?"A":(variant==1?"B":(variant+1).ToString(CultureInfo.InvariantCulture));
                        string name="voxena-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+letter+"-s"+seed+"-"+job.Substring(0,6)+"."+fmt;
                        string final=Path.Combine(AppPaths.Generated,name);
                        await _post.FinalizeAsync(merged,final,request,progress,token,line).ConfigureAwait(false);
                        results.Add(new GenerationResult{Success=true,FileName=name,FilePath=final,AudioUrl="https://audio.voxena/"+Uri.EscapeDataString(name),ModelId=model.Id,VoiceId=voice.Id,Seconds=0,Seed=seed});
                    }
                    finally
                    {
                        TryDelete(sequenceFile);TryDelete(merged);
                        foreach(string p in rawFiles)TryDelete(p);
                        foreach(string p in processedFiles)TryDelete(p);
                    }
                }
                return results;
            }
            catch(OperationCanceledException)
            {
                AppPaths.ClearGeneratedCache();
                return new List<GenerationResult>{new GenerationResult{Success=false,Error="Cancelled."}};
            }
            catch(Exception ex)
            {
                AppPaths.ClearGeneratedCache();Logger.Write("Generation failed: "+ex);
                return new List<GenerationResult>{new GenerationResult{Success=false,Error=ex.ToString()}};
            }
        }

        private static string EventFallbackText(string wholeText,string eventName)
        {
            bool cyrillic=!string.IsNullOrWhiteSpace(wholeText)&&wholeText.Any(ch=>(ch>='А'&&ch<='я')||ch=='Ё'||ch=='ё'||ch=='І'||ch=='і'||ch=='Ї'||ch=='ї'||ch=='Є'||ch=='є');
            if(string.Equals(eventName,"laugh",StringComparison.OrdinalIgnoreCase))return cyrillic?"Ха-ха!":"Ha-ha!";
            if(string.Equals(eventName,"sigh",StringComparison.OrdinalIgnoreCase))return cyrillic?"Ах...":"Ah...";
            return cyrillic?"Мм...":"Mm...";
        }

        private static int DeriveSegmentSeed(int baseSeed,int index)
        {
            long v=(long)Math.Max(1,baseSeed)+(long)index*7919L;
            v%=int.MaxValue;if(v<=0)v+=1;return (int)v;
        }
        private static void TryDelete(string p){try{if(File.Exists(p))File.Delete(p);}catch{}}
    }
}
