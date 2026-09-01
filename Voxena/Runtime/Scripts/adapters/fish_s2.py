from pathlib import Path
import shutil, sys
from .common import log, snapshot, choose_device, write_wav, seed_everything, prepare_tts_text
_ENGINE_CACHE = {}


def _setup(source_dir):
    if source_dir and source_dir not in sys.path:
        sys.path.insert(0, source_dir)


def doctor(source_dir=None, **_):
    _setup(source_dir)
    from fish_speech.inference_engine import TTSInferenceEngine
    from fish_speech.utils.schema import ServeTTSRequest, ServeReferenceAudio
    import transformers
    log('Fish Speech runtime OK · transformers ' + getattr(transformers, '__version__', '?'))

def prepare(model_dir, source_dir=None, **_):
    snapshot('fishaudio/s2-pro', model_dir)
    _setup(source_dir)
    if not (Path(model_dir) / 'codec.pth').exists():
        raise FileNotFoundError('Fish Speech S2 Pro codec.pth is missing from the downloaded checkpoint.')
    log('Fish Speech S2 Pro files are ready')


def clone(audio, transcript, output, **_):
    if not transcript or not transcript.strip():
        raise ValueError('Fish Speech requires an exact reference transcript.')
    out = Path(output)
    out.mkdir(parents=True, exist_ok=True)
    ext = Path(audio).suffix.lower() or '.wav'
    shutil.copy2(audio, out / ('reference' + ext))
    (out / 'reference.lab').write_text(transcript.strip(), encoding='utf-8')
    log('Prepared persistent Fish Speech reference')


def _make_engine(model_dir, source_dir, device):
    _setup(source_dir)
    import torch
    from fish_speech.inference_engine import TTSInferenceEngine
    from fish_speech.models.dac.inference import load_model as load_decoder_model
    from fish_speech.models.text2semantic.inference import launch_thread_safe_queue
    target = choose_device(device)
    key=(str(model_dir), str(source_dir or ''), target)
    if key in _ENGINE_CACHE:
        return _ENGINE_CACHE[key]
    precision = torch.bfloat16 if target.startswith('cuda') else torch.float32
    queue = launch_thread_safe_queue(checkpoint_path=Path(model_dir), device=target, precision=precision, compile=False)
    decoder = load_decoder_model(config_name='modded_dac_vq',checkpoint_path=Path(model_dir) / 'codec.pth',device=target)
    engine=TTSInferenceEngine(llama_queue=queue, decoder_model=decoder, compile=False, precision=precision)
    _ENGINE_CACHE[key]=engine
    return engine


def generate(model_dir, source_dir, voice_kind, voice, text, output, device='auto',
             temperature=0.8, native_tags='', event='', seed=0, **_):
    seed_everything(seed)
    text = prepare_tts_text(text, 'plain')
    prefix = (native_tags or '').strip()
    if event == 'sigh':
        text = '[sigh]'
    elif event == 'laugh':
        text = '[laughing]'
    if prefix:
        text = prefix + ' ' + text
    if voice_kind != 'custom':
        raise ValueError('Fish Speech requires a cloned reference voice in Voxena.')
    _setup(source_dir)
    from fish_speech.utils.schema import ServeTTSRequest, ServeReferenceAudio
    vdir = Path(voice)
    audio = next(
        (p for p in vdir.iterdir() if p.name.startswith('reference.') and p.suffix.lower() != '.lab'),
        None,
    )
    lab = vdir / 'reference.lab'
    if not audio or not lab.exists():
        raise FileNotFoundError('Prepared Fish Speech reference is incomplete.')
    ref = ServeReferenceAudio(audio=audio.read_bytes(), text=lab.read_text(encoding='utf-8'))
    engine = _make_engine(model_dir, source_dir, device)
    req = ServeTTSRequest(
        text=text,
        references=[ref],
        reference_id=None,
        max_new_tokens=2048,
        chunk_length=200,
        top_p=0.8,
        repetition_penalty=1.1,
        temperature=max(0.7, min(1.0, float(temperature))),
        seed=(int(seed) or None),
        use_memory_cache='on',
        format='wav',
    )
    final = None
    for result in engine.inference(req):
        if result.code == 'final':
            final = result.audio
        elif result.code == 'error':
            raise RuntimeError(str(result.error))
    if final is None:
        raise RuntimeError('Fish Speech returned no audio.')
    if isinstance(final, (bytes, bytearray)):
        Path(output).write_bytes(final)
        return
    if isinstance(final, tuple) and len(final) == 2:
        a, b = final
        if isinstance(a, (int, float)):
            sr, wav = int(a), b
        else:
            wav, sr = a, int(b)
        write_wav(output, wav, sr)
        return
    raise RuntimeError('Unsupported Fish Speech audio result type: ' + type(final).__name__)
