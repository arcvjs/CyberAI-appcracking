$ErrorActionPreference='Stop'
$source=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../final-dist/Afterlight/App'))
$package=Join-Path ([IO.Path]::GetTempPath()) ('afterlight-first-launch-'+[guid]::NewGuid().ToString('N'))
Copy-Item -LiteralPath $source -Destination $package -Recurse
$launcher=$null
try {
    $launcher=Start-Process -FilePath (Join-Path $package 'Vapor.exe') -WorkingDirectory $package -WindowStyle Hidden -PassThru
    $ready=$false
    for($i=0;$i -lt 40;$i++) {
        Start-Sleep -Milliseconds 500
        if($launcher.HasExited){throw 'Vapor exited during first launch'}
        try { $health=Invoke-RestMethod 'http://127.0.0.1:47838/health' -TimeoutSec 1; if($health.status -eq 'ok'){$ready=$true;break} } catch {}
    }
    if(!$ready){throw 'First launch did not start the service'}
    $body=@{account='festivalgoer';password='vapor';deviceId='first-launch-check';deviceName='First launch check'} | ConvertTo-Json
    $login=Invoke-RestMethod 'http://127.0.0.1:47838/api/v1/login' -Method Post -ContentType 'application/json' -Body $body
    if(!$login.sessionToken){throw 'Demo account was not initialized'}
    Write-Output 'PASS fresh package starts Vapor and creates a usable local demo account without seed files'
} finally {
    if($launcher -and !$launcher.HasExited){$launcher.CloseMainWindow() | Out-Null;if(!$launcher.WaitForExit(5000)){$launcher.Kill()}}
}
