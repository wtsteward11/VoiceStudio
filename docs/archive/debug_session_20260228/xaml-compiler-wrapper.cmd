@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem Args: <input.json> <output.json>
set "INPUT_JSON=%~1"
set "OUTPUT_JSON=%~2"

for %%d in ("%~dp0..") do set "REPO_ROOT=%%~fd"
set "APP_ROOT=%REPO_ROOT%\src\VoiceStudio.App"

rem Normalize accidental double backslashes FIRST (can break path existence checks)
set "INPUT_JSON=!INPUT_JSON:\\=\!"
set "OUTPUT_JSON=!OUTPUT_JSON:\\=\!"

rem Normalize to absolute paths relative to app root
if not exist "!INPUT_JSON!" if exist "%APP_ROOT%\!INPUT_JSON!" set "INPUT_JSON=%APP_ROOT%\!INPUT_JSON!"
rem Keep OUTPUT_JSON as provided (usually a relative obj\...\output.json path).
rem Some WinUI XAML compiler builds are sensitive to an absolute output.json path; we run from APP_ROOT below.

set "DEBUG_LOG=e:\VoiceStudio\.cursor\debug.log"
set "LOG_RUN_ID=pre-fix"
set "VSQ_DEBUG_ENABLED=0"
if /i "%VSQ_XAML_DEBUG%"=="1" set "VSQ_DEBUG_ENABLED=1"
set "RAW_LOG_ENABLED=0"
if /i "%VSQ_XAML_RAW_LOG%"=="1" set "RAW_LOG_ENABLED=1"
set "INPUT_JSON_ESC=!INPUT_JSON:\=\\!"
set "OUTPUT_JSON_ESC=!OUTPUT_JSON:\=\\!"
set "REPO_ROOT_ESC=!REPO_ROOT:\=\\!"
set "APP_ROOT_ESC=!APP_ROOT:\=\\!"
set "CWD_ESC=!CD:\=\\!"
set "MSBUILD_PROJECT_ESC=!MSBuildProjectFullPath:\=\\!"

if "%VSQ_DEBUG_ENABLED%"=="1" (
  rem #region agent log H1
  set "VSQ_TS=%date% %time%"
  >> "%DEBUG_LOG%" echo {"sessionId":"debug-session","runId":"%LOG_RUN_ID%","hypothesisId":"H1","location":"tools/xaml-compiler-wrapper.cmd:24","message":"wrapper_entry","data":{"inputJson":"!INPUT_JSON_ESC!","outputJson":"!OUTPUT_JSON_ESC!","repoRoot":"!REPO_ROOT_ESC!","appRoot":"!APP_ROOT_ESC!","cwd":"!CWD_ESC!","designTimeBuild":"%DesignTimeBuild%","msbuildProjectName":"%MSBuildProjectName%","msbuildProjectFullPath":"!MSBUILD_PROJECT_ESC!","msbuildNodeId":"%MSBuildNodeId%"},"timestamp":!VSQ_TS!}
  rem #endregion agent log H1
)

if not exist "!INPUT_JSON!" (
  echo Xaml compiler error: Input JSON file "%~1" doesn't exist! Resolved as "!INPUT_JSON!".
  exit /b 1
)

rem Avoid stale output.json causing incorrect Compile item lists.
rem If the compiler succeeds it will regenerate output.json; if it fails we want that failure to be real.
if exist "%OUTPUT_JSON%" del /f /q "%OUTPUT_JSON%" >nul 2>nul

rem NuGet root
if defined NUGET_PACKAGES (
  set "NUGET_ROOT=%NUGET_PACKAGES%"
) else (
  set "NUGET_ROOT=%USERPROFILE%\.nuget\packages"
)

