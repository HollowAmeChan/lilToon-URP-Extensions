from pathlib import Path
import hashlib,json,gzip
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
def sha(b):return hashlib.sha256(b).hexdigest().upper()
index=json.loads((HERE/'source-index.json').read_text(encoding='utf-8')) if (HERE/'source-index.json').exists() else {}
for runPath in HERE.glob('*/run.json'):
 run=json.loads(runPath.read_text(encoding='utf-8-sig'))
 for item in run.get('sources',[]):
  digest=item['sha256']
  if digest in index:continue
  p=ROOT/item['path'];b=p.read_bytes();assert sha(b)==digest,(str(runPath),item['path'])
  target=HERE/'source-blobs'/(digest[:16]+'-'+p.name+'.gz');target.parent.mkdir(exist_ok=True);packed=gzip.compress(b,mtime=0);target.write_bytes(packed);assert gzip.decompress(target.read_bytes())==b
  index[digest]={'path':target.relative_to(HERE).as_posix(),'bytes':len(b),'archiveSha256':sha(packed)}
(HERE/'source-index.json').write_text(json.dumps(index,indent=2)+'\n',encoding='utf-8');print('Frozen exact compile/run source objects',len(index))
