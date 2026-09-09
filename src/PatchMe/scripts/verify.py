"""Derive the release patch site; optionally test real converter code and a patched copy.

No third-party Python packages. The release binary is never modified.
"""
import argparse
import hashlib
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def tool(name):
    found = shutil.which(name)
    if found:
        return found
    fallback = Path(os.environ['LOCALAPPDATA']) / 'Microsoft/WinGet/Packages/BrechtSanders.WinLibs.POSIX.UCRT_Microsoft.Winget.Source_8wekyb3d8bbwe/mingw64/bin' / (name + '.exe')
    if not fallback.is_file():
        raise SystemExit(f'{name} not found. Put MinGW-w64 bin on PATH.')
    return str(fallback)


def patch_site(binary):
    data = binary.read_bytes()
    dis = subprocess.check_output([tool('objdump'), '-d', '-M', 'intel', '--disassemble=can_export_pro', str(binary)], text=True)
    matches = re.findall(r'^\s*([0-9a-f]+):\s+75\s+([0-9a-f]{2})\s+jne\s', dis, re.M)
    if len(matches) != 1 or '<g_licensed>' not in dis:
        raise AssertionError('Expected one short JNE in can_export_pro; inspect the new build.')
    va = int(matches[0][0], 16)
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    machine, sections = struct.unpack_from('<HH', data, pe+4)
    assert machine == 0x8664, 'Expected x64 PE'
    optional_size = struct.unpack_from('<H', data, pe+20)[0]
    image_base = struct.unpack_from('<Q', data, pe+24+24)[0]
    rva = va-image_base
    for i in range(sections):
        pos = pe+24+optional_size+i*40
        vsize, vrva, raw_size, raw_offset = struct.unpack_from('<IIII', data, pos+8)
        if vrva <= rva < vrva+raw_size:
            offset = raw_offset+rva-vrva
            assert data[offset] == 0x75 and data[offset-2:offset] == b'\x85\xc0'
            assert data[offset+1] == int(matches[0][1], 16)
            return offset, va, data
    raise AssertionError('Patch address not mapped to a file section')


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--test', action='store_true', help='compile/run tests and an inverted test binary in a temporary directory')
    ap.add_argument('--patched-copy', type=Path, help='create a new one-byte patched release copy for local booth QA')
    args = ap.parse_args()
    binary = ROOT.parents[1] / 'final-dist/Folio/PatchMe.exe'
    off, va, data = patch_site(binary)
    print(f'Folio: {len(data):,} bytes; SHA-256 {hashlib.sha256(data).hexdigest()}')
    print(f'can_export_pro: VA 0x{va:X}, file offset 0x{off:X}; {data[off]:02X} {data[off+1]:02X}')
    print('Patch 75 -> 74 for FREE-only export; 75 -> EB for unconditional export.')
    if args.patched_copy:
        target = args.patched_copy.resolve()
        if target.exists() or target == binary.resolve():
            raise SystemExit('Refusing to overwrite an existing file.')
        patched = bytearray(data); patched[off] = 0x74
        assert sum(a != b for a,b in zip(data,patched)) == 1
        target.write_bytes(patched)
        print(f'Created {target}')
    if args.test:
        with tempfile.TemporaryDirectory(prefix='folio-tests-') as tmp:
            exe = Path(tmp) / 'core.exe'
            subprocess.run([tool('gcc'), '-std=c11', '-Wall', '-Wextra', '-Werror', '-O0', '-fno-stack-protector', '-mconsole',
                str(ROOT/'tests/core.c'), '-o', str(exe), '-lcomdlg32', '-lcomctl32', '-lgdi32'], check=True)
            subprocess.run([str(exe), 'original', tmp], check=True)
            testoff, _, testdata = patch_site(exe)
            patched = bytearray(testdata); patched[testoff] = 0x74
            testexe = Path(tmp)/'core-patched.exe'; testexe.write_bytes(patched)
            subprocess.run([str(testexe), 'inverted', tmp], check=True)
        print('PASS: release symbol/offset verification and both test binaries.')


if __name__ == '__main__':
    main()
