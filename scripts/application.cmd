@echo off
if "%~1"=="" (
  "%~dp0desktop\Ipc.Desktop.exe"
  exit /b
)
where pwsh.exe >nul 2>nul
if errorlevel 1 (
  echo PowerShell 7 is required. See WINDOWS-APPLICATION.md in this release.
  exit /b 2
)
pwsh.exe -NoProfile -File "%~dp0application.ps1" %*
exit /b %errorlevel%
