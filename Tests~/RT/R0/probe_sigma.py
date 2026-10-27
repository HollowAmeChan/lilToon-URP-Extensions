"""Read pinned NRD CPU descriptors. Does not dispatch GPU work or modify Unity assets."""
import ctypes as c
import hashlib
import json
import platform
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DLL = ROOT / 'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll'
HEADER = ROOT / 'Runtime/RTDI/ThirdParty/NRD/Include/NRDDescs.h'
SETTINGS = ROOT / 'Runtime/RTDI/ThirdParty/NRD/Include/NRDSettings.h'


def enum_values(text, name):
    body = re.search(r'enum class ' + name + r'\s*:\s*uint\d+_t\s*\{(.*?)\};', text, re.S)
    if body is None:
        raise ValueError('Enum absent: ' + name)
    clean = re.sub(r'/\*.*?\*/|//[^\n]*', '', body[1], flags=re.S)
    return [x.strip() for x in clean.split(',') if x.strip()]


class Library(c.Structure):
    _fields_ = [('offsets', c.c_uint32 * 4), ('denoisers', c.POINTER(c.c_uint32)),
                ('count', c.c_uint32), ('major', c.c_uint8), ('minor', c.c_uint8),
                ('build', c.c_uint8), ('normal', c.c_uint8), ('roughness', c.c_uint8)]


class Denoiser(c.Structure):
    _fields_ = [('identifier', c.c_uint32), ('type', c.c_uint32)]


class Creation(c.Structure):
    _fields_ = [('callbacks', c.c_void_p * 4), ('denoisers', c.POINTER(Denoiser)), ('count', c.c_uint32)]


class Bytecode(c.Structure):
    _fields_ = [('data', c.c_void_p), ('size', c.c_uint64)]


class Range(c.Structure):
    _fields_ = [('type', c.c_uint32), ('count', c.c_uint32)]


class Pipeline(c.Structure):
    _fields_ = [('shaders', Bytecode * 3), ('ranges', c.POINTER(Range)), ('count', c.c_uint32),
                ('constants', c.c_bool), ('identifier', c.c_char * 256)]


class Texture(c.Structure):
    _fields_ = [('format', c.c_uint32), ('downsample', c.c_uint16)]


class Instance(c.Structure):
    _fields_ = [('registers', c.c_uint32 * 6), ('samplers', c.POINTER(c.c_uint32)),
                ('sampler_count', c.c_uint32), ('entry', c.c_char_p),
                ('pipelines', c.POINTER(Pipeline)), ('pipeline_count', c.c_uint32),
                ('permanent', c.POINTER(Texture)), ('permanent_count', c.c_uint32),
                ('transient', c.POINTER(Texture)), ('transient_count', c.c_uint32), ('pool', c.c_uint32 * 5)]


class SigmaSettings(c.Structure):
    _fields_ = [('light_direction', c.c_float * 3), ('plane_distance_sensitivity', c.c_float),
                ('max_stabilized_frame_num', c.c_uint32)]


def sha(path):
    return {'path': path.relative_to(ROOT).as_posix(), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}


