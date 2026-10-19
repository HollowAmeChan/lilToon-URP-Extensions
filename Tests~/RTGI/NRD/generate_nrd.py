"""Generate diffuse-only Unity adapters from the actual pinned NRD descriptor.
Vendor shader bodies remain untouched; use --check to verify derived files.
"""
import hashlib
import io
import json
from pathlib import Path
import re
import sys
import uuid

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / '.codex-research/python-nrd'))
from pcpp import Preprocessor

VENDOR = ROOT / 'Runtime/RTDI/ThirdParty/NRD'
SHADERS = ROOT / 'Runtime/RTGI/Shaders/NRD'
HOST = ROOT / 'Runtime/RTGI/NRD'
INVENTORY = json.loads((Path(__file__).parent / 'inventory.json').read_text())
CHECK = '--check' in sys.argv
BINDINGS = (ROOT / 'Runtime/RTDI/Shaders/NRD/HoRTDINrdBindings.hlsl').read_text()


def write(path, text):
    if CHECK:
        assert path.read_text() == text, f'Generated content mismatch: {path}'
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8', newline='\n')
    meta = Path(str(path) + '.meta')
    if not meta.exists():
        meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\n', encoding='utf-8')


def main():
    assert INVENTORY['version'] == [4, 17, 3] and INVENTORY['denoiserType'] == 10
    manifests = []
    for index, pipeline in enumerate(INVENTORY['pipelines']):
        parts = pipeline['identifier'].split('|')
        source = parts[0]
        defines = '\n'.join('#define ' + value.replace('=', ' ', 1) for value in parts[1:])
        header = '#pragma kernel main\n#pragma use_dxc\n#pragma target 6.0\n#define NRD_INTERNAL 1\n#define NRD_COMPILER_DXC 1\n' + defines + '\n'
        wrapper = '// Original pinned NRD RELAX_DIFFUSE shader; no DenseDI body.\n' + header
        wrapper += '#include "../../../RTDI/Shaders/NRD/HoRTDINrdBindings.hlsl"\n'
        wrapper += '#include "../../../RTDI/ThirdParty/NRD/' + source + '"\n'
        write(SHADERS / f'HoRTGINrd{index:02}.compute', wrapper)
        resource_header = source.replace('.cs.hlsl', '.resources.hlsli')
        markers = BINDINGS.replace('#define NRD_INPUT(type,data,name,reg,index) type<data> name;', '#define NRD_INPUT(type,data,name,reg,index) HO_IN(name,index);')
        markers = markers.replace('#define NRD_OUTPUT(type,data,name,reg,index) type<data> name;', '#define NRD_OUTPUT(type,data,name,reg,index) HO_OUT(name,index);')
        body = header + markers + '\n#include "NRD.hlsli"\n'
        body += '#include "RELAX_Config.hlsli"\n#include "' + resource_header + '"\n' if (VENDOR / resource_header).exists() else '#include "' + source + '"\n'
        pre = Preprocessor(); pre.add_path(str(VENDOR)); pre.line_directive = None
        pre.parse(body); output = io.StringIO(); pre.write(output)
        names = {(kind, int(slot)): name for kind, name, slot in re.findall(r'HO_(IN|OUT)\s*\(\s*(\w+)\s*,\s*(\d+)\s*\)', output.getvalue())}
        ordered = [names[('IN' if kind == 0 else 'OUT', slot)] for kind, count in pipeline['ranges'] for slot in range(count)]
        manifests.append({'identifier': pipeline['identifier'], 'resources': ordered})
    lines = ['// Generated from actual pinned RELAX_DIFFUSE CPU descriptors and original resource headers.', 'namespace lilToon.URP.Extensions.RTGI.NRD { internal static class HoRTGINrdManifest {', 'internal const uint DenoiserType=10;', 'internal static readonly string[] Identifiers={']
    lines += [json.dumps(p['identifier']) + ',' for p in manifests]
    lines += ['};', 'internal static readonly string[][] Resources={']
    lines += ['new[]{' + ','.join(json.dumps(n) for n in p['resources']) + '},' for p in manifests]
    lines += ['};', '} }', '']
    write(HOST / 'HoRTGINrdManifest.cs', '\n'.join(lines))
    if not CHECK:
        for folder in (HOST, SHADERS):
            meta = Path(str(folder) + '.meta')
            if not meta.exists():
                meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\nfolderAsset: yes\n', encoding='utf-8')
    print('Verified' if CHECK else 'Generated', len(manifests), 'original diffuse-only kernels; no vendor modifications')


if __name__ == '__main__':
    main()
