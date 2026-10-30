from pathlib import Path
import subprocess, json, re, hashlib, shutil, time

src = Path(r'E:/BaiduSyncdisk/大师/_完整动作')
out = Path(r'E:/BaiduSyncdisk/大师/_完整动作_整理')
out.mkdir(exist_ok=True)

def decode_bytes(data):
    for enc in ('utf-8','cp936','cp932'):
        try:
            text = data.decode(enc)
            if '\ufffd' not in text:
                return text
        except Exception:
            pass
    return data.decode('utf-8','replace')

def clean_name(name, author):
    stem = Path(name).stem
    stem = re.sub(r'\s*\(\d+\)$','',stem).strip()
    stem = re.sub(r'[_\s]+[0-9a-fA-F]{32}$','',stem).strip()
    stem = re.sub(r'[_\s]+by[_\s-]*'+re.escape(author)+r'$','',stem,flags=re.I).strip()
    stem = re.sub(r'[_\s]+by[_\s-]+[\w\u4e00-\u9fff]+$','',stem,flags=re.I).strip()
    return re.sub(r'[<>:"/\\|?*\x00-\x1f]','_',stem).strip().rstrip('.')[:150] or Path(name).stem

def safe_name(s):
    return re.sub(r'[<>:"/\\|?*\x00-\x1f]','_',s).strip().rstrip('.')[:150] or '_unnamed'

