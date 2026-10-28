"""Check GI scene ownership migration leaves the existing lighting core untouched."""
from pathlib import Path
import hashlib
import json
import subprocess

ROOT=Path(__file__).resolve().parents[4]
BASE='c05b93a0'


def main():
    paths=list(filter(None,subprocess.check_output(['git','ls-tree','-r','--name-only','-z',BASE,'--','Runtime/RTGI'],cwd=ROOT).decode().split('\0')))
    blobs=subprocess.run(['git','cat-file','--batch'],cwd=ROOT,input=''.join(f'{BASE}:{n}\n' for n in paths).encode(),stdout=subprocess.PIPE,check=True).stdout
    original={};cursor=0
    for name in paths:
        end=blobs.index(b'\n',cursor);size=int(blobs[cursor:end].split()[2]);cursor=end+1
        original[name]=blobs[cursor:cursor+size];cursor+=size+1
    allowed={'Runtime/RTGI/HoRTGIRendererFeature.cs','Runtime/RTGI/HoRTGISecondaryMaterials.cs'}
    unchanged=[]
    for name in filter(None,paths):
        if name in allowed: continue
        before=original[name]
        after=(ROOT/name).read_bytes()
        assert before.replace(b'\r\n',b'\n')==after.replace(b'\r\n',b'\n'),name
        unchanged.append(name)
    feature=(ROOT/'Runtime/RTGI/HoRTGIRendererFeature.cs').read_text(encoding='utf-8-sig')
    assert 'new HoRTScene(' not in feature,'GI must not privately create a scene'
    assert 'scene.Synchronize(' not in feature,'GI must not own world sync'
    assert 'scene?.Dispose()' not in feature,'GI must not free the borrowed scene'
    assert 'HoRTRendererFeature.Declare(' in feature and 'HoRTRendererFeature.TryAcquire(' in feature
    shaders=[n for n in unchanged if '/Shaders/' in n]
    result={'stage':'R3-GI','baseCommit':BASE,'unchangedCoreFiles':len(unchanged),
            'unchangedShaderFiles':len(shaders),'ownershipChecks':True,
            'scope':'Source ownership and unchanged lighting-core check; not a replacement for actual DI output/NRD tests.'}
    Path(__file__).with_name('migration.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(f'R3 GI: {len(unchanged)} core files and {len(shaders)} shader files unchanged; host ownership checks pass.')


if __name__=='__main__': main()
