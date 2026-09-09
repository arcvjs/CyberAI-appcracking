$ErrorActionPreference='Stop'
Write-Host 'Afterlight integration tests require a protected build. Close Vapor/game and restore the original SDK first.'
$repoRoot=Split-Path $PSScriptRoot -Parent
$distribution=Join-Path (Split-Path $repoRoot -Parent) 'final-dist'
Push-Location $repoRoot
try {
    & dotnet run --project Prism/Prism.Tests -c Release --artifacts-path Prism/artifacts/checks
    if ($LASTEXITCODE -ne 0) { throw 'Prism tests failed' }
    & python -m unittest discover -s Prism/server -p 'test_*.py'
    if ($LASTEXITCODE -ne 0) { throw 'Python server tests failed' }
    $app=Join-Path $distribution 'Afterlight/App'
    if (-not (Test-Path (Join-Path $app 'Afterlight.exe'))) { throw 'Build Afterlight before running integration tests' }
    & dotnet run --project Afterlight/Source/Vapor/Vapor.Tests -c Release -- $app
    if ($LASTEXITCODE -ne 0) { throw 'Afterlight tests failed' }
    & python PatchMe/scripts/verify.py --test
    if ($LASTEXITCODE -ne 0) { throw 'Folio verification failed' }
} finally { Pop-Location }