def main():
    if platform.system() != 'Windows' or c.sizeof(c.c_void_p) != 8:
        raise RuntimeError('The shipped plugin requires a Windows x64 probe process')
    sizes = {name: c.sizeof(value) for name, value in
             [('library', Library), ('creation', Creation), ('pipeline', Pipeline),
              ('texture', Texture), ('instance', Instance), ('sigmaSettings', SigmaSettings)]}
    assert sizes == {'library': 40, 'creation': 48, 'pipeline': 320, 'texture': 8,
                     'instance': 112, 'sigmaSettings': 20}, sizes
    header = HEADER.read_text(encoding='utf-8-sig')
    methods = enum_values(header, 'Denoiser')
    formats = enum_values(header, 'Format')
    descriptor_types = enum_values(header, 'DescriptorType')
    settings_text = SETTINGS.read_text(encoding='utf-8-sig')
    assert 'float lightDirection[3]' in settings_text and 'uint32_t maxStabilizedFrameNum' in settings_text
    lib = c.WinDLL(str(DLL))
    lib.GetLibraryDesc.argtypes = []
    lib.GetLibraryDesc.restype = c.POINTER(Library)
    lib.CreateInstance.argtypes = [c.POINTER(Creation), c.POINTER(c.c_void_p)]
    lib.CreateInstance.restype = c.c_uint32
    lib.GetInstanceDesc.argtypes = [c.c_void_p]
    lib.GetInstanceDesc.restype = c.POINTER(Instance)
    lib.DestroyInstance.argtypes = [c.c_void_p]
    lib.DestroyInstance.restype = None
    library = lib.GetLibraryDesc().contents
    version = [library.major, library.minor, library.build]
    assert version == [4, 17, 3], version
    assert [library.normal, library.roughness] == [2, 1]
    assert 0 < library.count <= len(methods) - 1
    supported = [int(library.denoisers[i]) for i in range(library.count)]
    assert all(0 <= x < len(methods) - 1 for x in supported)
    report = {'scope': 'CPU-only library/instance descriptors; no Unity, GPU execution or image-quality validation.',
              'version': version, 'normalEncoding': library.normal, 'roughnessEncoding': library.roughness,
              'abiSizes': sizes, 'sources': [sha(DLL), sha(HEADER), sha(SETTINGS), sha(Path(__file__).resolve())],
              'supportedDenoisers': [{'value': i, 'name': methods[i]} for i in supported], 'sigma': []}
    for name in ('SIGMA_SHADOW', 'SIGMA_SHADOW_TRANSLUCENCY'):
        index = methods.index(name)
        assert index in supported, name + ' is not in supportedDenoisers'
        method = Denoiser(0, index)
        creation = Creation()
        creation.denoisers, creation.count = c.pointer(method), 1
        handle = c.c_void_p()
        result = int(lib.CreateInstance(c.byref(creation), c.byref(handle)))
        assert result == 0 and handle.value, (name, result)
        try:
            inst = lib.GetInstanceDesc(handle).contents
            assert inst.pipeline_count < 128 and inst.permanent_count < 128 and inst.transient_count < 128
            record = {'name': name, 'type': index, 'createResult': result, 'entry': inst.entry.decode(),
                      'registers': list(inst.registers), 'constantMax': int(inst.registers[5]),
                      'samplers': [int(inst.samplers[i]) for i in range(inst.sampler_count)],
                      'descriptorPool': list(inst.pool), 'pipelines': [], 'permanent': [], 'transient': []}
            for i in range(inst.pipeline_count):
                pipe = inst.pipelines[i]
                assert pipe.count <= 2
                record['pipelines'].append({'identifier': pipe.identifier.decode(), 'hasConstants': bool(pipe.constants),
                    'bytecodeBytes': {key: int(pipe.shaders[j].size) for j, key in enumerate(('dxbc', 'dxil', 'spirv'))},
                    'ranges': [{'type': descriptor_types[pipe.ranges[j].type], 'value': int(pipe.ranges[j].type),
                                'count': int(pipe.ranges[j].count)} for j in range(pipe.count)]})
            for key in ('permanent', 'transient'):
                for i in range(getattr(inst, key + '_count')):
                    tex = getattr(inst, key)[i]
                    record[key].append({'format': formats[tex.format], 'value': int(tex.format), 'downsample': int(tex.downsample)})
            report['sigma'].append(record)
            print(f'{name}: type={index}, pipelines={inst.pipeline_count}, permanent={inst.permanent_count}, transient={inst.transient_count}')
        finally:
            lib.DestroyInstance(handle)
    out = Path(__file__).with_name('sigma-inventory.json')
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'NRD {".".join(map(str, version))}: both SIGMA instances supported; wrote {out.relative_to(ROOT)}')


if __name__ == '__main__':
    main()
