"""Freeze real-run bytes and lossless captures with existing source-object reuse."""
import gzip
import hashlib
import json
import os
import shutil
from pathlib import Path

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(b):return hashlib.sha256(b).hexdigest().upper()
def write(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
previous=HERE.parent/'ReplayContract'
index=read(HERE/'source-index.json') if (HERE/'source-index.json').exists() else {}
for digest,item in read(previous/'source-index.json').items():
    item=dict(item)
    if 'path' in item:item['path']=os.path.relpath(previous/item['path'],HERE).replace('\\','/')
    index.setdefault(digest,item)
used={};logs={}
runs=[(label+'-run.json',label+'-original/source') for label in ['initial-capture','initial-uav','qualification','matrix','compat'] if (HERE/(label+'-run.json')).exists()]
for name,source_dir in runs:
    run=read(HERE/name);assert run['activeProcess'] is None
    for item in run['sources']:
        digest=item['sha256'];used[name+':'+item['path']]=digest
        if digest in index:continue
        source=HERE/source_dir/item['path']
        if not source.exists():source=ROOT/item['path']
        value=source.read_bytes();assert sha(value)==digest
        target=HERE/'source-blobs'/(digest[:16]+'-'+source.name);target.parent.mkdir(exist_ok=True);target.write_bytes(value)
        index[digest]={'path':target.relative_to(HERE).as_posix(),'bytes':len(value)}
    entries=run['entries'] or [{'log':run['activeProcess']['log'],'logSha256':sha(Path(run['activeProcess']['log']).read_bytes())}]
    for e in entries:
        source=Path(e['log']);value=source.read_bytes();assert sha(value)==e['logSha256']
        target=HERE/'logs'/source.name;target.parent.mkdir(exist_ok=True);target.write_bytes(value)
        logs[str(source)]={'path':target.relative_to(HERE).as_posix(),'sha256':sha(value)}
write(HERE/'source-index.json',{k:index[k] for k in sorted(set(used.values()))});write(HERE/'source-files.json',used);write(HERE/'log-index.json',logs)
captures=read(HERE/'capture-archives.json') if (HERE/'capture-archives.json').exists() else {}
paths=list((HERE/'results').glob('*.json'))+list((HERE/'results').rglob('*.bin'))+[HERE/'initial-capture/report.json',HERE/'initial-capture/lineage.bin']

for path in paths:
    if not path.exists():continue
    value=path.read_bytes();packed=gzip.compress(value,mtime=0);assert gzip.decompress(packed)==value
    target=path.with_suffix(path.suffix+'.gz');target.write_bytes(packed)
    captures[path.relative_to(HERE).as_posix()]={'path':target.relative_to(HERE).as_posix(),'rawBytes':len(value),'rawSha256':sha(value),'archiveBytes':len(packed),'archiveSha256':sha(packed)}
    path.unlink()
write(HERE/'capture-archives.json',captures)
for _,source_dir in runs:
    target=(HERE/source_dir).resolve();assert target.is_relative_to(HERE) and target.name=='source'
    if target.exists():shutil.rmtree(target)
print('Frozen',len(set(used.values())),'exact sources,',len(logs),'logs and',len(captures),'lossless captures')
