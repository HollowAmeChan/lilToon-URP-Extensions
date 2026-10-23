"""Independent analytic plane visibility/Lambert oracle for actual GPU contract words."""
from pathlib import Path
import json,gzip,hashlib
import numpy as np
HERE=Path(__file__).resolve().parent
def raw(p):return p.read_bytes() if p.exists() else gzip.decompress(p.with_suffix(p.suffix+'.gz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def oracle(light,mode,changed):
 # Independently intersect the z=-1 mid-plane; these two rays cross the
 # rectangular slab iff this point is inside its x/y aperture.
 p=np.array([-1. if light==0 else 1.,0.,-2.]);distance=np.sqrt(np.dot(p,p));mid=p*.5
 blocked=changed and -.8<=mid[0]<=-.2 and -.25<=mid[1]<=.25
 if blocked:return np.zeros(3)
 power=np.array([4.,2.,1.]) if mode==0 or light==0 else np.array([1.,4.,2.])
 return np.array([1,128/255,64/255])*power*(2/distance)/(np.pi*distance*distance)*2

def summarize():
 report=read(HERE/'results/qualification.json');b=raw(HERE/'results/qualification'/report['stream']);assert hashlib.sha256(b).hexdigest().upper()==report['streamSha256']
 u=np.frombuffer(b,dtype='<u4').reshape(64,10,4);f=u.view('<f4');errors=[];negativeStatic=0;missedChange=0;falseChange=0
 for i in range(64):
  mode=i//32;source=i%2;assert u[i,0,0]==i and u[i,0,1]==mode and u[i,0,2]==(mode==0)
  selected=int((i+.5)/64<=.75);assert u[i,0,3]==selected
  assert u[i,1].tolist()==[0xcafe0b07,0x0b07,0,32]
  for slot,light,changed in [(2,source,False),(3,source,False),(4,source,True),(5,1-source,False),(8,1-source,True)]:errors.append(float(np.max(np.abs(f[i,slot,:3].astype(float)-oracle(light,mode,changed)))))
  assert np.array_equal(u[i,2,:3],u[i,3,:3]);assert f[i,4,3]==(source==1) and f[i,8,3]==(source==0)
  assert u[i,7,0]==0x80000000+selected and u[i,7,1]==(0x12345678 if selected else 0xc0004000) and f[i,7,2]==(7.5 if selected else 2)
  assert u[i,9,0]==0x80000000+source and u[i,9,1]==0xc0004000 and f[i,9,2]==2
  if mode==1:negativeStatic+=int(not np.array_equal(u[i,2,:3],u[i,5,:3]))
  if mode==0 and source==0:missedChange+=int(np.array_equal(u[i,2,:3],u[i,8,:3]))
  if mode==0 and source==1:falseChange+=int(not np.array_equal(u[i,2,:3],u[i,8,:3]))
 assert max(errors)<1e-6 and negativeStatic==32 and missedChange==falseChange==16
 budgets=[]
 for w,h in [(64,64),(256,256),(1920,1080)]:
  pitch=((w+15)//16)*256*((h+15)//16);sidecar=3*pitch*16;lastShade=w*h*48
  budgets.append({'width':w,'height':h,'hypotheticalSidecar16BThreePools':sidecar,'hypotheticalLastShade48B':lastShade,'extraBytes':sidecar+lastShade,'extraMiB':(sidecar+lastShade)/1048576,'scope':'Candidate accounting only; omits confidence/gradient textures and alignment/native allocations; not measured or proven sufficient layout.'})
 return {'scope':'Actual original SDK GPU packing/stream selection, independent bounded two-point-light Lambert/plane oracle. No production scene/AS replay, temporal/spatial provenance implementation or NRD confidence acceptance.','productionChanged':False,'gpuCases':64,'packed32ByteCollisionCases':32,'miscDroppedByDefaultRoundtripCases':64,'unusedDroppedByRoundtripCases':64,'sdkSelectedBQuantiles':48,'bothCpuFullWordMutationChecks':1024,'sameChoiceUnchangedRadianceBitExactCases':64,'oracleMaxAbsError':max(errors),'newChoiceStaticFalseGradientCases':negativeStatic,'newChoiceMissedRealChangeCollisionCases':missedChange,'newChoiceFalseChangeCollisionCases':falseChange,'oracleResults':{'oldChoice0':oracle(0,0,False).tolist(),'oldChoice1':oracle(1,0,False).tolist(),'currentChoice0AfterOccluder':oracle(0,0,True).tolist(),'currentChoice1AfterOccluder':oracle(1,0,True).tolist()},'tentativeMemoryBudgets':budgets,'finding':'Existing GI32 plus aggregate radiance cannot identify the original secondary DI sample. SDK default pack/unpack does not preserve a free metadata word. Selection-return bool can drive an explicit auxiliary ledger, but production high-level T/S do not export it.','legacyCpuLayoutCorrection':'Legacy normal@12 was misc, misc@16 was LogLuv radiance, radiance@24 was normal. All32-word equality remained valid; historical classified counts/JSON names must be decoded with old schema. Correct future layout tested with actual GPU sentinel and both equality implementations.'}
if __name__=='__main__':
 r=summarize();(HERE/'analysis.json').write_text(json.dumps(r,indent=2)+'\n',encoding='utf-8');print(json.dumps(r,indent=2))
