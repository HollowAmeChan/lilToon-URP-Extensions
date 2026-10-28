"""Import matching SIGMA source only and generate wrappers from native descriptors."""
from pathlib import Path
import ctypes as c,hashlib,importlib.util,json,re,sys,uuid
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).parent
VENDOR=ROOT/'.codex-research/UnityPathTracing/RenderingPlugin/External/NRD'
PINNED=ROOT/'Runtime/RTDI/ThirdParty/NRD'
WRAPPERS=ROOT/'Runtime/RTShadow/Shaders/NRD'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,text):
    p.parent.mkdir(parents=True,exist_ok=True);p.write_text(text,encoding='utf-8')
    meta=p.with_name(p.name+'.meta')
    if not meta.exists():meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n',encoding='utf-8')
def main():
    sys.dont_write_bytecode=True
    spec=importlib.util.spec_from_file_location('inventory',ROOT/'Tests~/RT/R0/probe_sigma.py');api=importlib.util.module_from_spec(spec);spec.loader.exec_module(api)
    matches={}
    for name,src in [('NRD.hlsli','Shaders/NRD.hlsli'),('NRDConfig.hlsli','Shaders/NRDConfig.hlsli'),('Common.hlsli','Shaders/Common.hlsli'),('Include/NRDSettings.h','Include/NRDSettings.h'),('Include/NRDDescs.h','Include/NRDDescs.h')]:
        a=PINNED/name;b=VENDOR/src
        assert a.read_bytes().replace(b'\r\n',b'\n')==b.read_bytes().replace(b'\r\n',b'\n'),name
        matches[name]={'pinned':sha(a),'source':sha(b)}
    imported=[]
    for source in sorted((VENDOR/'Shaders').glob('SIGMA*')):
        dest=PINNED/source.name
        if dest.exists():assert dest.read_bytes()==source.read_bytes()
        else:
            write(dest,source.read_text(encoding='utf-8-sig'))
            # Preserve the upstream bytes, including line endings.
            dest.write_bytes(source.read_bytes())
        imported.append({'path':dest.relative_to(ROOT).as_posix(),'sourceSha256':sha(source),'sha256':sha(dest)})
    baseline=json.loads((ROOT/'Tests~/RT/R0/sigma-inventory.json').read_text(encoding='utf-8-sig'))
    assert sha(api.DLL)==baseline['sources'][0]['sha256'],'Pinned native DLL changed'
    inventory=baseline['sigma'][0]
    lib=c.WinDLL(str(api.DLL));lib.CreateInstance.argtypes=[c.POINTER(api.Creation),c.POINTER(c.c_void_p)];lib.CreateInstance.restype=c.c_uint32;lib.GetInstanceDesc.argtypes=[c.c_void_p];lib.GetInstanceDesc.restype=c.POINTER(api.Instance);lib.DestroyInstance.argtypes=[c.c_void_p]
    method=api.Denoiser(0,16);create=api.Creation((c.c_void_p*4)(),c.pointer(method),1);instance=c.c_void_p()
    assert lib.CreateInstance(c.byref(create),c.byref(instance))==0
    identifiers=[];resources=[]
    try:
        desc=lib.GetInstanceDesc(instance).contents;assert desc.pipeline_count==9
        for i in range(desc.pipeline_count):
            pipeline=desc.pipelines[i];identifier=bytes(pipeline.identifier).split(b'\0')[0].decode();identifiers.append(identifier)
            assert identifier==inventory['pipelines'][i]['identifier']
            parts=identifier.split('|');file=parts[0];defines=dict(p.split('=') for p in parts[1:]);defines.update(__cplusplus='0',SIGMA_5X5_BLUR_RADIUS_ESTIMATION_KERNEL='1')
            header=PINNED/file.replace('.cs.hlsl','.resources.hlsli');text=header.read_text(encoding='utf-8-sig')
            active=[True];names=[]
            for line in text.splitlines():
                stripped=line.strip()
                if stripped.startswith('#ifndef'):
                    active.append(active[-1] and stripped.split()[1] not in defines);continue
                if stripped.startswith('#if'):
                    expr=stripped[3:].strip().replace('||',' or ').replace('&&',' and ')
                    expr=re.sub(r'\b[A-Z_a-z]\w*\b',lambda m:defines.get(m[0],m[0] if m[0] in ['or','and'] else '0'),expr)
                    assert re.fullmatch(r'[\d\s()=!<>orand.+*/-]+',expr),expr
                    active.append(active[-1] and bool(eval(expr,{'__builtins__':{}},{})));continue
                if stripped.startswith('#else'):active[-1]=active[-2] and not active[-1];continue
                if stripped.startswith('#endif'):active.pop();continue
                if not active[-1]:continue
                match=re.search(r'NRD_(?:INPUT|OUTPUT)\(\s*[^,]+,\s*[^,]+,\s*(\w+)\s*,',line)
                if match:names.append(match[1])
            count=sum(pipeline.ranges[r].count for r in range(pipeline.count));assert count==len(names),(identifier,count,names)
            resources.append(names)
            code='// Original pinned NRD SIGMA; Unity bindings only. Existing NRD license applies.\n#pragma kernel main\n#pragma use_dxc\n#pragma target 6.0\n#define NRD_INTERNAL 1\n#define NRD_COMPILER_DXC 1\n'
            code+=''.join(f'#define {k} {v}\n' for k,v in (p.split('=') for p in parts[1:]))
            code+='#include "../../../NRD/Shaders/HoNrdBindings.hlsl"\n'
            # From RTShadow/Shaders/NRD back to Runtime, then the pinned SDK.
            code+=f'#include "../../../RTDI/ThirdParty/NRD/{file}"\n'
            write(WRAPPERS/f'HoRTShadowSigma{i:02}.compute',code)
    finally:lib.DestroyInstance(instance)
    content='// Generated from pinned NRD 4.17.3 native descriptors and original resource declarations.\nnamespace lilToon.URP.Extensions.RTShadow\n{\n    internal static class HoRTShadowSigmaManifest\n    {\n        internal static readonly string[] Identifiers = {\n'
    content+=''.join('            '+json.dumps(v)+',\n' for v in identifiers)+'        };\n        internal static readonly string[][] Resources = {\n'
    content+=''.join('            new[] { '+', '.join(json.dumps(v) for v in row)+' },\n' for row in resources)+'        };\n    }\n}\n'
    write(ROOT/'Runtime/RTShadow/HoRTShadowSigmaManifest.cs',content)
    bindings=(ROOT/'Runtime/RTDI/Shaders/NRD/HoRTDINrdBindings.hlsl').read_text(encoding='utf-8-sig')
    write(ROOT/'Runtime/NRD/Shaders/HoNrdBindings.hlsl','// Legacy constant-buffer binding name is preserved for all NRD profiles.\n'+bindings)
    for folder in [WRAPPERS,ROOT/'Runtime/NRD/Shaders']:
        meta=folder.with_name(folder.name+'.meta')
        if not meta.exists():meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\nfolderAsset: yes\n',encoding='utf-8')
    report={'version':[4,17,3],'generatorSha256':sha(Path(__file__).resolve()),'nativeDllSha256':sha(api.DLL),'matchingSdkSources':matches,'imported':imported,'identifiers':identifiers,'resources':resources,'scope':'Source import and descriptor binding generation only; not GPU or image-quality acceptance.'}
    (HERE/'generation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print('Generated nine matching SIGMA wrappers; no native DLL or existing RELAX algorithm bytes modified.')
if __name__=='__main__':main()
