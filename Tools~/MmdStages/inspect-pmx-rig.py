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
    data=Path(path).read_bytes(); assert data[:4]==b'PMX '
    version=struct.unpack('<f',data[4:8])[0]; header_size=data[8]
    globals_data=data[9:9+header_size]
    if len(globals_data)<8: raise EOFError('PMX header is truncated')
    encoding=globals_data[0]; add_uv=globals_data[1]; sizes=globals_data[2:8]
    r=Reader(data,'utf-8' if encoding==1 else 'utf-16-le'); r.o=9+header_size
    text_encoding=encoding
    r.enc=text_encoding
    model_name=r.text(); english_name=r.text()
    comment=r.text(); english_comment=r.text()
    vx=r.u32()
    weight_type_names={0:'BDEF1',1:'BDEF2',2:'BDEF4',3:'SDEF',4:'QDEF'}
    weight_type_counts={name:0 for name in weight_type_names.values()}
    vertex_influences=[]
    for _ in range(vx):
        position=r.vec3();r.take(3*4+2*4+add_uv*4*4);wt=r.u8()
        indices=[];weights=[]
        if wt==0: indices=[r.idx(sizes[3])];weights=[1.0]
        elif wt==1:
            indices=[r.idx(sizes[3]),r.idx(sizes[3])]
            w=r.f32();weights=[w,1.0-w]
        elif wt in (2,4):
            indices=[r.idx(sizes[3]) for _ in range(4)]
            weights=[r.f32() for _ in range(4)]
        elif wt==3:
            indices=[r.idx(sizes[3]),r.idx(sizes[3])]
            w=r.f32();weights=[w,1.0-w];r.take(9*4)
        else: raise ValueError('Unsupported PMX weight type: '+str(wt))
        weight_type_counts[weight_type_names[wt]]+=1
        vertex_influences.append((position,wt,indices,weights))
        r.f32()
    ic=r.u32(); r.take(ic*sizes[0])
    tc=r.i32(); textures=[r.text() for _ in range(max(0,tc))]
    mc=r.i32()
    for _ in range(max(0,mc)):
        r.text();r.text();r.take(4*4+3*4+4+3*4+1+4*4+4);r.idx(sizes[1]);r.idx(sizes[1]);r.u8();toon=r.u8();r.idx(sizes[2]) if toon==0 else r.u8();r.text();r.i32()
    bc=r.i32(); bones=[]
    debug_sections = '--debug' in sys.argv
    if debug_sections: print('DEBUG boneCount',bc,'offset',r.o,file=sys.stderr)
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
    foot_names={'左足','左ひざ','左足首','左つま先','右足','右ひざ','右足首','右つま先'}
    foot_weight_type_counts={}
    for index,bone in enumerate(bones):
        if bone['name'] not in foot_names: continue
        counts={name:0 for name in weight_type_names.values()}
        for _,wt,indices,weights in vertex_influences:
            if any(i==index and w>1e-5 for i,w in zip(indices,weights)):
                counts[weight_type_names[wt]]+=1
        foot_weight_type_counts[bone['name']]=counts
    morph_types={0:'group',1:'vertex',2:'bone',3:'uv',4:'additionalUv1',5:'additionalUv2',6:'additionalUv3',7:'additionalUv4',8:'material',9:'flip',10:'impulse'}
    if debug_sections: print('DEBUG afterBones',r.o,'next',r.d[r.o:r.o+8],file=sys.stderr)
    morphs=[]; moc=r.i32()
    # Bone/IK inspection is intentionally independent of the large morph
    # payload. Some legacy PMX files contain malformed or vendor-specific morph
    # records; their bone/IK section is still valid and useful for adaptation.
    if '--full-morph' not in sys.argv:
        index_to_name={i:b['name'] for i,b in enumerate(bones)}
        for b in bones:
            if 'ikLinks' in b:b['ikLinkNames']=[index_to_name.get(x['index'],'<missing>') for x in b['ikLinks']]
            if 'ikTarget' in b:b['ikTargetName']=index_to_name.get(b['ikTarget'],'<missing>')
        names={b['name'] for b in bones}
        standard=['センター','全ての親','上半身','上半身2','下半身','左足','右足','左ひざ','右ひざ','左足首','右足首','左つま先','右つま先','左足ＩＫ','右足ＩＫ','左つま先ＩＫ','右つま先ＩＫ','左腕','右腕','左ひじ','右ひじ','左手首','右手首','頭','首','両目']
        return {'path':str(path),'modelName':model_name,'englishName':english_name,'version':version,'vertexCount':vx,'textureCount':len(textures),'materialCount':mc,'boneCount':bc,'morphCount':moc,'bones':bones,'morphs':[],'ikBones':[b['name'] for b in bones if 'ikLinks' in b],'standardBones':{n:n in names for n in standard},'morphTypeCounts':{},'vertexSkinWeightTypes':weight_type_counts,'footBoneVertexWeightTypes':foot_weight_type_counts}
    if debug_sections: print('DEBUG morphCount',moc,'offset',r.o,file=sys.stderr)
    for _ in range(max(0,moc)):
        n=r.text()
        if debug_sections and len(morphs)<60: print('DEBUG morph name',len(morphs),n,'offset',r.o,'nextLen',r.d[r.o:r.o+4],file=sys.stderr)
        english=r.text();panel=r.u8();typ=r.u8();count=r.i32()
        if debug_sections and len(morphs)<60: print('DEBUG morph fields',len(morphs),n,english,'panel',panel,'type',typ,'count',count,'offset',r.o,file=sys.stderr)
        m={'name':n,'panel':panel,'type':morph_types.get(typ,str(typ)),'typeId':typ,'offsetCount':count}
        for _ in range(max(0,count)):
            if typ==0:r.idx(sizes[4]);r.f32()
            elif typ==1:r.idx(sizes[0]);r.take(12)
            elif typ==2:r.idx(sizes[3]);r.take(12+16)
            elif 3<=typ<=7:r.idx(sizes[0]);r.take(16)
            elif typ==8:r.idx(sizes[2]);r.u8();r.take(4*4+3*4+4+3*4+4+4*4+4*4+4*4)
            elif typ==9:r.idx(sizes[4]);r.f32()
            elif typ==10:r.idx(sizes[5]);r.u8();r.u8();r.vec3()
        if debug_sections and len(morphs)<60: print('DEBUG morph end',len(morphs),r.o,'expected?',file=sys.stderr)
        morphs.append(m)
    index_to_name={i:b['name'] for i,b in enumerate(bones)}
    for b in bones:
        if 'ikLinks' in b:b['ikLinkNames']=[index_to_name.get(x['index'],'<missing>') for x in b['ikLinks']]
        if 'ikTarget' in b:b['ikTargetName']=index_to_name.get(b['ikTarget'],'<missing>')
    standard=['センター','全ての親','上半身','上半身2','下半身','左足','右足','左ひざ','右ひざ','左足首','右足首','左つま先','右つま先','左足ＩＫ','右足ＩＫ','左つま先ＩＫ','右つま先ＩＫ','左腕','右腕','左ひじ','右ひじ','左手首','右手首','頭','首','両目']
    names={b['name'] for b in bones}
    return {'path':str(path),'modelName':model_name,'englishName':english_name,'version':version,'vertexCount':vx,'textureCount':len(textures),'materialCount':mc,'boneCount':bc,'morphCount':moc,'bones':bones,'morphs':morphs,'ikBones':[b['name'] for b in bones if 'ikLinks' in b],'standardBones':{n:n in names for n in standard},'morphTypeCounts':{k:sum(1 for m in morphs if m['type']==k) for k in set(m['type'] for m in morphs)},'vertexSkinWeightTypes':weight_type_counts,'footBoneVertexWeightTypes':foot_weight_type_counts}

if __name__=='__main__':
    output=[]
    for arg in sys.argv[1:]:
        if not arg.startswith('--'): output.append(parse(arg))
    print(json.dumps(output,ensure_ascii=False,indent=2))
