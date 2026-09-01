from pathlib import Path
from .common import log, snapshot, choose_device, write_wav, language_code, seed_everything, prepare_tts_text
_MODEL_CACHE = {}

def _load(model_dir, device):
    import torch
    from TTS.tts.configs.xtts_config import XttsConfig
    from TTS.tts.models.xtts import Xtts
    target=choose_device(device)
    key=(str(model_dir), target)
    if key in _MODEL_CACHE:
        return _MODEL_CACHE[key]
    config=XttsConfig(); config.load_json(str(Path(model_dir)/'config.json'))
    model=Xtts.init_from_config(config)
    model.load_checkpoint(config, checkpoint_dir=model_dir, use_deepspeed=False)
    model.to(target)
    _MODEL_CACHE[key]=(model,config)
    return model, config

def doctor(**_):
    from TTS.tts.configs.xtts_config import XttsConfig
    from TTS.tts.models.xtts import Xtts
    import transformers
    log('XTTS runtime OK · transformers ' + getattr(transformers, '__version__', '?'))

def prepare(model_dir, **_):
    snapshot('coqui/XTTS-v2',model_dir)
    log('XTTS v2 files are ready')

def clone(model_dir, audio, output, device='auto', **_):
    import torch
    model,_=_load(model_dir,device)
    gpt,speaker=model.get_conditioning_latents(audio_path=[audio])
    Path(output).parent.mkdir(parents=True,exist_ok=True)
    torch.save({'gpt':gpt.detach().cpu(),'speaker':speaker.detach().cpu()},output)
    log('Cached XTTS speaker latents')

def generate(model_dir, voice_kind, voice, text, output, device='auto', temperature=0.75, speed=1.0, seed=0, **_):
    seed_everything(seed)
    text = prepare_tts_text(text, 'plain')
    if voice_kind!='custom': raise ValueError('XTTS v2 requires a cloned voice in Voxena.')
    import torch
    model,config=_load(model_dir,device)
    target=choose_device(device)
    data=torch.load(voice,map_location=target,weights_only=True)
    out=model.inference(text=text, language=language_code(text), gpt_cond_latent=data['gpt'].to(target), speaker_embedding=data['speaker'].to(target),
                        temperature=float(temperature), speed=float(speed))
    write_wav(output,out['wav'],24000)
