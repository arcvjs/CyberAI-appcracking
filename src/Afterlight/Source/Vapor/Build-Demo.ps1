$ErrorActionPreference='Stop'
$sourceBundle=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$bundle=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../final-dist/Afterlight'))
$app=Join-Path $bundle 'App'
$payload=Join-Path $PSScriptRoot 'artifacts\demo-payload'
$build=Join-Path $PSScriptRoot 'artifacts\demo-build'
& dotnet build (Join-Path $PSScriptRoot 'Vapor.Emulator\Vapor.Emulator.csproj') -c Release --artifacts-path $build -o $payload
if($LASTEXITCODE -ne 0){throw 'Emulator build failed'}
$manifest=@{Game=(Get-FileHash "$app\Afterlight.dll").Hash;Original=(Get-FileHash "$app\Vaporworks.dll").Hash;Replacement=(Get-FileHash "$payload\Vaporworks.dll").Hash}
if($manifest.Original -eq $manifest.Replacement){throw 'Restore the original SDK before building the tool'}
$manifest | ConvertTo-Json | Set-Content "$payload\manifest.json"
& dotnet publish (Join-Path $PSScriptRoot 'Vapor.DemoTool\Vapor.DemoTool.csproj') -c Release -r win-x64 --self-contained true --artifacts-path $build -o (Join-Path $bundle 'Demo') '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:EnableCompressionInSingleFile=true' '-p:DebugType=None' "-p:DemoPayload=$payload"
if($LASTEXITCODE -ne 0){throw 'Presenter tool build failed'}
Copy-Item -LiteralPath (Join-Path $sourceBundle 'docs/Presenter-guide.md') -Destination (Join-Path $bundle 'Demo/Presenter guide.md')
