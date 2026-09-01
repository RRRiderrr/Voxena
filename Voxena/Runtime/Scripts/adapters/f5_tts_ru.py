from pathlib import Path
from .common import log, snapshot, normalize_reference, choose_device, seed_everything, prepare_tts_text
_TTS_CACHE = {}

def _files(model_dir):
    p=Path(model_dir)
    ckpt=next(iter(p.glob('*.safetensors')),None)
    vocab=next(iter(p.glob('vocab*.txt')),None) or next(iter(p.glob('*.txt')),None)
    if not ckpt: raise FileNotFoundError('Russian F5 checkpoint was not found.')
    if not vocab: raise FileNotFoundError('Russian F5 vocabulary was not found.')
    return str(ckpt),str(vocab)

def doctor(**_):
    from f5_tts.api import F5TTS
    log('F5-TTS runtime OK')

def prepare(model_dir, **_):
    snapshot('hotstone228/F5-TTS-Russian',model_dir,allow_patterns=['*.safetensors','vocab.txt','*.json','*.yaml'])
    _files(model_dir)
    log('F5-TTS Russian checkpoint is ready')

def clone(audio, output, transcript, **_):
    if not transcript or not transcript.strip(): raise ValueError('F5-TTS requires an exact reference transcript.')
    normalize_reference(audio,output,24000)
    log('Cached normalized F5 reference audio')

def generate(model_dir, voice_kind, voice, transcript, text, output, device='auto', speed=1.0, seed=0, **_):
    seed_everything(seed)
    text = prepare_tts_text(text, 'f5')
    if voice_kind!='custom': raise ValueError('F5-TTS requires a cloned voice in Voxena.')
    if not transcript or not transcript.strip(): raise ValueError('F5-TTS requires the reference transcript.')
    from f5_tts.api import F5TTS
    ckpt,vocab=_files(model_dir)
    target=choose_device(device)
    key=(str(model_dir), target)
    if key not in _TTS_CACHE:
        _TTS_CACHE[key]=F5TTS(model='F5TTS_Base',ckpt_file=ckpt,vocab_file=vocab,device=target,hf_cache_dir=str(Path(model_dir)/'_cache'))
    tts=_TTS_CACHE[key]
    tts.infer(ref_file=voice,ref_text=transcript.strip(),gen_text=text,file_wave=output,speed=float(speed),seed=(int(seed) or None),remove_silence=False)
