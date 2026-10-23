"""Freeze exact actual source/log/payload bytes; preserve corrected metadata provenance."""
import json,hashlib,os,gzip,shutil
from pathlib import Path
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(b):return hashlib.sha256(b).hexdigest().upper()
def write(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
index=read(HERE/'source-index.json') if (HERE/'source-index.json').exists() else {}
for name in ['Backbuffer','PairedStages','ExchangeOperator']:
 prev=HERE.parent/name
 for digest,item in read(prev/'source-index.json').items():
  item=dict(item)
  if 'path' in item:item['path']=os.path.relpath(prev/item['path'],HERE).replace('\\','/')
  index.setdefault(digest,item)
used={};logs={}
for mode,folder in [('rg','initial-metadata/player-original/source'),('compat','compat-original/source')]:
 run=read(HERE/(mode+'-run.json'));assert run['activeProcess'] is None and len(run['entries'])==1
 for item in run['sources']:
  digest=item['sha256'];used[mode+':'+item['path']]=digest
  if digest in index:continue
  p=HERE/folder/item['path']
  if not p.exists():p=ROOT/item['path']
  b=p.read_bytes();assert sha(b)==digest,item['path'];target=HERE/'source-blobs'/(digest[:16]+'-'+p.name);target.parent.mkdir(exist_ok=True);target.write_bytes(b);index[digest]={'path':target.relative_to(HERE).as_posix(),'bytes':len(b)}
 for e in run['entries']:
  for field in ['buildLog','log']:
   p=Path(e[field]);b=p.read_bytes();assert sha(b)==e[field+'Sha256'];target=HERE/'logs'/p.name;target.parent.mkdir(exist_ok=True);target.write_bytes(b);logs[str(p)]={'path':target.relative_to(HERE).as_posix(),'sha256':sha(b)}
write(HERE/'source-index.json',{k:index[k] for k in sorted(set(used.values()))});write(HERE/'source-files.json',used);write(HERE/'log-index.json',logs)
payloads={}
for mode,folder in [('rg','PlayerGISkinnedComplexMotion'),('compat','PlayerGISkinnedCompatibilityComplexMotion')]:
 p=HERE/'player-results'/(mode+'-payload.json');items=read(p)
 for item in items:
  b=(ROOT/'.codex-research/RTGIValidation'/folder/item['path']).read_bytes();assert len(b)==item['bytes'] and sha(b)==item['sha256']
 assert not any(Path(i['path']).name=='HoGIGpuProbe.dll' for i in items)
 payloads[mode]={'manifest':p.relative_to(HERE).as_posix(),'sha256':sha(p.read_bytes()),'files':len(items),'verifiedAfterVisibleTest':True}
write(HERE/'payload-verification.json',payloads)
archives=read(HERE/'capture-archives.json') if (HERE/'capture-archives.json').exists() else {}
for p in (HERE/'player-results').glob('*/report.json'):
 b=p.read_bytes();packed=gzip.compress(b,mtime=0);assert gzip.decompress(packed)==b;t=p.with_suffix('.json.gz');t.write_bytes(packed);archives[p.relative_to(HERE).as_posix()]={'path':t.relative_to(HERE).as_posix(),'rawSha256':sha(b),'rawBytes':len(b),'archiveSha256':sha(packed),'archiveBytes':len(packed)};p.unlink()
write(HERE/'capture-archives.json',archives)
images={}
for p in (HERE/'player-results').glob('*/*.png'):
 b=p.read_bytes();images[p.relative_to(HERE).as_posix()]={'sha256':sha(b),'bytes':len(b)}
write(HERE/'image-artifacts.json',images)
for rel in ['initial-metadata/player-original/source','compat-original/source']:
 p=(HERE/rel).resolve();assert p.is_relative_to(HERE) and p.name=='source'
 if p.exists():shutil.rmtree(p)
print('Frozen',len(set(used.values())),'source objects,',len(logs),'logs,',len(images),'screen PNG; both complete payloads verified')
