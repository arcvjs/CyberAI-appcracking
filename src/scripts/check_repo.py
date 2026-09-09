"""Check tracked and unignored files before publishing; print paths, never secrets."""
from pathlib import Path
import re
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
result = subprocess.run(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'],
                        cwd=root, capture_output=True, check=True)
paths = sorted(set(p.decode('utf-8') for p in result.stdout.split(b'\0') if p))
problems = []
total = 0
private_key = re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----')
for name in paths:
    path = root / name
    if path.is_symlink():
        problems.append((name, 'symlink: review external dependencies'))
        continue
    if not path.is_file():
        continue
    size = path.stat().st_size
    total += size
    if size > 100 * 1024 * 1024:
        problems.append((name, 'over GitHub’s 100 MiB file limit'))
    parts = set(path.relative_to(root).parts)
    if parts & {'.local-licensing', '.local-steam', 'seed-data', '.sdk-backup', '.local-archive', 'bin', 'obj', 'node_modules'} or path.name == 'licenses.json':
        problems.append((name, 'generated or private runtime file'))
    release_binary = path.relative_to(root).parts[:1] == ('final-dist',)
    if path.suffix.lower() in {'.pem', '.pfx', '.p12', '.key', '.exe', '.dll'} and not release_binary:
        problems.append((name, 'credential or compiled binary'))
    if path.suffix.lower() in {'.cs', '.py', '.json', '.md', '.txt', '.ps1', '.mjs', '.pem', '.key', '.c'}:
        if private_key.search(path.read_bytes()):
            problems.append((name, 'private key material'))
for name, reason in problems:
    print(f'FAIL {name}: {reason}')
print(f'{len(paths)} repository files; {total / 1024 / 1024:.1f} MiB; {len(problems)} issue(s).')
sys.exit(bool(problems))
