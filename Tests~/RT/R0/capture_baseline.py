"""Record the RT migration baseline without launching Unity or changing assets."""
from pathlib import Path
import hashlib
import json
import subprocess

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = Path(__file__).with_name('baseline.json')
SCOPES = (
    'Runtime/RayTracing', 'Editor/RayTracing', 'Runtime/NRD',
    'Runtime/RTDI', 'Runtime/RTGI', 'Editor/RTDI', 'Editor/RTGI',
)


def main():
    names = subprocess.check_output(
        ['git', 'ls-files', '-z', '--', *SCOPES,
         'Runtime/RayTracing.meta', 'Editor/RayTracing.meta',
         'Runtime/jp.lilxyzw.liltoon.urp.extensions.Runtime.asmdef',
         'Editor/jp.lilxyzw.liltoon.urp.extensions.Editor.asmdef'], cwd=ROOT
    ).decode('utf-8').split('\0')
    files = []
    for name in sorted(filter(None, names)):
        data = (ROOT / name).read_bytes()
        files.append({'path': name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
    record = {
        'stage': 'R0',
        'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT).decode().strip(),
        'scope': 'Current RT production and validation source; no GPU result is inferred from these hashes.',
        'files': files,
        'acceptedEvidence': [
            {'path': 'Tests~/RenderingArchitecture/README.md', 'scope': 'Historical A01 ABI, independent histories and paired GPU/dispatch checks; old whole-tree guards are not current-tree passes.'},
            {'path': 'Tests~/RenderingReuse/README.md', 'scope': 'Historical A02 signal/publication parity, snapshots and Release checks.'},
            {'path': 'Tests~/RTGI/Backbuffer/README.md', 'scope': 'Existing bounded automatic-camera/Player validation; no full quality or performance acceptance.'},
            {'path': 'Tests~/CharacterShadow/Filtering-20261028/README.md', 'scope': 'Current raster shadow baseline; not RTShadow or SIGMA.'},
        ],
        'knownUnresolved': [
            'F39 AS instance-growth scratch diagnostic',
            'GI filtered local/full-image quality failures and complex dynamic motion',
            'Full 1080p frame budget/platform matrix',
            'New Ho-RT host, RTShadow and SIGMA production path are not implemented',
        ],
    }
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'R0 source baseline: {len(files)} files; commit {record["sourceCommit"][:8]}')


if __name__ == '__main__':
    main()
