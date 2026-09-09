# Afterlight / Vapor exhibition

Double-click **Vapor.exe**. The local exhibition service starts in the background and the mock Festival Goer account signs in automatically. Select **PLAY** to see **Your trial has ended**. Everything runs on this Windows PC; no internet, Steam account, installation, or separately installed .NET runtime is needed for the published package.

Vapor is an original mock launcher. Purchases are simulations and never collect payment. The game, launcher, protocol, and backend were created for this exhibition.

## Presenter controls

From the `Presenter` folder:

```text
DemoControl.exe expired       Restore the expired-trial exhibit
DemoControl.exe owned         Grant the demo account a full license
DemoControl.exe trial 2       Start a two-minute trial
DemoControl.exe revoke        Revoke the demo license
DemoControl.exe status        Inspect the demo license
```

Click **LIBRARY** to refresh after a change. An open game renews its license every 20 seconds; a timed trial also expires locally while paused or in menus. Resetting a trial keeps the saved gameplay score. The simulated checkout's **Get full game** button also grants ownership.

The reset tool changes only this mock game's local service. It does not modify Steam, another game, the Windows clock, antivirus, or Windows settings.

## Play

With an owned or live trial license, press **PLAY** and then **Enter the arena**. Survive three waves (21 drones). Use cover to block their orange projectiles. Regular drones take two hits; heavier drones take three. Clearing a wave restores up to 30 health and replenishes the magazine. Reserve ammo is unlimited.

| Control | Action |
|---|---|
| WASD | Move |
| Mouse | Aim |
| Left mouse | Fire; hold for automatic fire |
| R | Reload |
| Shift | Sprint |
| Esc | Pause / resume |
| F11 | Borderless fullscreen |
| M | Toggle sound |

Requirements: 64-bit Windows 10/11, an OpenGL 3.3 capable graphics driver, keyboard and mouse. The game renders at 1600 × 900 internally and scales to the window. Artwork, geometry and sound effects are generated locally. Windows fonts are used with a bundled-engine fallback.

## Files and offline behavior

- `Vapor.exe`: launcher; starts the bundled local service and manages the library.
- `Afterlight.exe`: native game; opening it directly opens the Vapor library. Press Play in the launcher.
- `Vaporworks.dll`: inspectable game SDK and ownership gate.
- `Vapor.Licensing.dll`: protocol and cryptographic verification.
- `Service/`: the separate local reference backend.
- `Presenter/seed-data/`: isolated demo accounts and issuer material for the presenter service. This folder is not a production game distribution.
- `exhibition.json`: installation metadata, local service address, and issuer ID.

Vapor caches the last signed ownership ticket using Windows DPAPI. An owned game can start with a valid cached ticket when the local backend is stopped, as long as the launcher is running. The demo's offline ticket lease is seven days. Revocation cannot be learned while the issuer is unavailable; an existing cached license remains usable until its lease expires. The default expired trial never becomes playable merely by disconnecting.

Save data is under `%LOCALAPPDATA%\Afterlight`. The launcher stores its account cache and service state beneath `%LOCALAPPDATA%\Vapor`, separated by the signing authority's ID. Source builds and published exhibition builds have separate authorities.

## Build from source

Run `Build-Exhibition.ps1` from PowerShell 7 with the .NET 8 or later SDK installed. The script publishes the game, launcher, backend, and presenter controls to `../../App`. Package builds explicitly disable internal diagnostics.

For development checks, build `../Afterlight/Afterlight.csproj -c Release -p:EnableDiagnostics=true` and run its executable with `--self-test`, `--drm-test`, or `--verify`. These diagnostic dispatch paths are excluded from the exhibition build. `--verify` produces screenshots, not a playable release mode.

Read **DRM-ARCHITECTURE.md** for the Steam comparison, trust boundaries, the exhibition's attack surfaces, and deliberate limitations.

## Dependencies

The game uses [raylib-cs](https://github.com/raylib-cs/raylib-cs) and [raylib](https://www.raylib.com/), distributed under the zlib license. The launcher uses WPF and the bundled Microsoft .NET runtime. Microsoft runtime notices accompany the published binaries. No Steam SDK, Valve assets, or third-party game binaries are included.

