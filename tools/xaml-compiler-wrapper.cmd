@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem XAML compiler wrapper - handles two WinUI XAML compiler bugs:
rem 1. Path resolution: converts relative paths to absolute
rem 2. VS-0001: false-positive exit code 1 when codegen succeeds
rem 3. Daemon cleanup: kills XamlCompiler.exe after each invocation
rem    to release file locks on VoiceStudio.App.dll and references
rem Args: <input.json> <output.json>

set "INPUT_JSON=%~1"
set "OUTPUT_JSON=%~2"

rem Convert to absolute paths (XamlCompiler.exe requires this)
for %%F in ("!INPUT_JSON!") do set "INPUT_JSON=%%~fF"
for %%F in ("!OUTPUT_JSON!") do set "OUTPUT_JSON=%%~fF"

rem Find NuGet root
if defined NUGET_PACKAGES (set "NUGET_ROOT=!NUGET_PACKAGES!") else (set "NUGET_ROOT=%USERPROFILE%\.nuget\packages")

rem Locate XamlCompiler.exe (net472 only)
set "COMPILER="
for /f "delims=" %%v in ('dir /b /ad "!NUGET_ROOT!\microsoft.windowsappsdk.winui" 2^>nul ^| sort') do (
  if exist "!NUGET_ROOT!\microsoft.windowsappsdk.winui\%%v\tools\net472\XamlCompiler.exe" (
    set "COMPILER=!NUGET_ROOT!\microsoft.windowsappsdk.winui\%%v\tools\net472\XamlCompiler.exe"
  )
)

if not defined COMPILER (
  echo [xaml-wrapper] XAML compiler not found.
  exit /b 1
)

if not exist "!INPUT_JSON!" (
  echo [xaml-wrapper] Input not found: !INPUT_JSON!
  exit /b 1
)

echo [xaml-wrapper] Running: "!COMPILER!"
echo [xaml-wrapper] Input:   "!INPUT_JSON!"

"!COMPILER!" "!INPUT_JSON!" "!OUTPUT_JSON!"
set "XC_EXIT=!ERRORLEVEL!"

echo [xaml-wrapper] Exit code: !XC_EXIT!

rem ALWAYS kill daemon to release file locks (CS2012 prevention).
rem The next invocation will start a fresh daemon.
rem Using /t to terminate the process tree.
taskkill /f /im XamlCompiler.exe >nul 2>nul
rem Wait for file handles to be released
ping 127.0.0.1 -n 3 >nul

if "!XC_EXIT!"=="0" (
  echo [xaml-wrapper] Success.
  exit /b 0
)

rem VS-0001: false positive exit code 1
if "!XC_EXIT!"=="1" (
  if exist "!OUTPUT_JSON!" (
    findstr /c:"GeneratedCodeFiles" "!OUTPUT_JSON!" >nul 2>nul
    if !ERRORLEVEL! EQU 0 (
      echo [xaml-wrapper] VS-0001: false-positive. Returning 0.
      exit /b 0
    )
  )
  rem Phase 12 WS3: Retry once after 5s when output.json absent (common failure mode)
  if not exist "!OUTPUT_JSON!" (
    echo [xaml-wrapper] No output.json. Retrying in 5 seconds...
    ping 127.0.0.1 -n 6 >nul
    "!COMPILER!" "!INPUT_JSON!" "!OUTPUT_JSON!"
    set "XC_EXIT=!ERRORLEVEL!"
    if "!XC_EXIT!"=="0" (
      echo [xaml-wrapper] Retry succeeded.
      exit /b 0
    )
    if exist "!OUTPUT_JSON!" (
      findstr /c:"GeneratedCodeFiles" "!OUTPUT_JSON!" >nul 2>nul
      if !ERRORLEVEL! EQU 0 (
        echo [xaml-wrapper] VS-0001: false-positive on retry. Returning 0.
        exit /b 0
      )
    )
  )
)

echo [xaml-wrapper] Failed with exit code !XC_EXIT!
exit /b !XC_EXIT!
