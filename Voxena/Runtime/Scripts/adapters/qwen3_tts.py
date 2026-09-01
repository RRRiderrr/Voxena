from pathlib import Path
from dataclasses import asdict
from .common import log, snapshot, choose_device, write_wav, language_name, seed_everything, prepare_tts_text

BASE = 'Qwen/Qwen3-TTS-12Hz-1.7B-Base'
CUSTOM = 'Qwen/Qwen3-TTS-12Hz-1.7B-CustomVoice'
_MODEL_CACHE = {}


def _dirs(model_dir):
    return str(Path(model_dir) / 'Base'), str(Path(model_dir) / 'CustomVoice')


def _load(path, device):
    import torch
    from qwen_tts import Qwen3TTSModel
    target = choose_device(device)
    key = (str(path), target)
    if key in _MODEL_CACHE:
        return _MODEL_CACHE[key]
    dtype = torch.bfloat16 if target.startswith('cuda') else torch.float32
    kwargs = {'dtype': dtype}
    if target.startswith('cuda'):
        kwargs['device_map'] = 'cuda:0' if target == 'cuda' else target
    model = Qwen3TTSModel.from_pretrained(path, **kwargs)
    if target == 'cpu' and hasattr(model, 'model'):
        model.model.to('cpu')
    _MODEL_CACHE[key] = model
    return model


def doctor(**_):
    from qwen_tts import Qwen3TTSModel
    import transformers
    log('Qwen3-TTS runtime OK · transformers ' + getattr(transformers, '__version__', '?'))


def prepare(model_dir, **_):
    base, custom = _dirs(model_dir)
    snapshot(BASE, base)
    snapshot(CUSTOM, custom)
    log('Qwen3-TTS Base and CustomVoice are ready')


def clone(model_dir, audio, transcript, output, device='auto', **_):
    if not transcript or not transcript.strip():
        raise ValueError('Qwen3-TTS high-fidelity cloning requires an exact reference transcript.')
    import torch
    base, _ = _dirs(model_dir)
    model = _load(base, device)
    items = model.create_voice_clone_prompt(
        ref_audio=audio,
        ref_text=transcript.strip(),
        x_vector_only_mode=False,
    )
    payload = {'items': [asdict(item) for item in items]}
    Path(output).parent.mkdir(parents=True, exist_ok=True)
    torch.save(payload, output)
    log('Cached Qwen3-TTS high-fidelity voice-clone prompt')


def _load_prompt(path):
    import torch
    from qwen_tts import VoiceClonePromptItem
    payload = torch.load(path, map_location='cpu', weights_only=True)
    values = payload.get('items') if isinstance(payload, dict) else None
    if not values:
        raise ValueError('The cached Qwen3-TTS voice prompt is empty or incompatible.')
    return [VoiceClonePromptItem(**item) for item in values]


def _event_instruction(event, delivery=''):
    event=(event or '').strip().lower()
    if event == 'sigh':
        core = 'Produce one brief, clearly audible, natural human sigh first. Do not pronounce the placeholder as a word.'
    elif event == 'laugh':
        core = 'Produce one brief, clearly audible, natural human laugh first. Do not pronounce the placeholder as ordinary speech.'
    else:
        core = ''
    if delivery and core:
        return core + ' ' + delivery
    return core or (delivery or '')


def generate(model_dir, voice_kind, voice, preset_id, text, output, device='auto', temperature=0.8,
             delivery='', event='', seed=0, **_):
    seed_everything(seed)
    text = prepare_tts_text(text, 'qwen')
    base, custom = _dirs(model_dir)
    lang = language_name(text)
    if voice_kind == 'preset':
        model = _load(custom, device)
        instruct = _event_instruction(event, delivery)
        # For an event the text is only a carrier: instruct tells CustomVoice to vocalise
        # the nonverbal action rather than read it normally.
        if event:
            text = '…'
        wavs, sr = model.generate_custom_voice(
            text=text,
            speaker=preset_id,
            language=lang,
            instruct=instruct,
            temperature=float(temperature),
        )
    else:
        # Official Base voice cloning does not expose instruct control. Keep the already
        # segmented speed/volume/expression fallback from Voxena instead of pretending
        # an instruction was accepted. Event segments use a short natural interjection.
        model = _load(base, device)
        prompt = _load_prompt(voice)
        wavs, sr = model.generate_voice_clone(
            text=text,
            language=lang,
            voice_clone_prompt=prompt,
            temperature=float(temperature),
        )
    write_wav(output, wavs[0], sr)
