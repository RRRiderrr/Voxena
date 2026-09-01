from pathlib import Path
from .common import log, snapshot, choose_device, write_wav, language_code, seed_everything, prepare_tts_text

V3_FILE = 't3_mtl23ls_v3.safetensors'
_MODEL_CACHE = {}


def _load(model_dir, device):
    from chatterbox.mtl_tts import ChatterboxMultilingualTTS
    target = choose_device(device)
    key = (str(model_dir), target)
    if key not in _MODEL_CACHE:
        _MODEL_CACHE[key] = ChatterboxMultilingualTTS.from_local(model_dir, device=target, t3_model='v3')
    return _MODEL_CACHE[key]


def doctor(**_):
    from chatterbox.mtl_tts import ChatterboxMultilingualTTS, Conditionals
    import transformers
    log('Chatterbox runtime OK · transformers ' + getattr(transformers, '__version__', '?'))

def prepare(model_dir, **_):
    snapshot(
        'ResembleAI/chatterbox',
        model_dir,
        allow_patterns=[
            've.pt', V3_FILE, 's3gen.pt',
            'grapheme_mtl_merged_expanded_v1.json',
            'conds.pt', 'Cangjie5_TC.json'
        ],
    )
    required = ['ve.pt', V3_FILE, 's3gen.pt', 'grapheme_mtl_merged_expanded_v1.json']
    missing = [name for name in required if not (Path(model_dir) / name).exists()]
    if missing:
        raise FileNotFoundError('Incomplete Chatterbox V3 download: ' + ', '.join(missing))
    _load(model_dir, 'cpu')
    log('Chatterbox Multilingual V3 is ready')


def clone(model_dir, audio, output, device='auto', expressiveness=0.5, **_):
    model = _load(model_dir, device)
    model.prepare_conditionals(audio, exaggeration=max(0.0, min(1.0, float(expressiveness))))
    if model.conds is None:
        raise RuntimeError('Chatterbox did not create speaker conditionals from the reference audio.')
    Path(output).parent.mkdir(parents=True, exist_ok=True)
    model.conds.save(Path(output))
    log('Cached Chatterbox V3 speaker conditionals')


def generate(model_dir, voice_kind, voice, text, output, device='auto', temperature=0.8,
             expressiveness=0.5, stability=0.55, event='', seed=0, **_):
    seed_everything(seed)
    text = prepare_tts_text(text, 'plain')
    # Multilingual V3 has no reliable inline paralinguistic API; use a short voiced
    # fallback for event-only segments while semantic emotion is driven by exaggeration.
    if event == 'laugh':
        text = 'Ха-ха!' if language_code(text) == 'ru' else 'Ha-ha!'
    elif event == 'sigh':
        text = 'Ах...' if language_code(text) == 'ru' else 'Ah...'
    from chatterbox.mtl_tts import Conditionals
    target = choose_device(device)
    model = _load(model_dir, device)
    if voice_kind == 'custom':
        model.conds = Conditionals.load(Path(voice), map_location=target).to(target)
    # Built-in preset keeps the bundled conditionals.
    cfg = max(0.15, min(0.9, 0.35 + float(stability) * 0.35))
    wav = model.generate(
        text=text,
        language_id=language_code(text),
        exaggeration=max(0.0, min(1.0, float(expressiveness))),
        cfg_weight=cfg,
        temperature=max(0.05, min(2.0, float(temperature))),
    )
    write_wav(output, wav, model.sr)
