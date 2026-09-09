@echo off
cd /d "%~dp0App"
if not exist "Afterlight.exe" (
  echo Missing App\Afterlight.exe. Extract the entire Afterlight folder first.
  pause
  exit /b 1
)
start "" "Afterlight.exe"
