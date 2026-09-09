# Exhibition presentation

Open `output/App-Cracking-12-Slides.pptx`, or use the [saved Canva presentation](https://www.canva.com/design/DAHUlXUeEFM/BJSNGnTt-4JgDcrqgoROgw/edit). See [current status](STATUS.md) for editing limitations.

- `assets/screenshots/` — original user-supplied demo screenshots used by the current deck.
- `scripts/build.mjs` — slide content, native text layouts, and speaker notes.
- `../../final-dist/Presentation/` — the final presentation retained for repository visitors.
- `render/` — optional WPF renderer for obtaining Prism previews.
- `revision-build/` — ignored intermediate PPTX, PNG previews, and validation receipts.

## Regenerate

The builder uses the OpenAI artifact-tool runtime; it is optional for building the applications. It requires Node.js with `@oai/artifact-tool` available and the presentation skill's `container_tools` validation helpers. Configure these paths in your local shell:

```powershell
$env:PRESENTATIONS_SKILL_DIR = 'C:/path/to/presentations/skills/presentations'
$env:PYTHON_EXECUTABLE = 'python'
node Exhibition-Deck/scripts/build.mjs
```

Run from the repository root. Output paths are resolved relative to the script, and screenshots are read from the repository rather than Windows Temp. The final PPTX is included so the deck can be used without this authoring runtime.