def sha256(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for chunk in iter(lambda:f.read(1024*1024), b''):
            h.update(chunk)
    return h.hexdigest()

def archive_list(path):
    r=subprocess.run(['tar.exe','-tf',str(path)],capture_output=True,timeout=120)
    entries=[x.strip('\r') for x in decode_bytes(r.stdout).splitlines() if x.strip()]
    warning=None if r.returncode == 0 else 'list rc=%s %s' % (r.returncode, decode_bytes(r.stderr)[:300])
    return entries, warning

def read_text(path):
    data=path.read_bytes()
    for enc in ('utf-8-sig','cp936','cp932','utf-16','latin1'):
        try:
            text=data.decode(enc)
            if '\ufffd' not in text:
                return text
        except Exception:
            pass
    return data.decode('utf-8','replace')

def parse_vmd(path):
    data=path.read_bytes(); n=len(data); off=0
    result={'path':str(path),'bytes':n}
    if n<50:
        result['error']='too_small'; return result
    sig=data[:30].rstrip(b'\0 ').decode('ascii','replace'); off=30
    result['signature']=sig
    if sig.startswith('Vocaloid Motion Data file'):
        model_bytes=10
    elif sig.startswith('Vocaloid Motion Data 0002'):
        model_bytes=20
    else:
        result['error']='invalid_signature'; return result
    result['modelName']=data[off:off+model_bytes].rstrip(b'\0 ').decode('cp932','replace'); off += model_bytes
    def u32():
        nonlocal off
        if off+4>n: raise EOFError
        value=int.from_bytes(data[off:off+4],'little'); off += 4; return value
    try:
        count=u32(); off += count*111; result['boneFrames']=count
        count=u32(); off += count*23; result['morphFrames']=count
        if off>=n: return result
        count=u32(); off += count*61; result['cameraFrames']=count
        if off>=n: return result
        count=u32(); off += count*28; result['lightFrames']=count
        if off>=n: return result
        count=u32(); off += count*9; result['selfShadowFrames']=count
        if off>=n: return result
        count=u32(); result['showIKFrames']=count
        toggles=0
        for _ in range(count):
            off += 5
            k=u32(); toggles += k; off += k*21
        result['ikToggleEntries']=toggles
        result['tailBytes']=n-off
        if off>n: result['error']='truncated'
    except EOFError:
        result['error']='truncated'
    return result

index={'source':str(src),'output':str(out),'authors':{},'duplicates':[],'errors':[],'generatedAt':time.strftime('%Y-%m-%dT%H:%M:%S%z')}

for author_dir in sorted([x for x in src.iterdir() if x.is_dir()], key=lambda x:x.name):
    author=author_dir.name
    author_out=out/safe_name(author); author_out.mkdir(exist_ok=True)
    direct_out=author_out/'_direct'; direct_out.mkdir(exist_ok=True)
    author_record={'name':author,'output':str(author_out),'archives':[],'directVmd':[],'readmes':[],'vmd':[]}
    seen={}
    for path in sorted(author_dir.rglob('*')):
        if path.is_file() and path.suffix.lower()=='.vmd':
            try:
                dst=direct_out/safe_name(path.name)
                if not dst.exists(): shutil.copy2(path,dst)
                item=parse_vmd(dst); item['source']=str(path)
                author_record['directVmd'].append(item); author_record['vmd'].append(item)
            except Exception as exc:
                index['errors'].append({'path':str(path),'error':str(exc)})
    for archive in sorted([x for x in author_dir.rglob('*') if x.is_file() and x.suffix.lower() in ('.zip','.rar')], key=lambda x:str(x)):
        try:
            digest=sha256(archive)
            if digest in seen:
                index['duplicates'].append({'source':str(archive),'duplicateOf':seen[digest],'sha256':digest})
                continue
            seen[digest]=str(archive)
            target=author_out/safe_name(clean_name(archive.name,author)); target.mkdir(exist_ok=True)
            entries, list_warning=archive_list(archive)
            (target/'_source.txt').write_text('Original archive: %s\nSHA256: %s\n' % (archive,digest),encoding='utf-8')
            extracted=subprocess.run(['tar.exe','-xf',str(archive),'-C',str(target)],capture_output=True,timeout=300)
            extracted_files=[p for p in target.rglob('*') if p.is_file()]
            if extracted.returncode != 0 and not extracted_files:
                raise RuntimeError('extract rc=%s %s' % (extracted.returncode,decode_bytes(extracted.stderr)[:300]))
            readmes=[]
            for file in target.rglob('*'):
                if file.is_file() and (file.name.lower().startswith('read') or '说明' in file.name or '使用' in file.name or '规约' in file.name):
                    readmes.append({'path':str(file.relative_to(target)),'text':read_text(file)[:20000]})
            vmd=[]
            for file in target.rglob('*.vmd'):
                item=parse_vmd(file); vmd.append(item); author_record['vmd'].append(item)
            record={'source':str(archive),'sha256':digest,'folder':str(target),'archiveEntries':len(entries),'vmdCount':len(vmd),'readmeCount':len(readmes),'entriesSample':entries[:80],'readmes':readmes,'vmd':vmd,'archiveWarning':list_warning if list_warning else (decode_bytes(extracted.stderr)[:300] if extracted.returncode != 0 else None)}
            author_record['archives'].append(record)
            author_record['readmes'].extend([dict(source=str(archive),**item) for item in readmes])
        except Exception as exc:
            index['errors'].append({'path':str(archive),'error':str(exc)})
    index['authors'][author]=author_record

(out/'index.json').write_text(json.dumps(index,ensure_ascii=False,indent=2),encoding='utf-8')
lines=['# MMD 动作整理库','', '来源：'+str(src), '', '作者：%d；重复包：%d；错误：%d' % (len(index['authors']),len(index['duplicates']),len(index['errors'])),'']
for author,record in index['authors'].items():
    lines += ['## '+author,'','- 解压包：%d；直接 VMD：%d；VMD 总数：%d' % (len(record['archives']),len(record['directVmd']),len(record['vmd']))]
    for item in record['archives']:
        cams=sum(1 for v in item['vmd'] if v.get('cameraFrames',0)>0)
        morphs=sum(1 for v in item['vmd'] if v.get('morphFrames',0)>0)
        ik=sum(1 for v in item['vmd'] if v.get('showIKFrames',0)>0 or v.get('ikToggleEntries',0)>0)
        lines.append('- %s：VMD %d，README %d，镜头 VMD %d，形态键数据 %d，IK 开关段 %d' % (Path(item['folder']).name,item['vmdCount'],item['readmeCount'],cams,morphs,ik))
    lines.append('')
if index['duplicates']:
    lines += ['## 重复包',''] + ['- %s 与 %s SHA256 相同' % (item['source'],item['duplicateOf']) for item in index['duplicates']]
if index['errors']:
    lines += ['## 错误',''] + ['- %s：%s' % (item['path'],item['error']) for item in index['errors']]
(out/'README.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(json.dumps({'authors':len(index['authors']),'archives':sum(len(v['archives']) for v in index['authors'].values()),'directVmd':sum(len(v['directVmd']) for v in index['authors'].values()),'vmd':sum(len(v['vmd']) for v in index['authors'].values()),'duplicates':len(index['duplicates']),'errors':len(index['errors'])},ensure_ascii=False))
