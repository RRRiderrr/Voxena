using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Voxena.Models;

namespace Voxena.Services
{
    internal static class TagParser
    {
        private static readonly Regex TagRegex=new Regex(@"\[(?<close>/)?(?<name>[\p{L}][\p{L}\p{Nd}_-]*)(?:\s*[:=]\s*(?<value>[0-9]+(?:\.[0-9]+)?))?\]",RegexOptions.Compiled|RegexOptions.CultureInvariant);
        // Built through index assignments instead of Dictionary.Add/collection initializer.
        // Duplicate aliases are intentionally harmless: the last mapping wins instead of
        // crashing TagParser's static constructor and disabling all synthesis.
        private static readonly Dictionary<string,string> Aliases=BuildAliases();

        private static Dictionary<string,string> BuildAliases()
        {
            var d=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            d["радостно"]="happy";
            d["восторженно"]="excited";
            d["грустно"]="sad";
            d["зло"]="angry";
            d["спокойно"]="calm";
            d["серьёзно"]="serious";
            d["серьезно"]="serious";
            d["саркастично"]="sarcastic";
            d["сочувственно"]="empathetic";
            d["шёпотом"]="whisper";
            d["шепотом"]="whisper";
            d["мягко"]="soft";
            d["громко"]="loud";
            d["медленно"]="slow";
            d["быстро"]="fast";
            d["глубже"]="deep";
            d["ярко"]="bright";
            d["диктор"]="narration";
            d["пауза"]="pause";
            d["смех"]="laughs";
            d["вздох"]="sighs";
            d["обычно"]="normal";
            d["радісно"]="happy";
            d["захоплено"]="excited";
            d["сумно"]="sad";
            d["сердито"]="angry";
            d["спокійно"]="calm";
            d["серйозно"]="serious";
            d["саркастично"]="sarcastic";
            d["співчутливо"]="empathetic";
            d["пошепки"]="whisper";
            d["лагідно"]="soft";
            d["голосно"]="loud";
            d["повільно"]="slow";
            d["швидко"]="fast";
            d["глибше"]="deep";
            d["яскраво"]="bright";
            d["диктор"]="narration";
            d["пауза"]="pause";
            d["сміх"]="laughs";
            d["зітхання"]="sighs";
            d["звичайно"]="normal";
            return d;
        }

        /// <summary>
        /// Style tags are state markers for the text that follows them. Adjacent style tags
        /// accumulate until actual text appears; a style tag after text starts a fresh state.
        /// Event tags ([laughs]/[sighs]) are real timeline events and do not mutate the active
        /// style. This lets [sad][whisper] text combine while [sad] text [angry] text switches.
        /// </summary>
        public static List<TagSegment> ParseSegments(string input)
        {
            string source=input??"";
            var result=new List<TagSegment>();
            var style=NewStyle();
            int pos=0;
            bool textSinceStyleRun=false;

            foreach(Match m in TagRegex.Matches(source))
            {
                string between=source.Substring(pos,m.Index-pos);
                if(!string.IsNullOrWhiteSpace(between))
                {
                    AddText(result,between,style);
                    textSinceStyleRun=true;
                }

                string raw=m.Groups["name"].Value;
                string name=NormalizeName(raw);
                string value=m.Groups["value"].Value;
                bool closing=m.Groups["close"].Success;

                if(closing)
                {
                    style=NewStyle();
                    textSinceStyleRun=false;
                    pos=m.Index+m.Length;
                    continue;
                }

                if(string.Equals(name,"pause",StringComparison.OrdinalIgnoreCase))
                {
                    double seconds=Clamp(Parse(value,.65),.08,4.0);
                    result.Add(new TagSegment{IsPause=true,PauseSeconds=seconds,Style=CloneStyle(style)});
                    pos=m.Index+m.Length;
                    continue;
                }

                // Paralinguistic cues are timeline events, not persistent style flags.
                if(IsEvent(name))
                {
                    result.Add(new TagSegment{IsEvent=true,EventName=CanonicalEvent(name),Style=CloneStyle(style)});
                    pos=m.Index+m.Length;
                    continue;
                }

                if(textSinceStyleRun)
                {
                    var fresh=NewStyle();
                    if(ApplyStyleTag(fresh,name,value))
                    {
                        style=fresh;
                        textSinceStyleRun=false;
                    }
                }
                else ApplyStyleTag(style,name,value);
                pos=m.Index+m.Length;
            }

            string tail=source.Substring(pos);
            if(!string.IsNullOrWhiteSpace(tail))AddText(result,tail,style);
            return result;
        }

