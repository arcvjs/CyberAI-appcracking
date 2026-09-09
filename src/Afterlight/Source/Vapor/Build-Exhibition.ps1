$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../final-dist/Afterlight'))
$package = Join-Path $outputRoot 'App'
$artifacts = Join-Path $PSScriptRoot 'artifacts\exhibition-build'
$seed = Join-Path $package 'Presenter\seed-data'
New-Item -ItemType Directory -Force $seed | Out-Null
function Run-Dotnet([string[]] $CommandArgs) {
    & dotnet @CommandArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $CommandArgs" }
}
Run-Dotnet @('publish', (Join-Path $PSScriptRoot 'Vapor.Backend\Vapor.Backend.csproj'), '-c','Release','-r','win-x64','--self-contained','true','--artifacts-path',$artifacts,'-o',(Join-Path $package 'Service'),'-p:DebugType=None')
$previousData=$env:VAPOR_DATA
$previousUrl=$env:VAPOR_URL
try {
    $env:VAPOR_DATA=$seed
    $env:VAPOR_URL='http://127.0.0.1:47838'
    & (Join-Path $package 'Service\Vapor.Backend.exe') --initialize
    if ($LASTEXITCODE -ne 0) { throw 'Seed initialization failed' }
} finally { $env:VAPOR_DATA=$previousData; $env:VAPOR_URL=$previousUrl }
Run-Dotnet @('publish',(Join-Path $workspace 'Afterlight\Afterlight.csproj'),'-c','Release','-r','win-x64','--self-contained','true','--artifacts-path',$artifacts,'-o',$package,'-p:EnableDiagnostics=false','-p:DebugType=None',('-p:VaporTrustFile='+ (Join-Path $seed 'vaporworks-trust.json')))
Run-Dotnet @('publish',(Join-Path $PSScriptRoot 'Vapor.Client\Vapor.Client.csproj'),'-c','Release','-r','win-x64','--self-contained','true','--artifacts-path',$artifacts,'-o',$package,'-p:DebugType=None',('-p:VaporTrustFile='+ (Join-Path $seed 'client-trust.json')))
Run-Dotnet @('publish',(Join-Path $PSScriptRoot 'Vapor.Presenter\Vapor.Presenter.csproj'),'-c','Release','-r','win-x64','--self-contained','true','--artifacts-path',$artifacts,'-o',(Join-Path $package 'Presenter'),'-p:PublishSingleFile=true','-p:EnableCompressionInSingleFile=true','-p:DebugType=None')
$trust=Get-Content -LiteralPath (Join-Path $seed 'client-trust.json') -Raw | ConvertFrom-Json
$issuer=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($trust.publicKey))).Substring(0,16)
@{version=1;appId=2937;serverUrl=$trust.serverUrl;issuerId=$issuer;edition='Local exhibition'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'exhibition.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $package 'READ ME.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DRM-ARCHITECTURE.md') -Destination $package
Write-Host "Ready: $package\Vapor.exe"

& (Join-Path $PSScriptRoot 'Build-Demo.ps1')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../launchers/START HERE.cmd') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../launchers/OPEN DEMO.cmd') -Destination $outputRoot
