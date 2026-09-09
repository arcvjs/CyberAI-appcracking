$ErrorActionPreference='Stop'
$sourceBundle=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$bundle=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../final-dist/Afterlight'))
$app=Join-Path $bundle 'App'
$payload=Join-Path $PSScriptRoot 'artifacts\demo-payload'
$build=Join-Path $PSScriptRoot 'artifacts\demo-build'
& dotnet build (Join-Path $PSScriptRoot 'Vapor.Emulator\Vapor.Emulator.csproj') -c Release --artifacts-path $build -o $payload -p:DebugType=None -p:DebugSymbols=false
if($LASTEXITCODE -ne 0){throw 'Emulator build failed'}
$sdkFolder=Join-Path $bundle 'SDK'
New-Item -ItemType Directory -Force -Path $sdkFolder | Out-Null
$target=Join-Path $sdkFolder 'Vaporworks.dll'
if (!(Test-Path -LiteralPath $target) -or (Get-FileHash $target).Hash -ne (Get-FileHash (Join-Path $payload 'Vaporworks.dll')).Hash) {
    Copy-Item -LiteralPath (Join-Path $payload 'Vaporworks.dll') -Destination $target -Force
}
