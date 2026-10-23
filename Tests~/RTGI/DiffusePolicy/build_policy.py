"""Reconstruct only the mature DI diffuse edits; never edit vendor/production."""
from pathlib import Path
import re, hashlib, json, difflib
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
DEST=HERE/'reconstructed'
DEST.mkdir(exist_ok=True)
def read(p):return p.read_text(encoding='utf-8').replace('\r\n','\n')
def once(s,a,b):
 assert s.count(a)==1,(a,s.count(a))
 return s.replace(a,b)
records=[]
expected=json.loads((HERE/'policy-source.json').read_text(encoding='utf-8'))
expected={r['stage']:r for r in expected}
for stem in ['AtrousSmem','Atrous']:
 source=read(ROOT/f'Runtime/RTDI/ThirdParty/NRD/RELAX_{stem}.cs.hlsl')
 di=read(ROOT/f'Runtime/RTDI/Shaders/NRD/HoRTDINrd{stem}.hlsl')
 assert hashlib.sha256(source.encode()).hexdigest()==expected[stem]['stockNormalizedSha256'], 'Stock source drift: reconstruct archived exact sources first'
 assert hashlib.sha256(di.encode()).hexdigest()==expected[stem]['diNormalizedSha256'], 'DI source drift: reconstruct archived exact sources first'
 body=source
 if stem=='AtrousSmem':
  # The variance preblur must also exclude spatial mean contrast. Preserve loop/spec code.
  body=once(body,'    float4 diffuseSum = 0;','    diffuseVariance = 0;')
  body=once(body,'            diffuseSum += diffuse * k;','            float lumaDiff = Color::Luminance(diffuse.rgb);\n            diffuseVariance += k * max(0, diffuse.a - lumaDiff * lumaDiff);')
  body=once(body,'    float diffuse1stMoment = Color::Luminance(diffuseSum.rgb);\n    float diffuse2ndMoment = diffuseSum.a;\n    diffuseVariance = max(0, diffuse2ndMoment - diffuse1stMoment * diffuse1stMoment);','    // Temporal variance was accumulated above, without spatial mean contrast.')
  body=once(body,'        float4 sumDiffuseIlluminationAnd2ndMoment = 0;','        float4 sumDiffuseIlluminationAnd2ndMoment = 0;\n        float sumDiffuseTemporalVariance = 0;')
  a='                sumDiffuseIlluminationAnd2ndMoment += wDiffuse * sampleDiffuseIlluminationAnd2ndMoment;'
  b=a+'\n                float sampleDiffuseTemporalVar = max(0.0, sampleDiffuseIlluminationAnd2ndMoment.a - sampleDiffuseLuminance * sampleDiffuseLuminance);\n                sumDiffuseTemporalVariance += wDiffuse * wDiffuse * sampleDiffuseTemporalVar;'
  body=once(body,a,b)
  body=once(body,'        float diffuseVariance = max(0, diffuse2ndMoment - diffuse1stMoment * diffuse1stMoment);','        float diffuseVariance = sumDiffuseTemporalVariance / (sumWDiffuse * sumWDiffuse);')
  assert 'sumDiffuseTemporalVariance / (sumWDiffuse * sumWDiffuse)' in di
  assert 'diffuseVariance += k * max(0, valueDiff.a - lumaDiff * lumaDiff);' in di
 else:
  anchor='                float diffuseLuminanceW = abs(centerDiffuseLuminance - sampleDiffuseLuminance) * diffusePhiLIlluminationInv;'
  begin=di.index('                float pairDiffuseVar =')
  end=di.index('\n                diffuseLuminanceW = min',begin)
  body=once(body,anchor,di[begin:end])
  anchor='    float4 filteredDiffuseIlluminationAndVariance = float4(sumDiffuseIlluminationAndVariance / float4(sumWDiffuse.xxx, sumWDiffuse * sumWDiffuse));'
  begin=di.index('    // Use fixed pair exchange coefficients')
  end=di.index('\n    #if( NRD_MODE == SH )',begin)
  body=once(body,anchor,di[begin:end])
 package='Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/ThirdParty/NRD/'
 body=re.sub(r'#include "([^"]+)"',lambda m:'#include "'+package+m[1]+'"',body)
 name='HoRTGIDiffusePolicy'+stem
 path=DEST/(name+'.hlsl');path.write_text(body,encoding='utf-8',newline='\n')
 wrapper='''#pragma kernel main
#pragma use_dxc
#pragma target 6.0
#define NRD_INTERNAL 1
#define NRD_COMPILER_DXC 1
#define NRD_SIGNAL DIFF
#define NRD_MODE RADIANCE
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/NRD/HoRTDINrdBindings.hlsl"
#include "'''+name+'''.hlsl"
'''
 (DEST/(name+'.compute')).write_text(wrapper,encoding='utf-8',newline='\n')
 (HERE/(stem+'.patch')).write_text(''.join(difflib.unified_diff(source.splitlines(True),body.splitlines(True),fromfile='vendor/'+stem,tofile='GI-private/'+stem)),encoding='utf-8',newline='\n')
 records.append({'stage':stem,'stockNormalizedSha256':hashlib.sha256(source.encode()).hexdigest(),'diNormalizedSha256':hashlib.sha256(di.encode()).hexdigest(),'bodySha256':hashlib.sha256(body.encode()).hexdigest(),'wrapperSha256':hashlib.sha256(wrapper.encode()).hexdigest()})
(HERE/'policy-source.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
print('Built two GI-private DIFF-only bodies; exact patch anchors and mature DI fragments checked')
