using System.Collections.Generic;
using System.Linq;
using Voxena.Models;

namespace Voxena.Services
{
    internal static class ModelCatalog
    {
        public const string StressModelId = "gemma4-stress";

        public static List<ModelProfile> Create()
        {
            return new List<ModelProfile>
            {
                new ModelProfile {
                    Id="cosyvoice3", Name="CosyVoice 3", VersionName="Fun-CosyVoice3-0.5B-2512", Repository="FunAudioLLM/Fun-CosyVoice3-0.5B-2512",
                    Description="Natural multilingual synthesis with strong zero-shot speaker similarity and prosody.",
                    Advantages="Very natural rhythm; strong speaker similarity; good multilingual cloning.",
                    Disadvantages="Large download and a heavier runtime; first setup takes longer.", DiskSize="~5.5 GB + runtime", ApproxBytes=5500000000L,
                    Vram="8–12 GB recommended", RecommendedVramMb=10000, License="Apache-2.0", Languages="9 languages including Russian",
                    CloneTranscriptRequired=true, ReferenceRecommendation="5–15 s of one clean speaker. Exact transcript is required; avoid music, reverb and overlapping speech.",
                    PythonVersion="3.10", PreparedExtension=".pt", RuntimeNote="Windows-native runtime; speech-token extraction may use CPU even when synthesis uses the GPU.",
                    UpstreamUrl="https://github.com/FunAudioLLM/CosyVoice",
                    Packages=new List<string>{"huggingface_hub>=0.34","conformer==0.3.2","diffusers==0.29.0","gdown==5.1.0","grpcio>=1.57","grpcio-tools>=1.57","hydra-core==1.3.2","HyperPyYAML==1.2.3","inflect==7.3.1","librosa==0.10.2","lightning==2.2.4","modelscope>=1.20","networkx>=3.1","numpy==1.26.4","omegaconf==2.3.0","onnx==1.16.0","onnxruntime>=1.18","openai-whisper==20231117","protobuf>=4.25,<6","pyarrow>=18","rich>=13.7","soundfile>=0.12","torch==2.3.1","torchaudio==2.3.1","transformers==4.51.3","x-transformers==2.11.24","wetext==0.0.4","wget>=3.2"}
                },
                new ModelProfile {
                    Id="fish-s2-pro", Name="Fish Speech", VersionName="Fish Audio S2 Pro 4B", Repository="fishaudio/s2-pro",
                    Description="Flagship high-fidelity multilingual model focused on expressive, realistic speech.",
                    Advantages="Excellent realism and emotional range; 80+ languages; native fine-grained delivery tags.",
                    Disadvantages="Very heavy; full-quality inference is aimed at high-memory GPUs. Windows runs without torch.compile acceleration.", DiskSize="~11 GB + runtime", ApproxBytes=11000000000L,
                    Vram="24 GB recommended", RecommendedVramMb=24000, License="Fish Audio Research License", Languages="83 languages",
                    CloneTranscriptRequired=true, ReferenceRecommendation="5–10 s is the upstream sweet spot. Exact transcript is required. Use one dry speaker with minimal silence, noise and room reverb.",
                    PythonVersion="3.12", PreparedExtension=".dir", RuntimeNote="Windows-native setup uses the upstream Python runtime with torch.compile disabled, as required on Windows.",
                    UpstreamUrl="https://github.com/fishaudio/fish-speech",
                    Packages=new List<string>{"numpy","transformers==4.57.3","datasets==2.18.0","lightning>=2.1.0","hydra-core>=1.3.2","natsort>=8.4.0","einops>=0.7.0","librosa>=0.10.1","rich>=13.5.3","grpcio>=1.58.0","kui>=1.6.0","uvicorn>=0.30.0","loguru>=0.6.0","loralib>=0.1.2","pyrootutils>=1.0.4","resampy>=0.4.3","einx[torch]==0.2.2","zstandard>=0.22.0","pydub","modelscope==1.17.1","opencc-python-reimplemented==0.1.7","silero-vad","ormsgpack","tiktoken>=0.8.0","pydantic==2.9.2","cachetools","descript-audio-codec","safetensors"}
                },
                new ModelProfile {
                    Id="xtts-v2", Name="XTTS v2", VersionName="coqui/XTTS-v2", Repository="coqui/XTTS-v2",
                    Description="Mature multilingual cloning model with a small footprint and fast setup.",
                    Advantages="Lightweight; proven Russian support; good cloning from short references; lower VRAM demand.",
                    Disadvantages="Older 24 kHz architecture; less expressive and less detailed than newer flagship models.", DiskSize="~2.1 GB + runtime", ApproxBytes=2090000000L,
                    Vram="6–8 GB recommended", RecommendedVramMb=7000, License="Coqui Public Model License", Languages="17 languages including Russian",
                    CloneTranscriptRequired=false, ReferenceRecommendation="6–20 s of clean speech. Transcript is not required. Multiple clean references can improve identity, but one clip is enough.",
                    PythonVersion="3.10", PreparedExtension=".pt", RuntimeNote="Best compatibility option for modest GPUs.", UpstreamUrl="https://github.com/coqui-ai/TTS",
                    Packages=new List<string>{"coqui-tts==0.27.5","transformers==4.57.6","huggingface_hub>=0.34","soundfile>=0.12"}
                },
                new ModelProfile {
                    Id="f5-tts-ru", Name="F5-TTS", VersionName="F5-TTS Base · Russian high-quality fine-tune", Repository="hotstone228/F5-TTS-Russian",
                    Description="Flow-matching voice cloning using a dedicated Russian/English high-quality checkpoint.",
                    Advantages="Strong naturalness; excellent similarity when reference text is exact; comparatively compact checkpoint.",
                    Disadvantages="Reference transcript quality matters a lot; the selected Russian checkpoint has a non-commercial share-alike license.", DiskSize="~1.4 GB + vocoder/runtime", ApproxBytes=1450000000L,
                    Vram="8 GB recommended", RecommendedVramMb=8000, License="CC BY-NC-SA 4.0", Languages="Russian + English",
                    CloneTranscriptRequired=true, ReferenceRecommendation="8–20 s of clean speech. Exact transcript is required to avoid ASR overhead and improve alignment.",
                    PythonVersion="3.10", PreparedExtension=".wav", RuntimeNote="Voxena caches a normalized reference WAV; the model conditions directly on the reference at render time.",
                    UpstreamUrl="https://github.com/SWivid/F5-TTS", Packages=new List<string>{"f5-tts==1.1.22","huggingface_hub>=0.34","soundfile>=0.12"}
                },
                new ModelProfile {
                    Id="qwen3-tts", Name="Qwen3-TTS", VersionName="12Hz 1.7B Base + 1.7B CustomVoice", Repository="Qwen/Qwen3-TTS-12Hz-1.7B-Base", SecondaryRepository="Qwen/Qwen3-TTS-12Hz-1.7B-CustomVoice",
                    Description="Top-quality 1.7B bundle: high-fidelity voice cloning plus nine built-in premium timbres.",
                    Advantages="Very strong 3-second cloning; reusable clone prompts; Russian support; built-in voices and style instructions.",
                    Disadvantages="Two 1.7B checkpoints are downloaded so the full preset + cloning feature set takes more disk space.", DiskSize="~9.1 GB + runtime", ApproxBytes=9100000000L,
                    Vram="8–12 GB recommended", RecommendedVramMb=10000, License="Apache-2.0", Languages="10 languages including Russian",
                    CloneTranscriptRequired=true, ReferenceRecommendation="3–12 s of clean speech. Exact transcript is required because Voxena uses the highest-fidelity ICL clone prompt instead of x-vector-only cloning.",
                    PythonVersion="3.10", PreparedExtension=".pt", RuntimeNote="Clone prompt is computed once and cached locally for reuse.", UpstreamUrl="https://github.com/QwenLM/Qwen3-TTS",
                    Packages=new List<string>{"qwen-tts==0.1.1","transformers==4.57.3","accelerate==1.12.0","huggingface_hub>=0.34","soundfile>=0.12"},
                    PresetVoices=new List<string>{"Vivian","Serena","Uncle_Fu","Dylan","Eric","Ryan","Aiden","Ono_Anna","Sohee"}
                },
                new ModelProfile {
                    Id="chatterbox-v3", Name="Chatterbox", VersionName="Multilingual V3 500M", Repository="ResembleAI/chatterbox",
                    Description="Modern multilingual zero-shot model with natural conversational delivery and strong speaker similarity.",
                    Advantages="Good Russian; stable cloning; expressive control; relatively small model; built-in default voice.",
                    Disadvantages="Watermarks generated speech by design; some references need tuning of exaggeration/CFG.", DiskSize="~3.3 GB + runtime", ApproxBytes=3300000000L,
                    Vram="6–8 GB recommended", RecommendedVramMb=7000, License="MIT", Languages="23 languages including Russian",
                    CloneTranscriptRequired=false, ReferenceRecommendation="6–12 s of clean speech is ideal. Transcript is not required; avoid background music and strong room reverb.",
                    PythonVersion="3.11", PreparedExtension=".pt", RuntimeNote="Voxena explicitly uses the current multilingual V3 checkpoint. Speaker conditionals are computed once and saved with the cloned voice.", UpstreamUrl="https://github.com/resemble-ai/chatterbox",
                    Packages=new List<string>{"numpy>=1.24,<2","librosa==0.11.0","s3tokenizer","transformers==5.2.0","diffusers==0.29.0","resemble-perth==1.0.1","conformer==0.3.2","safetensors==0.5.3","spacy-pkuseg","pykakasi==2.3.0","pyloudnorm","omegaconf","huggingface_hub>=0.34","soundfile>=0.12"}, PresetVoices=new List<string>{"Default"}
                }
            };
        }

        public static ModelProfile Get(string id) { return Create().FirstOrDefault(x => x.Id == id); }
    }
}
