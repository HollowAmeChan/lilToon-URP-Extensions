import ctypes as c, json, re
from pathlib import Path
root=Path(__file__).resolve().parents[3]
header=(root/'Runtime/RTDI/ThirdParty/NRD/Include/NRDDescs.h').read_text()
body=re.search(r'enum class Denoiser\s*:\s*uint32_t\s*\{(.*?)\};',header,re.S)[1]
body=re.sub(r'/\*.*?\*/|//[^\n]*','',body,flags=re.S)
methods=[n.strip() for n in body.split(',') if n.strip()]
denoiser_index=methods.index('RELAX_DIFFUSE')
lib=c.WinDLL(str(root/'Runtime/RTDI/Plugins/x86_64/HoRTDINRD.dll'))
class Library(c.Structure):
    _fields_=[('offsets',c.c_uint32*4),('denoisers',c.c_void_p),('count',c.c_uint32),('major',c.c_uint8),('minor',c.c_uint8),('build',c.c_uint8),('normal',c.c_uint8),('roughness',c.c_uint8)]
lib.GetLibraryDesc.restype=c.POINTER(Library)
d=lib.GetLibraryDesc().contents
print('Library',d.major,d.minor,d.build,'normal',d.normal,'roughness',d.roughness,flush=True)
assert (d.major,d.minor,d.build)==(4,17,3)
class Denoiser(c.Structure): _fields_=[('id',c.c_uint32),('type',c.c_uint32)]
class Creation(c.Structure): _fields_=[('callbacks',c.c_void_p*4),('denoisers',c.POINTER(Denoiser)),('count',c.c_uint32)]
class Bytecode(c.Structure): _fields_=[('data',c.c_void_p),('size',c.c_uint64)]
class Range(c.Structure): _fields_=[('type',c.c_uint32),('count',c.c_uint32)]
class Pipeline(c.Structure): _fields_=[('shaders',Bytecode*3),('ranges',c.POINTER(Range)),('count',c.c_uint32),('constants',c.c_bool),('identifier',c.c_char*256)]
class Texture(c.Structure): _fields_=[('format',c.c_uint32),('downsample',c.c_uint16)]
class Instance(c.Structure):
    _fields_=[('registers',c.c_uint32*6),('samplers',c.c_void_p),('samplerCount',c.c_uint32),('entry',c.c_char_p),('pipelines',c.POINTER(Pipeline)),('pipelineCount',c.c_uint32),('permanent',c.POINTER(Texture)),('permanentCount',c.c_uint32),('transient',c.POINTER(Texture)),('transientCount',c.c_uint32),('pool',c.c_uint32*5)]
lib.CreateInstance.argtypes=[c.POINTER(Creation),c.POINTER(c.c_void_p)];lib.CreateInstance.restype=c.c_uint32
lib.GetInstanceDesc.argtypes=[c.c_void_p];lib.GetInstanceDesc.restype=c.POINTER(Instance)
lib.DestroyInstance.argtypes=[c.c_void_p]
method=Denoiser(0,denoiser_index);desc=Creation();desc.denoisers=c.pointer(method);desc.count=1
instance=c.c_void_p();result=lib.CreateInstance(c.byref(desc),c.byref(instance));assert result==0,result
try:
    obj=lib.GetInstanceDesc(instance).contents
    out={'denoiserType':denoiser_index,'version':[d.major,d.minor,d.build],'normal':d.normal,'roughness':d.roughness,'sizes':{'library':c.sizeof(Library),'creation':c.sizeof(Creation),'pipeline':c.sizeof(Pipeline),'instance':c.sizeof(Instance)},'entry':obj.entry.decode(),'constantMax':obj.registers[5],'pipelines':[],'permanent':[],'transient':[]}
    for i in range(obj.pipelineCount):
        p=obj.pipelines[i];out['pipelines'].append({'identifier':p.identifier.decode(),'ranges':[(p.ranges[j].type,p.ranges[j].count) for j in range(p.count)],'constants':bool(p.constants)})
    for key,count,items in [('permanent',obj.permanentCount,obj.permanent),('transient',obj.transientCount,obj.transient)]:
        out[key]=[(items[i].format,items[i].downsample) for i in range(count)]
    (Path(__file__).parent/'inventory.json').write_text(json.dumps(out,indent=2))
    print('RELAX_DIFFUSE',denoiser_index,'pipelines',obj.pipelineCount,'permanent',obj.permanentCount,'transient',obj.transientCount,flush=True)
finally: lib.DestroyInstance(instance)
