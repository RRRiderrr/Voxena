from __future__ import annotations
import argparse
import json
import re
from pathlib import Path

MODEL = 'google/gemma-4-E2B-it-qat-q4_0-unquantized-assistant'
CYR = re.compile(r'[А-Яа-яЁё]')
WORD_RE = re.compile(r'[А-Яа-яЁё]+')
VOWELS = set('аеёиоуыэюяАЕЁИОУЫЭЮЯ')
LOWER_VOWELS = set('аеёиоуыэюя')
ACUTES = {'\u0301', '\u0341'}
MANUAL_TRANSPORT = '\ue000'  # private internal marker, never sent to a model verbatim


def log(text):
    print('VOXENA: ' + text, flush=True)


def _strip_manual_marks(text):
    """Remove combining acute accents while remembering their exact vowel indices."""
    out = []
    manual = set()
    for ch in text:
        if ch in ACUTES:
            if out and out[-1] in VOWELS:
                out[-1] = out[-1].upper()
                manual.add(len(out) - 1)
            continue
        out.append(ch)
    return ''.join(out), manual


def _prepare_for_stress(text):
    """
    Build an internal text where uppercase Russian vowels are reserved for stress.
    User-marked words are locked. Words containing ё are also deterministic because
    ё carries lexical stress in normal Russian spelling. All-caps acronyms are locked
    so they are never mistaken for stress markup.
    """
    cased, manual = _strip_manual_marks(text)
    chars = list(cased)
    locked = []
    acronym = []
    known_stress = set(manual)

    for m in WORD_RE.finditer(cased):
        a, b = m.span()
        has_manual = any(a <= i < b for i in manual)
        word = cased[a:b]
        is_acronym = (not has_manual and len(word) > 1 and word.isupper())
        if is_acronym:
            locked.append((a, b))
            acronym.append((a, b))
            continue

        # Neutralise ordinary vowel casing. This prevents a sentence-initial О/А/Е
        # from being confused with the internal uppercase-stress convention.
        for i in range(a, b):
            if chars[i] in VOWELS and i not in manual:
                chars[i] = chars[i].lower()

        if has_manual:
            locked.append((a, b))
            for i in manual:
                if a <= i < b:
                    chars[i] = chars[i].upper()
            continue

        # ё is intrinsically stressed for the pronunciation path. Do not ask Gemma
        # to add a second stressed vowel to the same word.
        yo = [i for i in range(a, b) if chars[i] == 'ё']
        if yo:
            for i in yo:
                chars[i] = 'Ё'
                known_stress.add(i)
            locked.append((a, b))

    return ''.join(chars), locked, acronym, known_stress, set(manual)


def _validate_stress(src, out, locked, known_stress):
    """Validate per word so one bad Gemma word cannot destroy correct marks elsewhere."""
    if not out or len(src) != len(out) or src.lower() != out.lower():
        return src, set(known_stress)

    result = list(out)
    stress = set(known_stress)
    locked_pos = set()
    for a, b in locked:
        locked_pos.update(range(a, b))
        result[a:b] = src[a:b]

    # Globally reject anything except lower-vowel -> same uppercase-vowel changes.
    for i, (a, b) in enumerate(zip(src, result)):
        if i in locked_pos or a == b:
            continue
        if not (a in LOWER_VOWELS and b == a.upper()):
            result[i] = a

    # Enforce at most one automatic stress mark in each unlocked Russian word.
    for m in WORD_RE.finditer(src):
        a, b = m.span()
        if any(a <= x < b for x in locked_pos):
            continue
        added = [i for i in range(a, b) if src[i] in LOWER_VOWELS and result[i] == src[i].upper()]
        if len(added) == 1:
            stress.add(added[0])
        elif len(added) > 1:
            result[a:b] = src[a:b]

    return ''.join(result), stress


def _preserve_stress_case(new_char, position, stress):
    return new_char.upper() if position in stress else new_char.lower()


def _phonetic_normalize(text, stress, acronym_spans, manual_stress):
    """
    Phase 3, after Gemma has finished all stress placement.

    Important: automatic stress is useful as linguistic context, but broad respelling of
    every automatically stressed word made normal speech less natural.  Therefore the
    aggressive grapheme workaround (unstressed о -> а) is used only in words whose
    stress the USER explicitly fixed, plus a very small set of known troublesome forms.
    This keeps normal text orthographic while still making a manual correction such as
    война́ -> вайнА deterministic.
    """
    chars = list(text)
    acronym_pos = set()
    for a, b in acronym_spans:
        acronym_pos.update(range(a, b))

    for m in WORD_RE.finditer(text):
        a, b = m.span()
        if any(a <= x < b for x in acronym_pos):
            continue
        word_stress = [i for i in stress if a <= i < b]
        manual_in_word = any(a <= i < b for i in manual_stress)

        # Russian что/чтобы/чтоб are robustly pronounced with [ш].
        low = ''.join(chars[a:b]).lower()
        if low.startswith('что') and b - a >= 3:
            chars[a] = 'Ш' if chars[a].isupper() else 'ш'
            low = ''.join(chars[a:b]).lower()

        # Do not rewrite every unstressed о in ordinary text. TTS models are trained on
        # normal Russian orthography and broad respelling caused audible artefacts.
        # Use the workaround when the user explicitly chose a stress in this word, or for
        # the known войн-* family that motivated the rule in the first place.
        force_akanye = manual_in_word or low.startswith('войн')
        if force_akanye and len(word_stress) == 1:
            stressed = word_stress[0]
            for i in range(a, b):
                if i == stressed:
                    continue
                if chars[i].lower() == 'о':
                    chars[i] = _preserve_stress_case('а', i, stress)

    return ''.join(chars)