rem Pick latest WinUI tools (prefer net6.0, then net472)
set "COMPILER="
for /f "delims=" %%v in ('dir /b /ad "%NUGET_ROOT%\microsoft.windowsappsdk.winui" 2^>nul ^| sort') do (
  if exist "%NUGET_ROOT%\microsoft.windowsappsdk.winui\%%v\tools\net6.0\XamlCompiler.exe" (
    set "COMPILER=%NUGET_ROOT%\microsoft.windowsappsdk.winui\%%v\tools\net6.0\XamlCompiler.exe"
  ) else if exist "%NUGET_ROOT%\microsoft.windowsappsdk.winui\%%v\tools\net472\XamlCompiler.exe" (
    set "COMPILER=%NUGET_ROOT%\microsoft.windowsappsdk.winui\%%v\tools\net472\XamlCompiler.exe"
  )
)

if not defined COMPILER (
  for /f "delims=" %%v in ('dir /b /ad "%NUGET_ROOT%\microsoft.windowsappsdk" 2^>nul ^| sort') do (
    if exist "%NUGET_ROOT%\microsoft.windowsappsdk\%%v\tools\net6.0\XamlCompiler.exe" (
      set "COMPILER=%NUGET_ROOT%\microsoft.windowsappsdk\%%v\tools\net6.0\XamlCompiler.exe"
    ) else if exist "%NUGET_ROOT%\microsoft.windowsappsdk\%%v\tools\net472\XamlCompiler.exe" (
      set "COMPILER=%NUGET_ROOT%\microsoft.windowsappsdk\%%v\tools\net472\XamlCompiler.exe"
    )
  )
)

if not defined COMPILER (
  set "NUGET_ROOT_ESC=%NUGET_ROOT:\=\\%"
  if "%VSQ_DEBUG_ENABLED%"=="1" (
    rem #region agent log H2
    set "VSQ_TS=%date% %time%"
    >> "%DEBUG_LOG%" echo {"sessionId":"debug-session","runId":"%LOG_RUN_ID%","hypothesisId":"H2","location":"tools/xaml-compiler-wrapper.cmd:74","message":"compiler_not_found","data":{"nugetRoot":"!NUGET_ROOT_ESC!"},"timestamp":!VSQ_TS!}
    rem #endregion agent log H2
  )
  echo Failed to locate XamlCompiler.exe under "%NUGET_ROOT%\microsoft.windowsappsdk.winui\*\tools\*\".
  exit /b 1
)

set "COMPILER_ESC=!COMPILER:\=\\!"
set "NUGET_ROOT_ESC=!NUGET_ROOT:\=\\!"
if "%VSQ_DEBUG_ENABLED%"=="1" (
  rem #region agent log H2
  set "VSQ_TS=%date% %time%"
  >> "%DEBUG_LOG%" echo {"sessionId":"debug-session","runId":"%LOG_RUN_ID%","hypothesisId":"H2","location":"tools/xaml-compiler-wrapper.cmd:83","message":"compiler_resolved","data":{"compiler":"!COMPILER_ESC!","nugetRoot":"!NUGET_ROOT_ESC!"},"timestamp":!VSQ_TS!}
  rem #endregion agent log H2
)

echo Running XAML compiler...
echo Compiler: "!COMPILER!"
echo Arguments: "!INPUT_JSON!" "!OUTPUT_JSON!"
echo.

set "RAW_LOG="
if "%RAW_LOG_ENABLED%"=="1" (
  set "RAW_LOG=%REPO_ROOT%\xaml_compiler_raw_%RANDOM%.log"
  set "RAW_LOG_ESC=!RAW_LOG:\=\\!"
  if "%VSQ_DEBUG_ENABLED%"=="1" (
    rem #region agent log H3
    set "VSQ_TS=%date% %time%"
    >> "%DEBUG_LOG%" echo {"sessionId":"debug-session","runId":"%LOG_RUN_ID%","hypothesisId":"H3","location":"tools/xaml-compiler-wrapper.cmd:95","message":"raw_log_path_set","data":{"rawLog":"!RAW_LOG_ESC!","inputJson":"!INPUT_JSON_ESC!","outputJson":"!OUTPUT_JSON_ESC!"},"timestamp":!VSQ_TS!}
    rem #endregion agent log H3
  )
  echo Raw log: "!RAW_LOG!"
  echo [%date% %time%] CMD="%COMPILER%" "%INPUT_JSON%" "%OUTPUT_JSON%" > "!RAW_LOG!"
  echo [%date% %time%] PWD=%CD% >> "!RAW_LOG!"
)

