@echo off
"%~dp0desktop\Ipc.Desktop.exe" --maintenance %*
exit /b %errorlevel%
