from pathlib import Path
import sys
from .common import log, snapshot, write_wav, choose_device, seed_everything, prepare_tts_text

MODEL_REPO = 'FunAudioLLM/Fun-CosyVoice3-0.5B-2512'
PROMPT_PREFIX = 'You are a helpful assistant.<|endofprompt|>'
_MODEL_CACHE = {}


def _setup_source(source_dir):
    if source_dir and source_dir not in sys.path:
        sys.path.insert(0, source_dir)
    third = Path(source_dir) / 'third_party' / 'Matcha-TTS' if source_dir else None
    if third and third.exists() and str(third) not in sys.path:
        sys.path.insert(0, str(third))


def _load(model_dir, source_dir, device):
    _setup_source(source_dir)
    import torch
    from cosyvoice.cli.cosyvoice import CosyVoice3
    target = choose_device(device)
    key=(str(model_dir), str(source_dir or ''), target)
    if key not in _MODEL_CACHE:
        _MODEL_CACHE[key]=CosyVoice3(model_dir,load_trt=False,load_vllm=False,fp16=(target.startswith('cuda') and torch.cuda.is_available()))
    return _MODEL_CACHE[key]


def _prompt_text(transcript):
    value = (transcript or '').strip()
    if not value:
        raise ValueError('CosyVoice 3 requires an exact reference transcript.')
    # CosyVoice 3 requires a hard instruction/content separator before the
    # reference transcript. The cached speaker entry stores the tokenized form.
    return PROMPT_PREFIX + value


def doctor(source_dir=None, **_):
    _setup_source(source_dir)
    from cosyvoice.cli.cosyvoice import CosyVoice3
    import transformers
    log('CosyVoice runtime OK · transformers ' + getattr(transformers, '__version__', '?'))

def prepare(model_dir, source_dir=None, device='auto', **_):
    # Download only files used by the standard high-quality inference path.
    # This intentionally skips the duplicate batch tokenizer and RL checkpoint.
    snapshot(
        MODEL_REPO,
        model_dir,
        allow_patterns=[
            'CosyVoice-BlankEN/**',
            'campplus.onnx', 'configuration.json', 'config.json', 'cosyvoice3.yaml',
            'flow.pt', 'hift.pt', 'llm.pt', 'speech_tokenizer_v3.onnx', 'spk2info.pt',
        ],
    )
    required = [
        'campplus.onnx', 'cosyvoice3.yaml', 'flow.pt', 'hift.pt', 'llm.pt',
        'speech_tokenizer_v3.onnx', 'CosyVoice-BlankEN/model.safetensors'
    ]
    missing = [name for name in required if not (Path(model_dir) / name).exists()]
    if missing:
        raise FileNotFoundError('Incomplete CosyVoice 3 download: ' + ', '.join(missing))
    _load(model_dir, source_dir, device)
    log('CosyVoice 3 is ready')


def clone(model_dir, source_dir, audio, transcript, output, device='auto', **_):
    import torch
    model = _load(model_dir, source_dir, device)
    key = 'voxena_cached_voice'
    if model.add_zero_shot_spk(_prompt_text(transcript), audio, key) is not True:
        raise RuntimeError('CosyVoice 3 failed to prepare the reference voice.')
    item = model.frontend.spk2info[key]
    cpu_item = {k: (v.detach().cpu() if hasattr(v, 'detach') else v) for k, v in item.items()}
    Path(output).parent.mkdir(parents=True, exist_ok=True)
    torch.save(cpu_item, output)
    log('Cached CosyVoice 3 speaker features')


def generate(model_dir, source_dir, voice_kind, voice, text, output, device='auto', speed=1.0, seed=0, **_):
    seed_everything(seed)
    text = prepare_tts_text(text, 'plain')
    if voice_kind != 'custom':
        raise ValueError('CosyVoice 3 uses cloned/reference voices in Voxena.')
    import torch
    model = _load(model_dir, source_dir, device)
    key = 'voxena_cached_voice'
    map_location = choose_device(device)
    cached = torch.load(voice, map_location=map_location, weights_only=True)
    model.frontend.spk2info[key] = cached
    chunks = []
    # The cached speaker entry already carries the exact reference prompt text
    # and acoustic features, so do not re-tokenize/re-encode the original audio.
    for item in model.inference_zero_shot(
        text, '', '', zero_shot_spk_id=key, stream=False, speed=float(speed)
    ):
        chunks.append(item['tts_speech'].detach().cpu())
    if not chunks:
        raise RuntimeError('CosyVoice 3 returned no audio.')
    wav = torch.cat(chunks, dim=1)
    write_wav(output, wav, model.sample_rate)