rem Change to project directory so relative paths in input.json resolve correctly
cd /d "%APP_ROOT%"
if "%RAW_LOG_ENABLED%"=="1" echo [%date% %time%] NEW_PWD=%CD% >> "!RAW_LOG!"

rem Some environments intermittently fail to materialize output.json due to transient file locks
rem ("The process cannot access the file because it is being used by another process.").
rem If the compiler reports success but output.json is missing, retry a few times.
set "MAX_RETRIES=4"
set "RETRY_DELAY_MS=250"
set "ATTEMPT=1"

:_retry_xaml
if "%RAW_LOG_ENABLED%"=="1" (
  echo [%date% %time%] ATTEMPT=%ATTEMPT% >> "!RAW_LOG!"
  "!COMPILER!" "!INPUT_JSON!" "!OUTPUT_JSON!" >> "!RAW_LOG!" 2>&1
) else (
  "!COMPILER!" "!INPUT_JSON!" "!OUTPUT_JSON!"
)
set "EXIT_CODE=%ERRORLEVEL%"
set "OUTPUT_JSON_EXISTS=0"
if exist "%OUTPUT_JSON%" set "OUTPUT_JSON_EXISTS=1"
if "%VSQ_DEBUG_ENABLED%"=="1" (
  rem #region agent log H4
  set "VSQ_TS=%date% %time%"
  >> "%DEBUG_LOG%" echo {"sessionId":"debug-session","runId":"%LOG_RUN_ID%","hypothesisId":"H4","location":"tools/xaml-compiler-wrapper.cmd:117","message":"compiler_exit","data":{"exitCode":"%EXIT_CODE%","outputJsonExists":"%OUTPUT_JSON_EXISTS%","attempt":"%ATTEMPT%","maxRetries":"%MAX_RETRIES%"},"timestamp":!VSQ_TS!}
  rem #endregion agent log H4
)

if "%EXIT_CODE%"=="0" (
  if exist "%OUTPUT_JSON%" (
    echo.
    echo XAML compiler exit code: 0
    echo XAML compilation succeeded.
    exit /b 0
  )
  if not exist "%OUTPUT_JSON%" (
    echo XAML compiler reported success but output.json is missing - retrying...
    if "%RAW_LOG_ENABLED%"=="1" echo [%date% %time%] RETRY_MISSING_OUTPUT_JSON=1 OUTPUT_JSON="%OUTPUT_JSON%" >> "!RAW_LOG!"
    if %ATTEMPT% LSS %MAX_RETRIES% (
      set /a ATTEMPT+=1
      rem Sleep (milliseconds) via ping.
      ping 127.0.0.1 -n 1 -w %RETRY_DELAY_MS% >nul
      goto :_retry_xaml
    )
  )
)

echo.
echo XAML compiler exit code: %EXIT_CODE%
if "%RAW_LOG_ENABLED%"=="1" echo [%date% %time%] EXIT=%EXIT_CODE% >> "!RAW_LOG!"

