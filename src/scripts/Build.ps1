param([ValidateSet('All','Prism','Afterlight','Folio')][string]$Project='All')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$distribution=Join-Path (Split-Path $repoRoot -Parent) 'final-dist'
Push-Location $repoRoot
try {
    if ($Project -in @('All','Prism')) {
        & dotnet publish Prism/Desktop/Prism.csproj -c Release --artifacts-path Prism/artifacts/release -o (Join-Path $distribution 'Prism') --self-contained false
        if ($LASTEXITCODE -ne 0) { throw 'Prism publish failed' }
    }
    if ($Project -in @('All','Afterlight')) {
        & ./Afterlight/Source/Vapor/Build-Exhibition.ps1
    }
    if ($Project -in @('All','Folio')) {
        & ./PatchMe/Build.bat (Join-Path $distribution 'Folio')
        if ($LASTEXITCODE -ne 0) { throw 'Folio build failed' }
    }
} finally { Pop-Location }
