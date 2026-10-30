from pathlib import Path
import json, struct, sys

class Reader:
    def __init__(self, data, encoding): self.d=data; self.o=0; self.enc=encoding
    def take(self,n):
        if self.o+n>len(self.d): raise EOFError
        b=self.d[self.o:self.o+n]; self.o+=n; return b
    def u8(self): return self.take(1)[0]
    def i8(self): return struct.unpack('<b',self.take(1))[0]
    def u16(self): return struct.unpack('<H',self.take(2))[0]
    def i16(self): return struct.unpack('<h',self.take(2))[0]
    def u32(self): return struct.unpack('<I',self.take(4))[0]
    def i32(self): return struct.unpack('<i',self.take(4))[0]
    def f32(self): return struct.unpack('<f',self.take(4))[0]
    def vec3(self): return [self.f32(),self.f32(),self.f32()]
    def idx(self,size,signed=True):
        if size==1: return self.i8() if signed else self.u8()
        if size==2: return self.i16() if signed else self.u16()
        return self.i32() if signed else self.u32()
    def text(self):
        n=self.i32(); b=self.take(max(0,n))
        return b.decode('utf-16-le' if self.enc==0 else 'utf-8','replace').rstrip('\0')

def parse(path):
    r0=Reader(Path(path).read_bytes(),'utf-8'); assert r0.take(4)==b'PMX '
    version=struct.unpack('<f',r0.take(4))[0]; header=r0.take(1)[0]; r0.take(header)
    r=Reader(r0.d,r0.enc if hasattr(r0,'enc') else 'utf-8'); r.o=r0.o
    # Re-read the global settings because the header bytes are: encoding, addUV,
    # vertex, texture, material, bone, morph, rigid sizes.
    r.o=8; globals_count=r.u8(); globals_data=r.take(globals_count)
    encoding=globals_data[0]; add_uv=globals_data[1]; sizes=globals_data[2:]
    r=Reader(r0.d,'utf-8' if encoding==1 else 'utf-16-le'); r.o=8+1+globals_count
    text_encoding=encoding
    r.enc=text_encoding
    model_name=r.text(); english_name=r.text()
    comment=r.text(); english_comment=r.text()
    vx=r.u32()
    for _ in range(vx):
        r.take(3*4+3*4+2*4+add_uv*4*4); wt=r.u8()
        if wt==0: r.idx(sizes[3])
        elif wt==1: r.idx(sizes[3]);r.idx(sizes[3]);r.f32()
        elif wt in (2,4):
            for _ in range(4): r.idx(sizes[4])
            for _ in range(4): r.f32()
        elif wt==3:
            r.idx(sizes[3]);r.idx(sizes[3]);r.f32();r.take(9*4)
        r.f32()
    ic=r.u32(); r.take(ic*sizes[0])
    tc=r.i32(); textures=[r.text() for _ in range(max(0,tc))]
    mc=r.i32()
    for _ in range(max(0,mc)):
        r.text();r.text();r.take(4*4+3*4+4+3*4+1+4*4+4);r.idx(sizes[1]);r.idx(sizes[1]);r.u8();toon=r.u8();r.idx(sizes[2]) if toon==0 else r.u8();r.text();r.i32()
    bc=r.i32(); bones=[]
    for _ in range(max(0,bc)):
        n=r.text();r.text();pos=r.vec3();parent=r.idx(sizes[4]);layer=r.i32();flags=r.u16(); b={'name':n,'position':pos,'parentIndex':parent,'layer':layer,'flags':flags}
        if flags&1: b['tailIndex']=r.idx(sizes[3])
        else: b['tailOffset']=r.vec3()
        if flags&0x0300: b['inherit']={'parent':r.idx(sizes[3]),'weight':r.f32(),'rotation':bool(flags&0x0100),'translation':bool(flags&0x0200)}
        if flags&0x0400: b['fixedAxis']=r.vec3()
        if flags&0x0800: b['localAxis']=[r.vec3(),r.vec3()]
        if flags&0x2000: b['externalParentKey']=r.i32()
        if flags&0x0020:
            b['ikTarget']=r.idx(sizes[3]); b['ikLoops']=r.i32(); b['ikLimit']=r.f32(); lc=r.i32(); b['ikLinks']=[]
            for _ in range(max(0,lc)):
                link={'index':r.idx(sizes[3]),'angleLimits':bool(r.u8())}
                if link['angleLimits']: link['min']=r.vec3();link['max']=r.vec3()
                b['ikLinks'].append(link)
        bones.append(b)
    morph_types={0:'group',1:'vertex',2:'bone',3:'uv',4:'additionalUv1',5:'additionalUv2',6:'additionalUv3',7:'additionalUv4',8:'material',9:'flip',10:'impulse'}
    morphs=[]; moc=r.i32()
    for _ in range(max(0,moc)):
        n=r.text();r.text();panel=r.u8();typ=r.u8();count=r.i32();m={'name':n,'panel':panel,'type':morph_types.get(typ,str(typ)),'typeId':typ,'offsetCount':count}
        for _ in range(max(0,count)):
            if typ==0:r.idx(sizes[2]);r.f32()
            elif typ==1:r.idx(sizes[0]);r.take(12)
            elif typ==2:r.idx(sizes[3]);r.take(12+16)
            elif 3<=typ<=7:r.idx(sizes[0]);r.take(16)
            elif typ==8:r.idx(sizes[1]);r.u8();r.take(4*4+3*4+4+3*4+4*4+4*4+4*4)
            elif typ==9:r.idx(sizes[2]);r.f32()
            elif typ==10:r.idx(sizes[3]);r.u8();r.u8();r.vec3()
        morphs.append(m)
    index_to_name={i:b['name'] for i,b in enumerate(bones)}
    for b in bones:
        if 'ikLinks' in b:b['ikLinkNames']=[index_to_name.get(x['index'],'<missing>') for x in b['ikLinks']]
        if 'ikTarget' in b:b['ikTargetName']=index_to_name.get(b['ikTarget'],'<missing>')
    standard=['センター','全ての親','上半身','上半身2','下半身','左足','右足','左ひざ','右ひざ','左足首','右足首','左つま先','右つま先','左足ＩＫ','右足ＩＫ','左つま先ＩＫ','右つま先ＩＫ','左腕','右腕','左ひじ','右ひじ','左手首','右手首','頭','首','両目']
    names={b['name'] for b in bones}
    return {'path':str(path),'modelName':model_name,'englishName':english_name,'version':version,'vertexCount':vx,'textureCount':len(textures),'materialCount':mc,'boneCount':bc,'morphCount':moc,'bones':bones,'morphs':morphs,'ikBones':[b['name'] for b in bones if 'ikLinks' in b],'standardBones':{n:n in names for n in standard},'morphTypeCounts':{k:sum(1 for m in morphs if m['type']==k) for k in set(m['type'] for m in morphs)}}

if __name__=='__main__':
    output=[]
    for arg in sys.argv[1:]: output.append(parse(arg))
    print(json.dumps(output,ensure_ascii=False,indent=2))