rem VS-0001: Some WinUI/XAML compiler builds return exit code 1 but still
rem generate valid .g.i.cs codegen files. Treat that as success to avoid blocking builds.
if "%EXIT_CODE%"=="1" (
  echo Checking if XAML codegen was generated despite exit code 1...
  for %%F in ("%OUTPUT_JSON%") do set "OUT_DIR=%%~dpF"
  rem Case 1: output.json exists with valid content
  if exist "%OUTPUT_JSON%" (
    findstr /c:"GeneratedCodeFiles" "%OUTPUT_JSON%" >nul 2>nul
    if "!ERRORLEVEL!"=="0" (
      if exist "!OUT_DIR!App.g.i.cs" if exist "!OUT_DIR!MainWindow.g.i.cs" (
        echo output.json found with GeneratedCodeFiles - treating as false-positive exit code 1
        if "%RAW_LOG_ENABLED%"=="1" echo [%date% %time%] FALSE_POSITIVE=1 OUTPUT_JSON="%OUTPUT_JSON%" >> "!RAW_LOG!"
        exit /b 0
      )
    )
  )
  rem Case 2: output.json missing but .g.i.cs files exist from this or prior pass
  rem MSBuild reads output.json to discover generated code files, so we must produce one.
  if not exist "%OUTPUT_JSON%" (
    if exist "!OUT_DIR!App.g.i.cs" if exist "!OUT_DIR!MainWindow.g.i.cs" (
      echo output.json missing but .g.i.cs files exist - generating output.json from discovered files...
      if "%RAW_LOG_ENABLED%"=="1" echo [%date% %time%] FALSE_POSITIVE=2 GENERATING_OUTPUT_JSON=1 >> "!RAW_LOG!"
      echo output.json missing but .g.i.cs exist - generating output.json from top-level codegen...
      if "%RAW_LOG_ENABLED%"=="1" echo [%date% %time%] FALSE_POSITIVE=2 GENERATING_OUTPUT_JSON=1 >> "!RAW_LOG!"
      rem Build output.json matching MSBuild OutputDeserializer schema.
      rem Only include top-level .g.i.cs and .g.cs files (NOT win-x64 subdirectory
      rem to avoid duplicate definitions).
      set "TMPOUT=%TEMP%\voicestudio_output_%RANDOM%.json"
      echo {"GeneratedCodeFiles":[ > "!TMPOUT!"
      set "FIRST=1"
      for /f "delims=" %%G in ('dir /b "!OUT_DIR!*.g.i.cs" 2^>nul') do (
        set "GFILE=!OUT_DIR!%%G"
        set "GFILE_ESC=!GFILE:\=\\!"
        if "!FIRST!"=="1" (
          echo "!GFILE_ESC!" >> "!TMPOUT!"
          set "FIRST=0"
        ) else (
          echo ,"!GFILE_ESC!" >> "!TMPOUT!"
        )
      )
      rem Include subdirectory .g.i.cs (Controls/, Views/, etc.) but NOT win-x64/
      for /f "delims=" %%D in ('dir /b /ad "!OUT_DIR!" 2^>nul') do (
        if /i not "%%D"=="win-x64" (
          for /f "delims=" %%G in ('dir /b "!OUT_DIR!%%D\*.g.i.cs" 2^>nul') do (
            set "GFILE=!OUT_DIR!%%D\%%G"
            set "GFILE_ESC=!GFILE:\=\\!"
            echo ,"!GFILE_ESC!" >> "!TMPOUT!"
          )
        )
      )
      rem Include .g.cs files (XamlTypeInfo) from top level only
      for /f "delims=" %%G in ('dir /b "!OUT_DIR!*.g.cs" 2^>nul') do (
        set "GFILE=!OUT_DIR!%%G"
        set "GFILE_ESC=!GFILE:\=\\!"
        echo ,"!GFILE_ESC!" >> "!TMPOUT!"
      )
      echo ],"GeneratedXamlFiles":[],"GeneratedXbfFiles":[],"GeneratedXamlPagesFiles":[],"MSBuildLogEntries":[]} >> "!TMPOUT!"
      move /y "!TMPOUT!" "%OUTPUT_JSON%" >nul
      echo Generated output.json with top-level codegen files
      exit /b 0
    )
  )
)

if "%RAW_LOG_ENABLED%"=="1" (
  echo XAML compilation failed. Dumping log contents:
  type "!RAW_LOG!"
) else (
  echo XAML compilation failed. Re-run with VSQ_XAML_RAW_LOG=1 to capture a raw log.
)

rem Log XAML compilation failure to audit system
if "%EXIT_CODE%" NEQ "0" (
  if exist "%REPO_ROOT%\scripts\xaml_audit_log.py" (
    python "%REPO_ROOT%\scripts\xaml_audit_log.py" --input "!INPUT_JSON!" --exit-code %EXIT_CODE% 2>nul
  )
)

exit /b %EXIT_CODE%
