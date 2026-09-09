$ErrorActionPreference = 'Stop'
$package = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\final-dist\Afterlight\App'))
$launcherPath = Join-Path $package 'Vapor.exe'
$gamePath = Join-Path $package 'Afterlight.exe'
if (Get-Process -Name Vapor -ErrorAction SilentlyContinue) { throw 'Close Vapor before running this isolated launch check.' }
function Launch-Direct {
    $info = [Diagnostics.ProcessStartInfo]::new($gamePath)
    $info.UseShellExecute = $false
    $info.WorkingDirectory = $package
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.Environment.Remove('VAPOR_LAUNCH_SECRET') | Out-Null
    $info.Environment.Remove('VAPOR_CLIENT_PID') | Out-Null
    $child = [Diagnostics.Process]::Start($info)
    if (!$child.WaitForExit(10000)) { $child.Kill(); throw 'Direct game launch did not return to launcher' }
    if ($child.ExitCode -ne 0) { throw 'Game bootstrap returned an error' }
    $child.Dispose()
}
$launcher = $null
try {
    Launch-Direct
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        $launcher = Get-Process -Name Vapor -ErrorAction SilentlyContinue | Where-Object Path -eq $launcherPath | Select-Object -First 1
        if ($launcher -and $launcher.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 150
    }
    if (!$launcher -or $launcher.MainWindowHandle -eq 0) { throw 'Launcher window did not open' }
    $firstId = $launcher.Id
    Start-Sleep -Seconds 2
    Launch-Direct
    Start-Sleep -Seconds 2
    $instances = @(Get-Process -Name Vapor -ErrorAction SilentlyContinue | Where-Object Path -eq $launcherPath)
    if ($instances.Count -ne 1 -or $instances[0].Id -ne $firstId) { throw 'Second direct launch did not reuse the existing launcher' }
    if (Get-Process -Name Afterlight -ErrorAction SilentlyContinue | Where-Object Path -eq $gamePath) { throw 'Game unexpectedly auto-started' }
    @('PASS Direct game launch exits successfully and opens the native launcher window.', 'PASS Repeated direct launch reuses the same launcher process.', 'PASS Library opens without automatically entering gameplay.') | Tee-Object -FilePath (Join-Path $package 'bootstrap-verification.txt')
} finally {
    if ($launcher -and !$launcher.HasExited) { $launcher.CloseMainWindow() | Out-Null; if (!$launcher.WaitForExit(5000)) { $launcher.Kill($true) } }
}
