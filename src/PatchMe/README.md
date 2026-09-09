# Folio - Document Converter

A small, offline Windows app. Open or paste text, choose a format, and export.
`../../final-dist/Folio/PatchMe.exe` runs Folio; the filename stays the same for the exhibition.

| Feature | Free | Pro |
| --- | --- | --- |
| Open TXT / Markdown and edit | Yes | Yes |
| Convert to a styled web document (.html) | Yes | Yes |
| Export Word-compatible rich text (.rtf) | | Yes |

## Use the app

1. Run `../../final-dist/Folio/PatchMe.exe`. Try the short sample or click **Open .md / .txt**.
2. Edit the title and Markdown text.
3. Choose **HTML** (free) or **Word (.rtf)** (Pro), click **Convert**, and save.

The **License** button reveals the activation field and **Remove** button.

HTML and RTF support `#`, `##`, `###` headings, `-` / `*` bullet lists and `>`
quotes. Each ordinary source line becomes a paragraph. Other Markdown syntax
is kept literally. HTML input is escaped; scripts and remote resources are not loaded.
There is no TXT-to-TXT export. Both outputs apply actual document formatting.

Import accepts UTF-8 (with or without BOM) and UTF-16 LE with BOM, under 128K
UTF-16 code units after line-ending normalization. It does not import PDF,
DOCX, HTML, or RTF as formatted documents. Unicode paths and text are supported.
RTF opens in compatible word processors; HTML opens in a browser.

Ctrl+O opens a file. Ctrl+S converts to the selected format. The app confirms
before discarding edits that have not been converted. Keep your original
Markdown file if you need an editable source; converted output is not a source backup.

Requires 64-bit Windows 10/11. No installer, account, network, or extra runtime.

## Booth challenge: unlock Pro with one byte

This is intentionally simple licensing for a reverse-engineering exercise.
The objective is to export a real Word-compatible RTF document without entering a valid
key. Changing the FREE badge alone does not count.

1. Copy the executable to your lab folder. Keep the original.
2. Start in **FREE**. Choose **Word (.rtf)** and click Convert: it is locked.
3. Open your copy in IDA and find `can_export_pro` in the Functions window.
   Alternatively, find the UTF-16 string beginning `PRO license required.`
   and follow its cross-reference. Enable Unicode strings if needed.
4. The Word gate reads `g_licensed`, tests it, and branches with `jne`:

   ```asm
   mov   eax, [g_licensed]
   test  eax, eax
   jne   short ALLOW_EXPORT  ; 75 16 in the supplied build
   ```

5. **Edit > Patch program > Change byte**: change `75` to `74` (`jne` to `je`).
   Apply patches to your input copy, close IDA, then run that copy.
6. Choose Word (.rtf), enter fresh content, convert, and inspect the saved file.
   The badge can still say FREE. Word export now works.

For the supplied Folio 2.0 build, the opcode is at **file offset `0xF80`**,
VA `0x140001B80`. Verify the bytes are `75 16` before editing in HxD.
Offsets depend on the compiler and build. After rebuilding, run
`python scripts\verify.py` to derive the current site rather than guessing.

`75 -> 74` inverts the check: FREE succeeds and a licensed PRO session is
blocked. `75 -> EB` is an alternative one-byte patch that always allows export.
The free HTML path is unaffected by either patch.

Activation persists at `%LOCALAPPDATA%\PatchMe\license.dat`. **License > Remove**
returns to FREE. The crew should reset the license before each attempt.

## Development

`Build.bat` uses MinGW-w64 GCC with `-O0 -fno-stack-protector`, retaining
function symbols while stripping debug sections. It runs from any directory.
The existing WinLibs install is used if GCC is absent from PATH.

`python scripts\verify.py --test` compiles the real converter code in a
temporary test harness, checks conversion/Unicode/file handling and licensing,
and verifies a one-byte inversion on a copy of that test executable.
It also derives the gate offset from the release binary's symbol and PE sections.

Give visitors only `final-dist\Folio\PatchMe.exe` and this guide. Keep source, tests,
original backups and organizer notes in the crew kit.