        public static TagStyle Parse(string input)
        {
            var segments=ParseSegments(input);
            if(segments.Count==0)return NewStyle();
            foreach(var s in segments)if(!s.IsPause&&!s.IsEvent)return s.Style??NewStyle();
            return NewStyle();
        }

        private static void AddText(List<TagSegment> result,string text,TagStyle style)
        {
            string clean=Regex.Replace(text,@"[ \t]{2,}"," ");
            if(string.IsNullOrWhiteSpace(clean))return;
            result.Add(new TagSegment{Text=clean.Trim(),Style=CloneStyle(style)});
        }

        private static string NormalizeName(string name)
        {
            string n=(name??"").ToLowerInvariant(),a;
            return Aliases.TryGetValue(n,out a)?a:n;
        }

        private static bool IsEvent(string n)
        {
            return n=="laugh"||n=="laughs"||n=="laughter"||n=="sigh"||n=="sighs";
        }
        private static string CanonicalEvent(string n)
        {
            return (n=="laugh"||n=="laughs"||n=="laughter")?"laugh":"sigh";
        }

        private static bool ApplyStyleTag(TagStyle s,string n,string v)
        {
            switch(n)
            {
                case "normal": case "neutral": Reset(s); return true;

                // Semantic tags: use explicit model instructions where supported and still
                // provide conservative DSP/expression deltas as a fallback for clone-only engines.
                // None of these shifts pitch, so emotions cannot turn the speaker cartoonish.
                case "happy":
                    AddCue(s,"Speak with a clearly happy, warm and cheerful emotional tone. Keep the same speaker identity and natural vocal pitch.");
                    AddNative(s,"happy");s.TemperatureDelta+=.04;s.SpeedMultiplier*=1.03;s.VolumeDb+=.25;s.ExpressivenessDelta+=.12;return true;
                case "excited":
                    AddCue(s,"Speak with unmistakable excitement and energetic enthusiasm. Keep the same speaker identity; do not make the voice higher-pitched or cartoonish.");
                    AddNative(s,"excited");s.TemperatureDelta+=.08;s.SpeedMultiplier*=1.06;s.VolumeDb+=.55;s.ExpressivenessDelta+=.22;return true;
                case "sad":
                    AddCue(s,"Speak in a clearly sad, subdued and downcast manner, with restrained energy. Preserve the speaker's normal timbre and pitch.");
                    AddNative(s,"sad");s.TemperatureDelta-=.03;s.SpeedMultiplier*=.90;s.VolumeDb-=.75;s.ExpressivenessDelta+=.10;return true;
                case "angry":
                    AddCue(s,"Speak with clearly audible anger: tense, forceful and irritated, while preserving the same speaker identity and natural pitch.");
                    AddNative(s,"angry");s.TemperatureDelta+=.06;s.SpeedMultiplier*=1.03;s.VolumeDb+=1.15;s.ExpressivenessDelta+=.24;return true;
                case "calm":
                    AddCue(s,"Speak calmly and gently, relaxed and controlled. Preserve the same speaker identity.");
                    AddNative(s,"calm");s.TemperatureDelta-=.05;s.SpeedMultiplier*=.97;s.ExpressivenessDelta-=.08;return true;
                case "serious":
                    AddCue(s,"Use a serious, firm and composed delivery without changing the speaker identity.");
                    AddNative(s,"serious");s.TemperatureDelta-=.03;s.ExpressivenessDelta-=.02;return true;
                case "sarcastic":
                    AddCue(s,"Use an unmistakably sarcastic, dry delivery with natural ironic emphasis. Preserve the same speaker identity.");
                    AddNative(s,"sarcastic");s.TemperatureDelta+=.05;s.ExpressivenessDelta+=.12;return true;
                case "empathetic":
                    AddCue(s,"Speak empathetically, gently and thoughtfully, with caring emotional warmth.");
                    AddNative(s,"empathetic");s.SpeedMultiplier*=.96;s.ExpressivenessDelta+=.07;return true;
                case "whisper": case "whispers": case "whispering":
                    AddCue(s,"Whisper softly and naturally. Keep the same speaker identity and avoid a high-pitched voice.");
                    AddNative(s,"whisper");s.WhisperEffect=true;s.VolumeDb-=2.0;s.SpeedMultiplier*=.97;s.TemperatureDelta-=.03;s.ExpressivenessDelta-=.05;return true;
                case "loud": case "shout": case "shouting":
                    AddCue(s,"Speak loudly and forcefully, like a natural shout, without changing the speaker identity.");
                    AddNative(s,"shouting");s.VolumeDb+=2.0;s.ExpressivenessDelta+=.18;return true;
                case "soft":
                    AddCue(s,"Speak softly and quietly, with gentle delivery.");
                    AddNative(s,"low volume");s.VolumeDb-=1.4;s.ExpressivenessDelta-=.04;return true;
                case "slow":
                    AddCue(s,"Speak noticeably slower, with natural pauses and clear articulation.");
                    AddNative(s,"speak slowly");s.SpeedMultiplier*=.84;return true;
                case "fast":
                    AddCue(s,"Speak noticeably faster while staying intelligible and natural.");
                    AddNative(s,"speak quickly");s.SpeedMultiplier*=1.16;return true;
                case "deep":
                    AddNative(s,"low voice");s.PitchSemitones-=1.0;return true;
                case "bright":
                    AddNative(s,"pitch up");s.PitchSemitones+=.8;return true;
                case "narration":
                    AddCue(s,"Use a polished professional narrator delivery with steady pacing and clear diction.");
                    AddNative(s,"professional broadcast tone");s.ExpressivenessDelta-=.02;return true;
                default:return false;
            }
        }

