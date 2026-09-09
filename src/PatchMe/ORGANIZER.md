# Folio 2.0 / PatchMe - crew notes

Do not distribute this file, source, tests, scripts, or backups to visitors.

## Key and reset

The real key is `VXK9-QM27-TR4D-8HBW`.
The normalized ASCII key has djb2 hash `0x9BA8503C`.
`DEMO-DEMO-DEMO-0000` is intentionally invalid. djb2 is a weak 32-bit demo
hash, not production licensing. Do not describe guessing or collisions as infeasible.

Licenses retain compatibility with the old demo's plaintext file at
`%LOCALAPPDATA%\PatchMe\license.dat`. A valid license is loaded on startup.
Use **License > Remove** to return to FREE. Reset before each attempt, and
restart the visitor's patched copy after editing its executable.

## What changed

The old text-reversal toy is now Folio: a single-window document converter.
Free users can turn text or Markdown into styled HTML. Pro unlocks Word-compatible
RTF. The compact app has an editor, format picker and Convert button; activation
is tucked under License. The walkthrough is in README.md. One gate protects Word export.

## Reference solution

Supplied Folio 2.0 build, `can_export_pro`:

```asm
140001B78  mov eax, [g_licensed]
140001B7E  test eax, eax
140001B80  75 16  jne ALLOW_EXPORT
```

- File offset: **`0xF80`**. Original bytes: **`75 16`**.
- Change `75` to `74`: FREE can export RTF; licensed PRO is denied RTF.
- Change `75` to `EB`: both FREE and PRO can export RTF. HTML is always free.
- The badge remaining FREE is expected: the patch affects enforcement, not the label.
- The UTF-16 string beginning `PRO license required.` points to the same function.
- No second premium gate or proof-file check needs patching. A real exported
  RTF document is the proof. HTML is free and does not demonstrate bypassing Pro.

Do not use the old offsets `0x1006` or `0x115D`, function names, console menu
numbers, or `75 23` signature. They described previous versions.

## Build and verification

1. Close Folio before rebuilding; Windows locks a running executable.
2. Run `Build.bat` (MinGW-w64 GCC; current machine uses WinLibs 16.1).
3. Run `python scripts\verify.py --test`.
4. Record the printed SHA-256 and actual offset. Update this guide and README
   if the offset changes. Virtual addresses can also change across rebuilds.
5. For UI verification, make a fresh copy with:

   ```powershell
   python scripts\verify.py --patched-copy dist\PatchMe-PATCHED.exe
   ```

   This refuses to overwrite existing files and changes exactly one byte.
   Never hand out the already-patched copy as the starting challenge.

The verifier locates `can_export_pro` with objdump, requires one short `jne`
and a `g_licensed` reference, then maps its VA through the PE section table.
It does not depend on a global byte signature that might match unrelated code.

Tests check original and inverted gate behavior, valid/invalid keys, HTML
escaping, RTF Unicode and metacharacters, UTF-8 and UTF-16 import, malformed and
oversized input, maximum-size conversion, and actual file
writes/replacement. The test-only executable is built in a temporary directory;
the release app has no test-mode license bypass.

## Quick booth check

1. Start FREE. HTML conversion should succeed. Word (.rtf) should be blocked.
2. Patch a copy, launch it, type fresh text, and select Word (.rtf).
3. Confirm the native Save dialog opens and the exported file contains the
   fresh content with actual heading/list formatting. Inspect it locally.
4. Confirm HTML still works too. Leave the original executable intact.

The app writes only to user-selected export destinations and its license file.
Exports are staged in the destination directory before replacement; the Save
dialog confirms overwriting an existing destination. It never auto-opens output.

Original v1.2 backups: `dist\PatchMe-v1.2.exe.orig` and `src\patchme.c.orig`.
