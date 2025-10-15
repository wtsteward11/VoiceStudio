@echo off
setlocal
cd /d %~dp0
pwsh -NoProfile -ExecutionPolicy Bypass -File ".\tools\run_app.ps1" -App menu
pause
