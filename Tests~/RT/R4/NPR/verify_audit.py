"""Retain the NPR failure and eliminate unsupported shader-layout hypotheses."""
from pathlib import Path
import gzip,hashlib,json,re
HERE=Path(__file__).resolve().parent
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
def main():
    for record in read(HERE/'compression.json'):
        packed=HERE/record['compressed'];assert digest(packed)==record['compressedSha256']
        assert hashlib.sha256(gzip.decompress(packed.read_bytes())).hexdigest()==record['sha256']
    audits=[];controls=[];ao_control=None
    for path in sorted(HERE.glob('run-*.json')):
        run=read(path);assert run['activeProcess'] is None
        log=HERE/(path.stem.replace('run-','layout-')+'.log');assert digest(log)==run['logSha256'].lower()
        entry=run.get('entry','Run')
        if entry!='Run':
            text=log.read_text(encoding='utf-8-sig')
            if entry=='RunAoIsolatedRendering':
                result=read(HERE/run['output']/'render-report.json')
                assert run['exit']==0 and result['pass'] and result['toonShadow']==0 and result['toonReceiveOff']==result['toonLit']==1
                assert '[HoRTNprAoControl] Only realtime AO disabled' in text
                ao_control={'run':path.name,'result':run['output']+'/render-report.json','shadow':0,'receiveOff':1,'lit':1};continue
            assert run['exit']!=0,'Do not infer reception success from a diagnostic wrapper'
            controls.append({'run':path.name,'entry':entry,'exit':run['exit'],'conclusion':'No NPR reception pass; bootstrap changed failure stage, Integer and reimport retained same response assertion.'})
            continue
        report_path=HERE/run['output']/'report.json'
        if not report_path.exists():
            assert '[HoRTNprLayoutAudit] END variants=0' in log.read_text(encoding='utf-8-sig')
            audits.append({'run':path.name,'accepted':False,'reason':'Zero variants; initial helper emitted no report. No URP layout coverage.'});continue
        report=read(report_path)
        if len(report['variants'])==0:
            audits.append({'run':path.name,'accepted':False,'reason':'Active builtin subshader selected before URP bootstrap; zero URP variants, no coverage.'});continue
        assert run['exit']==0 and len(report['variants'])==20 and not report['error']
        layouts={};sizes=set()
        for variant in report['variants']:
            assert variant['compile'] and variant['preprocess']
            for buffer in variant['buffers']:
                if buffer['name']!='UnityPerMaterial':continue
                sizes.add(buffer['size'])
                for field in buffer['fields']:
                    key=field['name'];value=(field['index'],field['type'],field['rows'],field['columns'],field['arraySize'])
                    if key in layouts:assert layouts[key]==value,(key,layouts[key],value)
                    layouts[key]=value
        assert sizes=={2800}
        for field in ['_Color','_UseShadow','_AsUnlit','_LightMinLimit','_ShadowReceive']:assert field in layouts
        audits.append({'run':path.name,'accepted':True,'variants':20,'materialBufferSize':2800,'consistentUsedFields':len(layouts),'keyFields':{name:layouts[name] for name in ['_Color','_UseShadow','_AsUnlit','_LightMinLimit','_ShadowReceive']}})
    assert sum(x['accepted'] for x in audits)==1 and len(controls)==4 and ao_control
    capture=read(HERE/'capture-completion.json');assert digest(HERE/capture['capture'])==capture['captureSha256']
    def mcp(name):
        result=read(HERE/('replay-'+name+'.json'))
        return json.loads(next(c['text'] for c in result['content'] if c['type']=='text'))
    info=mcp('info');assert info['api']=='D3D12' and info['totalDraws']==44 and info['totalEvents']==321
    assert mcp('log')['count']==0
    trace=mcp('942-trace');assert trace['eventId']==942 and trace['totalSteps']==418
    # nextInstruction is shown after each executed operation. At step 169 the
    # t30 sample just executed: it supplies 0.2158203 before AO remap/multiply.
    sample=next(c['after']['uintValues'][0] for row in trace['trace'] if row['step']==169 for c in row['changes'] if c['after']['name']=='r22')
    import struct
    sampled_ao=struct.unpack('<f',struct.pack('<I',sample))[0];assert sampled_ao==.2158203125
    result={'scope':'NPR diagnosis; original gate was masked by the private test material realtime AO without a producer','layoutAudits':audits,'renderControls':controls,'aoIsolatedControl':ao_control,'capture':capture['capture'],'captureSha256':capture['captureSha256'],'sampledAo':sampled_ao,
            'eliminated':'The 20 selected URP stage variants agree on every reflected material field index/type and 2800-byte buffer size. Integer setter and active-URP reimport do not resolve reception. No production shader/constant-layout change is justified by this evidence.',
            'limits':'Reflection proves compiled layouts, not GPU-bound bytes or actual forward keyword/state. Initial zero-variant run is rejected. Older wrapper copies of the prior layout report are stale shared artifacts, not fresh control evidence; use accepted audit only.',
            'next':'Keep AO disabled only in Cast-focused test material. Qualify real RT Cast with URP map keywords absent, per-layer/mask/strength/fallback and SIGMA continuous ramp; final character/Player/multilight work remains.'}
    (HERE/'review.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('NPR diagnosis verified: consistent layouts, retained failures, GPU AO=0.2158203 and private AO-only control 0/1/1.')
if __name__=='__main__':main()
