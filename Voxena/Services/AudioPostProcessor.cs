using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Voxena.Infrastructure;
using Voxena.Models;

namespace Voxena.Services
{
    internal sealed class AudioPostProcessor
    {
        private readonly RuntimeBootstrapService _runtime;
        public AudioPostProcessor(RuntimeBootstrapService runtime){_runtime=runtime;}

        // Local per-segment processing. Emotion/tag effects belong here so a tag can affect
        // only its own spoken span. Global normalize/trim are deliberately postponed until
        // after all spans have been joined.
        public async Task<string> ProcessSegmentAsync(string input,string output,GenerationRequest request,TagStyle tags,IProgress<DownloadProgress> progress,CancellationToken token,Action<string> line)
        {
            string ff=await _runtime.EnsureFfmpegAsync(progress,token).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var filters=new List<string>();
            double speed=Math.Max(.5,Math.Min(2,request.Speed*(tags==null?1:tags.SpeedMultiplier)));
            if(Math.Abs(speed-1)>.001)filters.Add("atempo="+speed.ToString("0.###",CultureInfo.InvariantCulture));
            double pitch=request.Pitch+(tags==null?0:tags.PitchSemitones);
            if(Math.Abs(pitch)>.01)
            {
                double ratio=Math.Pow(2,pitch/12.0);
                filters.Add("asetrate=44100*"+ratio.ToString("0.######",CultureInfo.InvariantCulture)+",aresample=44100");
            }
            double vol=tags==null?0:tags.VolumeDb;
            if(Math.Abs(vol)>.01)filters.Add("volume="+vol.ToString("0.##",CultureInfo.InvariantCulture)+"dB");
            if(tags!=null&&tags.WhisperEffect)filters.Add("highpass=f=180,lowpass=f=8000");

            string args="-y -hide_banner -loglevel warning -i "+ProcessRunner.Quote(input)+
                        (filters.Count>0?" -af "+ProcessRunner.Quote(string.Join(",",filters)):"")+
                        " -ac 1 -ar "+Math.Max(16000,request.SampleRate)+" -c:a pcm_s16le "+ProcessRunner.Quote(output);
            var r=await ProcessRunner.RunAsync(ff,args,Path.GetDirectoryName(input),token,line).ConfigureAwait(false);
            if(r.ExitCode!=0)throw new InvalidOperationException("Audio segment processing failed.\r\n"+r.StdErr);
            return output;
        }

        public async Task<string> CreateSilenceAsync(string output,double seconds,int sampleRate,IProgress<DownloadProgress> progress,CancellationToken token,Action<string> line)
        {
            string ff=await _runtime.EnsureFfmpegAsync(progress,token).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            double d=Math.Max(.08,Math.Min(4.0,seconds));
            string args="-y -hide_banner -loglevel warning -f lavfi -i "+ProcessRunner.Quote("anullsrc=r="+Math.Max(16000,sampleRate)+":cl=mono")+
                        " -t "+d.ToString("0.###",CultureInfo.InvariantCulture)+" -c:a pcm_s16le "+ProcessRunner.Quote(output);
            var r=await ProcessRunner.RunAsync(ff,args,Path.GetDirectoryName(output),token,line).ConfigureAwait(false);
            if(r.ExitCode!=0)throw new InvalidOperationException("Could not create a pause segment.\r\n"+r.StdErr);
            return output;
        }

        public async Task<string> ConcatAsync(IList<string> inputs,string output,int sampleRate,IProgress<DownloadProgress> progress,CancellationToken token,Action<string> line)
        {
            if(inputs==null||inputs.Count==0)throw new InvalidOperationException("There are no generated speech segments to join.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            if(inputs.Count==1){File.Copy(inputs[0],output,true);return output;}

            string ff=await _runtime.EnsureFfmpegAsync(progress,token).ConfigureAwait(false);
            string list=Path.Combine(AppPaths.Temp,"concat-"+Guid.NewGuid().ToString("N")+".txt");
            try
            {
                var sb=new StringBuilder();
                foreach(string input in inputs)
                {
                    string full=Path.GetFullPath(input).Replace('\\','/').Replace("'","'\\''");
                    sb.Append("file '").Append(full).AppendLine("'");
                }
                File.WriteAllText(list,sb.ToString(),new UTF8Encoding(false));
                string args="-y -hide_banner -loglevel warning -f concat -safe 0 -i "+ProcessRunner.Quote(list)+
                            " -ac 1 -ar "+Math.Max(16000,sampleRate)+" -c:a pcm_s16le "+ProcessRunner.Quote(output);
                var r=await ProcessRunner.RunAsync(ff,args,Path.GetDirectoryName(output),token,line).ConfigureAwait(false);
                if(r.ExitCode!=0)throw new InvalidOperationException("Could not join generated speech segments.\r\n"+r.StdErr);
                return output;
            }
            finally{try{if(File.Exists(list))File.Delete(list);}catch{}}
        }

        public async Task<string> FinalizeAsync(string input,string output,GenerationRequest request,IProgress<DownloadProgress> progress,CancellationToken token,Action<string> line)
        {
            string ff=await _runtime.EnsureFfmpegAsync(progress,token).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var filters=new List<string>();
            // Trim ONLY the outside edges. The old negative stop_periods form removed internal
            // silence too, which would destroy explicit [pause] segments after concatenation.
            if(request.TrimSilence)filters.Add("silenceremove=start_periods=1:start_duration=0.05:start_threshold=-48dB,areverse,silenceremove=start_periods=1:start_duration=0.08:start_threshold=-48dB,areverse");
            if(request.Normalize)filters.Add("loudnorm=I=-16:LRA=11:TP=-1.5");
            string codec="";string ext=(request.Format??"wav").ToLowerInvariant();
            if(ext=="mp3")codec="-c:a libmp3lame -b:a "+Math.Max(64,request.BitrateKbps)+"k";
            else if(ext=="m4a")codec="-c:a aac -b:a "+Math.Max(64,request.BitrateKbps)+"k";
            else if(ext=="flac")codec="-c:a flac";
            else if(ext=="ogg")codec="-c:a libvorbis -q:a 6";
            else codec="-c:a pcm_s16le";
            string args="-y -hide_banner -loglevel warning -i "+ProcessRunner.Quote(input)+
                        (filters.Count>0?" -af "+ProcessRunner.Quote(string.Join(",",filters)):"")+
                        " -ar "+Math.Max(16000,request.SampleRate)+" "+codec+" "+ProcessRunner.Quote(output);
            var r=await ProcessRunner.RunAsync(ff,args,Path.GetDirectoryName(input),token,line).ConfigureAwait(false);
            if(r.ExitCode!=0)throw new InvalidOperationException("Audio finalization failed.\r\n"+r.StdErr);
            return output;
        }

        // Backward-compatible one-shot helper.
        public async Task<string> ProcessAsync(string input,string output,GenerationRequest request,TagStyle tags,IProgress<DownloadProgress> progress,CancellationToken token,Action<string> line)
        {
            string tmp=Path.Combine(AppPaths.Temp,"post-"+Guid.NewGuid().ToString("N")+".wav");
            try{await ProcessSegmentAsync(input,tmp,request,tags,progress,token,line).ConfigureAwait(false);return await FinalizeAsync(tmp,output,request,progress,token,line).ConfigureAwait(false);}
            finally{try{if(File.Exists(tmp))File.Delete(tmp);}catch{}}
        }
    }
}
