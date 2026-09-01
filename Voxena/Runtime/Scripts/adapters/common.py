from __future__ import annotations
import json, os, re, shutil
from pathlib import Path
import numpy as np

CYRILLIC_RE = re.compile(r"[А-Яа-яЁёІіЇїЄєҐґ]")

def log(text: str):
    print(f"VOXENA: {text}", flush=True)

def choose_device(requested: str | None = None):
    requested=(requested or 'auto').strip().lower()
    import torch
    if requested not in ('','auto'):
        if requested.startswith('cuda') and torch.cuda.is_available(): return requested
        if requested=='cpu': return 'cpu'
    return 'cuda' if torch.cuda.is_available() else 'cpu'


def seed_everything(seed=0):
    try:
        value = int(seed or 0)
    except Exception:
        value = 0
    if value == 0:
        value = int.from_bytes(os.urandom(4), 'little') & 0x7fffffff
        if value == 0:
            value = 1
    import random
    random.seed(value)
    np.random.seed(value & 0xffffffff)
    try:
        import torch
        torch.manual_seed(value)
        if torch.cuda.is_available():
            torch.cuda.manual_seed_all(value)
    except Exception:
        pass
    return value

def language_name(text: str):
    return 'Russian' if CYRILLIC_RE.search(text or '') else 'English'

def language_code(text: str):
    return 'ru' if CYRILLIC_RE.search(text or '') else 'en'

def snapshot(repo_id: str, model_dir: str, allow_patterns=None):
    from huggingface_hub import snapshot_download
    Path(model_dir).mkdir(parents=True, exist_ok=True)
    log(f"Downloading {repo_id}")
    kwargs=dict(repo_id=repo_id, local_dir=model_dir, allow_patterns=allow_patterns)
    try:
        return snapshot_download(local_dir_use_symlinks=False, **kwargs)
    except TypeError:
        # Newer huggingface_hub releases removed local_dir_use_symlinks.
        return snapshot_download(**kwargs)

def write_wav(path: str, wav, sr: int):
    import soundfile as sf
    arr = wav
    try:
        import torch
        if torch.is_tensor(arr): arr = arr.detach().float().cpu().numpy()
    except Exception:
        pass
    arr=np.asarray(arr)
    while arr.ndim>1: arr=arr[0]
    sf.write(path, arr.astype(np.float32), int(sr))

def normalize_reference(src: str, dst: str, sample_rate=24000):
    import librosa, soundfile as sf
    y,_=librosa.load(src, sr=sample_rate, mono=True)
    if y.size==0: raise ValueError('Reference audio is empty.')
    peak=float(np.max(np.abs(y)))
    if peak>1e-6: y=np.clip(y/peak*0.92,-1,1)
    Path(dst).parent.mkdir(parents=True,exist_ok=True)
    sf.write(dst,y,sample_rate)
    return dst


MANUAL_STRESS_MARK = '\ue000'
COMBINING_STRESS_MARKS = ('\u0301', '\u0341')


def prepare_tts_text(text: str, stress_style: str = 'plain'):
    """Translate Voxena's private *manual* stress marker for one engine.

    Automatic Gemma stress never reaches this function as capitalization or punctuation;
    it has already served its purpose during phonetic preprocessing. This prevents global
    prosody/emphasis artefacts. Only an explicit user stress can create a model hint.
    """
    text = text or ''
    # Defensive cleanup for files made by older versions.
    for mark in COMBINING_STRESS_MARKS:
        text = text.replace(mark, '')

    out = []
    for ch in text:
        if ch != MANUAL_STRESS_MARK:
            out.append(ch)
            continue
        if not out:
            continue
        if stress_style == 'qwen':
            # Qwen3-TTS Russian users report U+02CA (modifier acute) as the least
            # destructive direct stress hint. Crucially we emit it ONLY for a manual
            # word, not for every automatically stressed word.
            out.append('\u02ca')
        elif stress_style == 'f5':
            # The Hotstone Russian F5 community checkpoint has a best-effort @ stress
            # convention. Again, use it only when the user explicitly requested stress.
            out.append('@')
        # plain: models without a reliable stress syntax receive the already-phonetic
        # spelling, but no unknown marker that could create audible garbage.
    return ''.join(out)

def save_json(path, value):
    Path(path).parent.mkdir(parents=True,exist_ok=True)
    Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
