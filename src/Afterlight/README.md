# Afterlight Exhibition

Paths below are relative to the repository root.

Double-click **START HERE.cmd** to open the Vapor game library.

- **final-dist/Afterlight/App/** — generated portable Windows game and launcher. Keep the files together. Opening `Afterlight.exe` directly opens the launcher library; press Play there.
- **Source/** — current Afterlight game and Vapor launcher, licensing service, and tests.
- **docs/** — manual DLL replacement guide. `App/` and `SDK/` are generated packages; rebuild them when the source changes.

The original SDK starts with an expired trial on a fresh installation. Follow the manual guide to back up and replace the DLL, then launch the game.

Build with PowerShell 7 from the repository root: `pwsh -File src/scripts/Build.ps1 -Project Afterlight`. Output goes to **final-dist/Afterlight/**.

Run integration checks: `pwsh -File src/scripts/Test.ps1`.

See **final-dist/Afterlight/App/DRM-ARCHITECTURE.md** for the local mock licensing design.

Re-run direct-launch checks with `pwsh -File src/Afterlight/Source/Vapor/Test-Bootstrap.ps1` from the repository root (close Vapor first). Generated previews and test reports are excluded from Git.

For manual SDK replacement, read [the guide](docs/Manual-DLL-replacement.md). Use **final-dist/Afterlight/Play Afterlight.cmd** to launch the game. Copy the entire distribution to another Windows x64 PC. First launch generates the local demo state automatically; private keys are never bundled.
