@echo off
REM Folio native GUI build - MinGW-w64 gcc, -O0 on purpose.
REM -O0 keeps the license check as a plain  cmp / test / jcc  sequence so the
REM booth patch is exactly ONE byte (0x75 -> 0x74). Do NOT "optimize".
REM -mwindows gives a real windowed app (no console window).
setlocal
cd /d "%~dp0"

set "GCC="
for /f "delims=" %%G in ('where gcc 2^>nul') do if not defined GCC set "GCC=%%G"
if not defined GCC (
  set "GCC=%LOCALAPPDATA%\Microsoft\WinGet\Packages\BrechtSanders.WinLibs.POSIX.UCRT_Microsoft.Winget.Source_8wekyb3d8bbwe\mingw64\bin\gcc.exe"
)

set "OUT=%~1"
if not defined OUT set "OUT=%~dp0..\..\final-dist\Folio"
if not exist "%OUT%" mkdir "%OUT%"
"%GCC%" -std=c11 -Wall -Wextra -O0 -fno-stack-protector -mwindows -o "%OUT%\PatchMe.exe" src\patchme.c -lcomdlg32 -lcomctl32 -lgdi32
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
REM Strip debug sections only (keep function symbols for the booth).
REM NOTE: this shifts .text in the file, so the documented patch file-offset
REM (README.md) is only valid for the stripped binary built here.
for %%F in ("%GCC%") do set "GBIN=%%~dpF"
"%GBIN%strip.exe" --strip-debug "%OUT%\PatchMe.exe"
if errorlevel 1 (
  echo STRIP FAILED
  exit /b 1
)
echo.
echo Built: %OUT%\PatchMe.exe
echo Re-derive the patch offset with: python scripts\verify.py
