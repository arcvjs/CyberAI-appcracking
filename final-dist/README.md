# Final distributions

This folder contains the release packages built from `src/`. Rebuild them with:

```powershell
pwsh -File ../src/scripts/Build.ps1
```

Packages:

- `Prism/` — native image editor.
- `Folio/` — Folio document converter (`PatchMe.exe`).
- `Afterlight/` — Vapor launcher, game, service, and manual replacement SDK.
- `Presentation/` — final 12-slide exhibition deck.

Do not copy private `seed-data`, license databases, or signing keys into a repository. See the root README for the demonstration walkthrough.
