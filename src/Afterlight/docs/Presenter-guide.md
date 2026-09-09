# SDK replacement demonstration

Open **OPEN DEMO.cmd** in the main folder. Everything runs offline.

1. Click **Launch game** while the tool says **Protected**. Vapor opens; click Play to show the expired trial. If the account was previously activated, use `final-dist/Afterlight/App/Presenter/DemoControl.exe expired` before presenting.
2. Close Vapor and Afterlight. Click **Apply replacement**. The tool backs up and verifies the original SDK, then replaces only `final-dist/Afterlight/App/Vaporworks.dll`.
3. Click **Launch game** again. Afterlight opens directly. Enter the arena and play briefly.
4. Close Afterlight. Click **Restore protection**, then **Launch game**. The original launcher flow returns.

Suggested narration: “The game asks a separate component whether play is allowed. We replaced that component with one that answers locally. The game executable did not change, and we did not create a valid signed license.”

The account license is not altered by Apply or Restore. The expired-trial state remains in the service. Backups are stored in `final-dist/Afterlight/App/.sdk-backup/`. The tool rejects unknown game/SDK builds and damaged backups. Close the game and launcher before changing files.

## Manual replacement

The presenter automates a reversible file replacement. To demonstrate the same original project manually, close Vapor and Afterlight, build `Vapor.Emulator/Vapor.Emulator.csproj`, and copy its `Vaporworks.dll` into `final-dist/Afterlight/App/` only after saving the original as `.sdk-backup/Vaporworks.dll`. Compare SHA-256 hashes before and after the copy. Launch `Afterlight.exe` directly while the replacement is present. Restore the backup with the game and launcher closed, then compare its hash to the original backup. For a prebuilt replacement and File Explorer steps, see section 4 of the root README.

This tool implements an original Afterlight-specific API replacement. It does not run SteamAutoCracker, Steamless, or any third-party emulator. It does not unpack executables or modify Steam games.

Build: `Source/Vapor/Build-Demo.ps1` after building the protected app. The standard `Build-Exhibition.ps1` also rebuilds this tool. The replacement is embedded inside the presenter executable; it is not installed until Apply is selected.

Checks: the presenter accepts `--self-test <absolute-report-path>` for an isolated copy test. It checks native startup and renewal, backup integrity, repeated Apply/Restore, unknown SDK rejection, and unchanged game bytes.
