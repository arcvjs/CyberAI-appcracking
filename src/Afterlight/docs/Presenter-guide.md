# Manual SDK replacement

1. Close Afterlight and Vapor.
2. Back up `App/Vaporworks.dll` in a separate folder.
3. Copy `SDK/Vaporworks.dll` over `App/Vaporworks.dll` using File Explorer.
4. Double-click **Play Afterlight.cmd**.
5. Close the game and restore the original DLL to return to license checking.

The replacement affects only the included Afterlight app. The game executable stays unchanged.

## Moving to another PC

Copy the entire Afterlight distribution, including App, Service (inside App), SDK, and the launch shortcuts. The first Vapor launch generates local keys and an expired demo account under the current user's LocalAppData/Vapor/Exhibition folder. No private seed files need to be transferred.

With the original SDK, open the library and use the simulated checkout to enable play. The checkout takes no payment. If startup fails, fully extract the package and check whether Windows or antivirus blocked App/Service/Vapor.Backend.exe. The local service uses port 47838; close any other Vapor instance first.
