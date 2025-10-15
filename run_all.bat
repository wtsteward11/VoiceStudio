@echo off
setlocal
cd /d %~dp0
pwsh -NoProfile -ExecutionPolicy Bypass -File ".\tools\run_all.ps1" -RunGui
pause
