"""Prove R1 only changed approved namespace/include text and preserved asset GUID bytes."""
from pathlib import Path
import hashlib
import json
import subprocess

ROOT = Path(__file__).resolve().parents[3]
BASE = 'bddd8268'
TEXT = {'.cs', '.hlsl', '.compute', '.shader', '.raytrace', '.hlsli'}


def rewrite(data):
    return data.replace(b'lilToon.URP.Extensions.Editor.RayTracing', b'lilToon.URP.Extensions.Editor.RT').replace(
        b'lilToon.URP.Extensions.RayTracing', b'lilToon.URP.Extensions.RT').replace(b'/RayTracing/', b'/RT/').replace(
            b'Hidden/lilToon/URP/RT/PrimarySurfaceValidation', b'Hidden/lilToon/URP/RayTracing/PrimarySurfaceValidation')


def destination(name):
    for side in ('Runtime', 'Editor'):
        if name == f'{side}/RayTracing.meta':
            return f'{side}/RT.meta'
        if name.startswith(f'{side}/RayTracing/'):
            return name.replace(f'{side}/RayTracing/', f'{side}/RT/', 1)
    return name


def main():
    names = subprocess.check_output(['git', 'ls-tree', '-r', '--name-only', '-z', BASE, '--', 'Runtime', 'Editor'], cwd=ROOT).decode().split('\0')
    names = list(filter(None, names))
    # Read source blobs in one process; thousands of per-file git invocations
    # obscure progress and add no verification strength.
    blob_input = ''.join(f'{BASE}:{name}\n' for name in names).encode()
    blob_data = subprocess.run(['git', 'cat-file', '--batch'], input=blob_input, stdout=subprocess.PIPE, check=True, cwd=ROOT).stdout
    cursor = 0
    before_blobs = {}
    for name in names:
        header_end = blob_data.index(b'\n', cursor)
        header = blob_data[cursor:header_end].split()
        size = int(header[2])
        cursor = header_end + 1
        before_blobs[name] = blob_data[cursor:cursor+size]
        cursor += size + 1
    baseline = json.loads((ROOT/'Tests~/RT/R0/baseline.json').read_text(encoding='utf-8'))
    metadata_hashes = {x['path']:x['sha256'] for x in baseline['files'] if x['path'].endswith('.meta')}
    changed = []
    checked = 0
    guid_count = 0
    errors = []
    for name in names:
        before = before_blobs[name]
        target = destination(name)
        after_path = ROOT / target
        if not after_path.exists():
            errors.append('Missing: ' + target)
            continue
        after = after_path.read_bytes()
        # Git's working-tree EOL conversion is not a semantic change.
        expected = rewrite(before) if Path(name).suffix in TEXT else before
        normal = lambda b: b.replace(b'\r\n', b'\n')
        if normal(after) != normal(expected):
            errors.append('Unexpected content: ' + target)
        if target != name or normal(before) != normal(after):
            changed.append({'before': name, 'after': target, 'workingSha256': hashlib.sha256(after).hexdigest()})
            if name.endswith('.meta'):
                guid_count += 1
                if hashlib.sha256(after).hexdigest() != metadata_hashes[name]:
                    errors.append('Metadata bytes changed: ' + target)
        checked += 1
    for side in ('Runtime', 'Editor'):
        if (ROOT / side / 'RayTracing').exists() or (ROOT / side / 'RayTracing.meta').exists():
            errors.append('Old directory or metadata remains: ' + side)
    result = {'stage':'R1','baseCommit':BASE,'trackedProductFilesChecked':checked,
              'movedMetadataByteIdentical':guid_count,'approvedChanges':changed,'errors':errors,
              'scope':'Approved text/path migration only; GPU behavior is checked separately.'}
    output = Path(__file__).with_name('migration.json')
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(f'R1 migration: {checked} files, {len(changed)} approved changes, {guid_count} metadata preserved, {len(errors)} errors')
    if errors:
        raise SystemExit('\n'.join(errors))


if __name__ == '__main__':
    main()
