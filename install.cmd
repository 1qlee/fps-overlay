@echo off
rem Double-click this to install FPS Overlay. It runs install.ps1 and keeps the
rem window open afterwards so any message can be read.
rem Pass -StartWithWindows or -Uninstall to forward them to install.ps1.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -NoPause %*
echo.
pause
