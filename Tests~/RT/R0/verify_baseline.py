"""Verify the exact pre-migration RT sources; does not reinterpret old GPU runs."""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[3]
record = json.loads(Path(__file__).with_name('baseline.json').read_text(encoding='utf-8'))
errors = []
for item in record['files']:
    path = ROOT / item['path']
    if not path.exists() or hashlib.sha256(path.read_bytes()).hexdigest() != item['sha256']:
        errors.append(item['path'])
if errors:
    raise SystemExit('R0 source drift: ' + ', '.join(errors))
print(f'R0 baseline verified: {len(record["files"])} unchanged files; no new GPU claim.')