        private static TagStyle NewStyle(){return new TagStyle{SpeedMultiplier=1.0};}
        private static TagStyle CloneStyle(TagStyle s)
        {
            if(s==null)return NewStyle();
            return new TagStyle{
                TemperatureDelta=Clamp(s.TemperatureDelta,-.25,.25),
                SpeedMultiplier=Clamp(s.SpeedMultiplier,.60,1.55),
                PitchSemitones=Clamp(s.PitchSemitones,-4,4),
                VolumeDb=Clamp(s.VolumeDb,-8,5),
                WhisperEffect=s.WhisperEffect,
                DeliveryInstruction=s.DeliveryInstruction??"",
                NativeTags=s.NativeTags??"",
                ExpressivenessDelta=Clamp(s.ExpressivenessDelta,-.35,.40)
            };
        }
        private static void AddCue(TagStyle s,string cue)
        {
            if(string.IsNullOrWhiteSpace(cue))return;
            if(string.IsNullOrWhiteSpace(s.DeliveryInstruction))s.DeliveryInstruction=cue;
            else s.DeliveryInstruction=s.DeliveryInstruction.Trim().TrimEnd(';')+" "+cue;
        }
        private static void AddNative(TagStyle s,string tag)
        {
            if(string.IsNullOrWhiteSpace(tag))return;
            string token="["+tag.Trim()+"]";
            if(string.IsNullOrWhiteSpace(s.NativeTags))s.NativeTags=token;
            else if(s.NativeTags.IndexOf(token,StringComparison.OrdinalIgnoreCase)<0)s.NativeTags+=token;
        }
        private static void Reset(TagStyle s)
        {
            s.TemperatureDelta=0;s.SpeedMultiplier=1;s.PitchSemitones=0;s.VolumeDb=0;
            s.WhisperEffect=false;s.DeliveryInstruction="";s.NativeTags="";s.ExpressivenessDelta=0;
        }
        private static double Parse(string v,double f){double x;return double.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out x)?x:f;}
        private static double Clamp(double v,double a,double b){return Math.Max(a,Math.Min(b,v));}
    }
}
