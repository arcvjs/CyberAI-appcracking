# Prism

Native Windows image editor. Run the following commands from this folder.

## Project layout

- `Desktop/` — WPF desktop application and bundled artwork.
- `server/` — current Python licensing servers and protocol tests.
- `Prism.Licensing/` — shared types and previous signed protocol, still referenced by the app/tests.
- `Prism.LicenseServer/` — previous signed-protocol server, retained for tests; not used by the current desktop client.
- `Prism.Tests/` — editor and licensing checks.
- `../../final-dist/Prism/` — published Windows application.
- `artifacts/` — generated build outputs and previews.

The earlier web prototype is preserved locally under `../.local-archive/Prism-web-prototype/` and excluded from Git. Generated builds and runtime credentials are also ignored.

## Build and run

```powershell
dotnet run --project Desktop/Prism.csproj
python server/local_demo_server.py
dotnet run --project Prism.Tests/Prism.Tests.csproj -c Release --artifacts-path artifacts/checks
dotnet publish Desktop/Prism.csproj -c Release --artifacts-path artifacts/release -o ../../final-dist/Prism --self-contained false
```

The desktop application requires Windows and the .NET 8 Desktop Runtime. Building requires the .NET SDK.

Read [server setup](server/README.md) before activating: the current client uses unsigned `POST /validate` JSON. The strict Python server rejects invalid keys; the localhost demo intentionally accepts any nonempty key. See [trial demo](docs/trial-demo.md) for editable trial state. Run Python tests with `python -m unittest discover -s server -p 'test_*.py'`.

