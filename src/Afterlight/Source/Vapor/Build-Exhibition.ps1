$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../final-dist/Afterlight'))
$package = Join-Path $outputRoot 'App'
$artifacts = Join-Path $PSScriptRoot 'artifacts\exhibition-build'
New-Item -ItemType Directory -Force $package | Out-Null
function Run-Dotnet([string[]] $CommandArgs) {
    & dotnet @CommandArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $CommandArgs" }
}
Run-Dotnet @('publish', (Join-Path $PSScriptRoot 'Vapor.Backend\Vapor.Backend.csproj'), '-c','Release','-r','win-x64','--self-contained','true','--artifacts-path',$artifacts,'-o',(Join-Path $package 'Service'),'-p:DebugType=None')
Run-Dotnet @('publish',(Join-Path $workspace 'Afterlight\Afterlight.csproj'),'-c','Release','-r','win-x64','--self-contained','true','--artifacts-path',$artifacts,'-o',$package,'-p:EnableDiagnostics=false','-p:DebugType=None')
Run-Dotnet @('publish',(Join-Path $PSScriptRoot 'Vapor.Client\Vapor.Client.csproj'),'-c','Release','-r','win-x64','--self-contained','true','--artifacts-path',$artifacts,'-o',$package,'-p:DebugType=None')
@{version=2;appId=2937;serverUrl='http://127.0.0.1:47838';edition='Local exhibition'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'exhibition.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $package 'READ ME.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DRM-ARCHITECTURE.md') -Destination $package
Write-Host "Ready: $package\Vapor.exe"

& (Join-Path $PSScriptRoot 'Build-SDK.ps1')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../launchers/START HERE.cmd') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../launchers/Play Afterlight.cmd') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../docs/Manual-DLL-replacement.md') -Destination (Join-Path $outputRoot 'Manual-DLL-replacement.md') -Force
