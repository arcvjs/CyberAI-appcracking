# Afterlight Exhibition

Paths below are relative to the repository root.

Double-click **START HERE.cmd** to open the Vapor game library.

- **final-dist/Afterlight/App/** — generated portable Windows game and launcher. Keep the files together. Opening `Afterlight.exe` directly opens the launcher library; press Play there.
- **Source/** — current Afterlight game and Vapor launcher, licensing service, and tests.
- **docs/** — presenter guide included in source control. `App/` and `Demo/` are generated packages; rebuild them when the source changes.

The exhibition starts with an expired trial. Use the simulated checkout in the launcher, or run `final-dist/Afterlight/App/Presenter/DemoControl.exe owned` to enable play. Run the same tool with `expired` to reset the exhibit.

Build with PowerShell 7 from the repository root: `pwsh -File src/scripts/Build.ps1 -Project Afterlight`. Output goes to **final-dist/Afterlight/**.

Run integration checks: `pwsh -File src/scripts/Test.ps1`.

See **final-dist/Afterlight/App/DRM-ARCHITECTURE.md** for the local mock licensing design.

Re-run direct-launch checks with `pwsh -File src/Afterlight/Source/Vapor/Test-Bootstrap.ps1` from the repository root (close Vapor first). Generated previews and test reports are excluded from Git.

Old Aurora/AfterlightDRM prototypes and build caches are preserved locally under `../.local-archive/Afterlight-prototypes/`, excluded from Git. Build-Exhibition generates its own signing keys and matching public trust files. Local accounts, private keys, and packages are never required in source control.

For the SDK replacement demonstration, open **OPEN DEMO.cmd**. Read [the presenter guide](docs/Presenter-guide.md) for the short presentation script. The build also copies it into `Demo/`.

