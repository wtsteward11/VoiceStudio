@echo off
setlocal enabledelayedexpansion

set "XAML_EXE=C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk.winui\1.8.251105000\tools\net472\XamlCompiler.exe"
set "INPUT=%~1"
set "OUTPUT=%~2"
set "LOGFILE=E:\VoiceStudio\xaml_debug.log"

echo ============================================ >> "%LOGFILE%"
echo %DATE% %TIME% >> "%LOGFILE%"
echo Input: %INPUT% >> "%LOGFILE%"
echo Output: %OUTPUT% >> "%LOGFILE%"
echo. >> "%LOGFILE%"

"%XAML_EXE%" "%INPUT%" "%OUTPUT%" 1>> "%LOGFILE%" 2>&1

echo Exit code: %ERRORLEVEL% >> "%LOGFILE%"
exit /b %ERRORLEVEL%