def _encode_for_tts(text, manual_stress, acronym_spans):
    """
    Phase 4: internal uppercase vowels MUST NOT leak into the speech model.

    Gemma uses uppercase vowels only as Voxena's private stress representation.  Once
    phonetic normalization is complete we lowercase those service marks again.  Only
    USER stresses survive as a private U+E000 marker placed after the chosen vowel.
    engine adapters translate that marker to their own best-supported syntax.
    """
    acronym_pos = set()
    for a, b in acronym_spans:
        acronym_pos.update(range(a, b))

    out = []
    for i, ch in enumerate(text):
        c = ch
        if i not in acronym_pos and c in VOWELS:
            c = c.lower()
        out.append(c)
        if i in manual_stress:
            out.append(MANUAL_TRANSPORT)
    return ''.join(out)


def snapshot(repo_id, model_dir):
    from huggingface_hub import snapshot_download
    kwargs = dict(repo_id=repo_id, local_dir=model_dir)
    try:
        return snapshot_download(local_dir_use_symlinks=False, **kwargs)
    except TypeError:
        return snapshot_download(**kwargs)


def prepare(model_dir):
    snapshot(MODEL, model_dir)
    log('Russian stress helper is ready')


def _prompt(tokenizer, text):
    instruction = (
        'Return exactly the same Russian text character-for-character. '
        'Your only allowed change is to uppercase ONE stressed vowel in each Russian word '
        'when you are confident about lexical stress. Do not add, delete, replace or reorder '
        'characters. Preserve punctuation, spaces, numbers, line breaks and markup. '
        'IMPORTANT: a word that already contains an uppercase Russian vowel is LOCKED because '
        'that stress was supplied by the user or is deterministic. Do not change ANY character '
        'inside such a locked word and do not add another uppercase vowel to it. '
        'Output only transformed text, with no explanation.\n\nTEXT:\n' + text
    )
    if hasattr(tokenizer, 'apply_chat_template'):
        try:
            return tokenizer.apply_chat_template(
                [{'role': 'user', 'content': instruction}],
                tokenize=False,
                add_generation_prompt=True,
            )
        except Exception:
            pass
    return instruction


def _load_model(model_dir):
    import torch
    from transformers import AutoTokenizer, AutoModelForCausalLM
    tok = AutoTokenizer.from_pretrained(model_dir, local_files_only=True)
    device = 'cuda' if torch.cuda.is_available() else 'cpu'
    dtype = torch.bfloat16 if device == 'cuda' else torch.float32
    kwargs = dict(local_files_only=True)
    try:
        model = AutoModelForCausalLM.from_pretrained(model_dir, dtype=dtype, **kwargs)
    except TypeError:
        model = AutoModelForCausalLM.from_pretrained(model_dir, torch_dtype=dtype, **kwargs)
    return tok, model.to(device).eval(), device, torch


def _transform_loaded(text, tok, model, device, torch):
    if not CYR.search(text):
        clean, _ = _strip_manual_marks(text)
        return clean
    prepared, locked, acronym, known_stress, manual_stress = _prepare_for_stress(text)
    prompt = _prompt(tok, prepared)
    data = tok(prompt, return_tensors='pt').to(device)
    with torch.inference_mode():
        ids = model.generate(
            **data,
            max_new_tokens=max(64, min(4096, len(prepared) * 3)),
            do_sample=False,
            use_cache=True,
        )
    out = tok.decode(ids[0][data['input_ids'].shape[1]:], skip_special_tokens=True).strip('\r\n')
    stressed, stress = _validate_stress(prepared, out, locked, known_stress)
    phonetic = _phonetic_normalize(stressed, stress, acronym, manual_stress)
    return _encode_for_tts(phonetic, manual_stress, acronym)


def transform(model_dir, text):
    tok, model, device, torch = _load_model(model_dir)
    return _transform_loaded(text, tok, model, device, torch)


def transform_batch(model_dir, texts):
    if not any(CYR.search(x or '') for x in texts):
        return [_strip_manual_marks(x or '')[0] for x in texts]
    tok, model, device, torch = _load_model(model_dir)
    out = []
    for i, text in enumerate(texts):
        log(f'Stress segment {i + 1}/{len(texts)}')
        out.append(_transform_loaded(text or '', tok, model, device, torch))
    return out


def main():
    p = argparse.ArgumentParser()
    p.add_argument('command', choices=['prepare', 'transform', 'transform-batch'])
    p.add_argument('--model-dir', required=True)
    p.add_argument('--input')
    p.add_argument('--output')
    a = p.parse_args()
    if a.command == 'prepare':
        prepare(a.model_dir)
        return
    if a.command == 'transform-batch':
        texts = json.loads(Path(a.input).read_text(encoding='utf-8'))
        if not isinstance(texts, list):
            raise ValueError('Stress batch input must be a JSON array.')
        Path(a.output).write_text(json.dumps(transform_batch(a.model_dir, texts), ensure_ascii=False), encoding='utf-8')
        return
    text = Path(a.input).read_text(encoding='utf-8')
    Path(a.output).write_text(transform(a.model_dir, text), encoding='utf-8')


if __name__ == '__main__':
    main()
