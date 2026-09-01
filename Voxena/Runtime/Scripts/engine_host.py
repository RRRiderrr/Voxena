from __future__ import annotations
import argparse, importlib, json, os, sys, traceback
from pathlib import Path

ADAPTERS={
 'cosyvoice3':'adapters.cosyvoice3',
 'fish-s2-pro':'adapters.fish_s2',
 'xtts-v2':'adapters.xtts_v2',
 'f5-tts-ru':'adapters.f5_tts_ru',
 'qwen3-tts':'adapters.qwen3_tts',
 'chatterbox-v3':'adapters.chatterbox_v3',
}

def parser():
 p=argparse.ArgumentParser(); p.add_argument('command',choices=['doctor','prepare','clone','generate','generate-sequence']); p.add_argument('--model-id',required=True); p.add_argument('--model-dir',required=True); p.add_argument('--source-dir',default=''); p.add_argument('--device',default='auto');
 p.add_argument('--audio',default=''); p.add_argument('--transcript',default=''); p.add_argument('--output',default=''); p.add_argument('--voice-kind',default='custom'); p.add_argument('--voice',default=''); p.add_argument('--preset-id',default=''); p.add_argument('--text-file',default=''); p.add_argument('--raw-text-file',default=''); p.add_argument('--sequence-file',default=''); p.add_argument('--temperature',type=float,default=.8); p.add_argument('--speed',type=float,default=1); p.add_argument('--seed',type=int,default=0); p.add_argument('--delivery',default=''); p.add_argument('--expressiveness',type=float,default=.5); p.add_argument('--stability',type=float,default=.55); return p

def _base_kwargs(a):
 kwargs=vars(a).copy(); kwargs.pop('command',None); kwargs.pop('sequence_file',None)
 kwargs['text']=Path(a.text_file).read_text(encoding='utf-8') if a.text_file else ''
 kwargs['raw_text']=Path(a.raw_text_file).read_text(encoding='utf-8') if a.raw_text_file else ''
 return kwargs

def _generate_sequence(mod,a):
 if not a.sequence_file: raise ValueError('--sequence-file is required for generate-sequence')
 items=json.loads(Path(a.sequence_file).read_text(encoding='utf-8'))
 if not isinstance(items,list) or not items: raise ValueError('The segmented synthesis sequence is empty.')
 base=_base_kwargs(a)
 for i,item in enumerate(items):
  if not isinstance(item,dict): raise ValueError('Invalid sequence item at index '+str(i))
  kwargs=base.copy(); kwargs.update(item)
  kwargs['text']=str(item.get('text',''))
  kwargs['output']=str(item.get('output',''))
  if not kwargs['text'].strip(): raise ValueError('Empty text in sequence item '+str(i))
  if not kwargs['output']: raise ValueError('Missing output in sequence item '+str(i))
  print(f'VOXENA: Rendering tagged segment {i+1}/{len(items)}',flush=True)
  mod.generate(**kwargs)

def main():
 a=parser().parse_args(); modname=ADAPTERS.get(a.model_id)
 if not modname: raise ValueError('Unsupported Voxena model: '+a.model_id)
 mod=importlib.import_module(modname)
 if a.command=='generate-sequence': _generate_sequence(mod,a)
 else:
  kwargs=_base_kwargs(a)
  getattr(mod,a.command)(**kwargs)
 print('VOXENA_RESULT=OK',flush=True)

if __name__=='__main__':
 try: main()
 except Exception as e:
  print('VOXENA_ERROR='+str(e),file=sys.stderr,flush=True); traceback.print_exc(); sys.exit(1)
