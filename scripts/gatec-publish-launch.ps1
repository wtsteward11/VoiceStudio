Param(
  [string]$Configuration = "Release",
  [string]$RuntimeIdentifier = "win-x64",
  [string]$BinlogPath = "",
  [string]$PublishDir = "",
  [int]$SmokeSeconds = 10,
  [switch]$UiSmoke,
  [int]$UiSmokeTimeoutSeconds = 120,
  [switch]$NoLaunch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "== Gate C publish+launch =="

# Ensure model root default is set
if (-not $env:VOICESTUDIO_MODELS_PATH) {
  $env:VOICESTUDIO_MODELS_PATH = "E:\VoiceStudio\models"
}

$repoRoot = Resolve-Path "$PSScriptRoot\.."
if ($repoRoot -is [System.Management.Automation.PathInfo]) {
  $repoRoot = $repoRoot.Path
}

if (-not $PublishDir -or $PublishDir.Trim() -eq "") {
  $PublishDir = Join-Path $repoRoot ".buildlogs\x64\$Configuration\gatec-publish"
}
if (-not $BinlogPath -or $BinlogPath.Trim() -eq "") {
  # Use a per-run binlog to avoid file-lock failures (MSB4104) when a previous binlog is
  # still open by another process (e.g. a binlog viewer/IDE).
  $runId = "$(Get-Date -Format 'yyyyMMdd-HHmmss')-$PID"
  $BinlogPath = Join-Path $repoRoot ".buildlogs\gatec-publish-$runId.binlog"
}
if ($SmokeSeconds -lt 1) { $SmokeSeconds = 1 }
if ($UiSmokeTimeoutSeconds -lt 1) { $UiSmokeTimeoutSeconds = 1 }

New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $BinlogPath -Parent) -Force | Out-Null
$publishDir = (Resolve-Path $PublishDir).Path
$latestInfoPath = Join-Path $repoRoot ".buildlogs\gatec-latest.txt"
$baseOutputPath = (Join-Path $repoRoot ".buildlogs") + "\"

# Publish (unpackaged apphost EXE)
$publishCmd = @(
  "dotnet", "publish", "$($repoRoot)\src\VoiceStudio.App\VoiceStudio.App.csproj",
  "-c", $Configuration,
  "-r", $RuntimeIdentifier,
  "-p:SelfContained=true",
  "-p:WindowsAppSDKSelfContained=false",
  "-p:UseAppHost=true",
  "-p:WindowsPackageType=None",
  "-p:EnableMsixTooling=false",
  "-p:EnableDefaultPriItems=false",
  # Gate C/H ship artifact relies on compiled XAML payload (.xbf). Ensure XBF generation is enabled
  # so publish output cannot retain stale XBFs when XAML changes.
  "-p:DisableXbfGeneration=false",
  "-p:BaseOutputPath=$baseOutputPath",
  "-p:PublishDir=$publishDir",
  "/bl:$BinlogPath",
  "/clp:ErrorsOnly;Summary"
)

$publishCmdForPrint = $publishCmd | ForEach-Object {
  if ($_ -match ";") { "`"$_`"" } else { $_ }
}
Write-Host "Publish: $($publishCmdForPrint -join ' ')"
& $publishCmd[0] @($publishCmd[1..($publishCmd.Length - 1)])
$publishExitCode = $LASTEXITCODE
if ($publishExitCode -ne 0) {
  Write-Host "Publish failed with exit code $publishExitCode. See binlog: $BinlogPath"
  $logLines = @(
    "Gate C publish",
    "Timestamp: $(Get-Date -Format o)",
    "Exe: <not produced>",
    "ExitCode: $publishExitCode",
    "Binlog: $(Resolve-Path $BinlogPath)"
  )
  $launchLog = Join-Path $publishDir "gatec-launch.log"
  $logLines | Set-Content -Path $launchLog
  @(
    "Timestamp: $(Get-Date -Format o)",
    "Binlog: $(Resolve-Path $BinlogPath)",
    "PublishDir: $publishDir",
    "Exe: <not produced>",
    "Result: publish_failed",
    "ExitCode: $publishExitCode"
  ) | Set-Content -Path $latestInfoPath
  exit $publishExitCode
}

# Locate apphost (avoid -Recurse; publish dir can be large)
$exePath = Join-Path $publishDir "VoiceStudio.App.exe"
if (-not (Test-Path $exePath)) {
  Write-Error "VoiceStudio.App.exe not found at $exePath"
}
$exe = Get-Item -LiteralPath $exePath


# Ensure WinUI resource indexes (.pri) are present.
# In our unpackaged lane, WinUI may fail early with:
#   XamlParseException: Cannot locate resource from 'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'
# unless these PRI files are present alongside the app.
$requiredWinUiPri = @(
  "Microsoft.UI.pri",
  "Microsoft.UI.Xaml.Controls.pri",
  "Microsoft.WindowsAppRuntime.pri",
  # App PRI is required for ms-appx resource resolution in the unpackaged lane.
  # Without it, WinUI can fail early with:
  #   XamlParseException: Cannot locate resource from 'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'
  "VoiceStudio.App.pri"
)

$priSourceDir = Join-Path $repoRoot ".buildlogs\\x64\\$Configuration\\net8.0-windows10.0.19041.0\\$RuntimeIdentifier\\publish"
$appPriSourcePath = Join-Path $repoRoot ".buildlogs\\x64\\$Configuration\\net8.0-windows10.0.19041.0\\$RuntimeIdentifier\\VoiceStudio.App.pri"
foreach ($priName in $requiredWinUiPri) {
  $dest = Join-Path $publishDir $priName
  if (Test-Path $dest) { continue }

  $src = $null
  if ($priName -eq "VoiceStudio.App.pri") {
    $src = $appPriSourcePath
  }
  else {
    $src = Join-Path $priSourceDir $priName
  }

  if (Test-Path $src) {
    Copy-Item -LiteralPath $src -Destination $dest -Force
  }
}

# Required files (unpackaged apps need XAML payload + WinUI PRI indexes)
$required = @(
  (Join-Path $publishDir "VoiceStudio.App.exe"),
  (Join-Path $publishDir "VoiceStudio.App.dll"),
  (Join-Path $publishDir "VoiceStudio.App.deps.json"),
  (Join-Path $publishDir "VoiceStudio.App.runtimeconfig.json"),
  (Join-Path $publishDir "Microsoft.UI.pri"),
  (Join-Path $publishDir "Microsoft.UI.Xaml.Controls.pri"),
  (Join-Path $publishDir "Microsoft.WindowsAppRuntime.pri"),
  (Join-Path $publishDir "VoiceStudio.App.pri")
)
foreach ($p in $required) {
  if (-not (Test-Path $p)) {
    Write-Error "Publish sanity check failed: missing $p"
  }
}

# Check for XAML payload (XBF or XAML files).
# Note: We always include `VoiceStudio.App.pri` in the unpackaged lane to satisfy ms-appx
# resource resolution, so `.pri` presence is NOT a reliable indicator of packaged vs unpackaged.
$xbfFiles = Get-ChildItem -Path $publishDir -Filter "*.xbf" -Recurse -ErrorAction SilentlyContinue
$xamlFiles = Get-ChildItem -Path $publishDir -Filter "*.xaml" -Recurse -ErrorAction SilentlyContinue

if ($xbfFiles.Count -eq 0 -and $xamlFiles.Count -eq 0) {
  Write-Error "Publish sanity check failed: missing XAML payload (.xbf/.xaml)"
}

$launchLog = Join-Path $publishDir "gatec-launch.log"
Write-Host "Publish sanity check: PASS"

if ($NoLaunch) {
  if ($UiSmoke) {
    Write-Error "Invalid flags: -UiSmoke requires launching the app; do not combine with -NoLaunch."
  }

  Write-Host "Launch skipped (--NoLaunch). exe: $($exe.FullName)"
  @(
    "Gate C publish (no launch)",
    "Timestamp: $(Get-Date -Format o)",
    "Exe: $($exe.FullName)",
    "ExitCode: 0",
    "Binlog: $(Resolve-Path $BinlogPath)"
  ) | Set-Content -Path $launchLog
  @(
    "Timestamp: $(Get-Date -Format o)",
    "Binlog: $(Resolve-Path $BinlogPath)",
    "PublishDir: $publishDir",
    "Exe: $($exe.FullName)",
    "Result: publish_only",
    "ExitCode: 0"
  ) | Set-Content -Path $latestInfoPath
  exit 0
}

if ($UiSmoke) {
  $crashDir = Join-Path $env:LOCALAPPDATA "VoiceStudio\\crashes"
  New-Item -ItemType Directory -Path $crashDir -Force | Out-Null

  # Paths (crash dir + publish dir copies)
  $summary = Join-Path $crashDir "ui_smoke_summary.json"
  $exception = Join-Path $crashDir "ui_smoke_exception.log"
  $steps = Join-Path $crashDir "ui_smoke_steps_latest.log"
  $summaryCopy = Join-Path $publishDir "ui_smoke_summary.json"
  $exceptionCopy = Join-Path $publishDir "ui_smoke_exception.log"

  # Clear stale artifacts from prior runs so a PASS doesn't leave confusing leftovers in the publish folder.
  Remove-Item -LiteralPath $summaryCopy -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $exceptionCopy -Force -ErrorAction SilentlyContinue
  # Also clear stale crash-dir artifacts from prior runs (the app will write fresh ones during this run).
  Remove-Item -LiteralPath $exception -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $summary -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $steps -Force -ErrorAction SilentlyContinue

  $start = Get-Date
  Write-Host "Running Gate C UI smoke: $($exe.FullName) --smoke-ui (WorkingDirectory=$($exe.DirectoryName))"

  $uiResult = "start_failed"
  $uiExitCode = 125
  $uiProc = $null
  $startProcessError = $null

  try {
    $uiProc = Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName -ArgumentList "--smoke-ui" -PassThru
    $uiResult = "running"
    $uiExitCode = 0
  }
  catch {
    $startProcessError = $_ | Out-String
  }

  if ($uiResult -ne "start_failed") {
    # Wait-Process can race if the app exits very quickly; prefer waiting on the process object.
    $exited = $false
    try {
      $exited = $uiProc.WaitForExit($UiSmokeTimeoutSeconds * 1000)
    }
    catch {
      # If we can't wait reliably, fall back to the old behavior (best effort).
      $uiWait = Wait-Process -Id $uiProc.Id -Timeout $UiSmokeTimeoutSeconds -ErrorAction SilentlyContinue
      $exited = [bool]$uiWait
    }

    if ($exited) {
      $uiResult = "exited"
      $uiExitCode = $uiProc.ExitCode
    }
    else {
      $uiResult = "timeout_killed"
      $uiExitCode = 124
      Write-Host "UI smoke still running after $UiSmokeTimeoutSeconds seconds. Terminating..."
      try { Stop-Process -Id $uiProc.Id -Force -ErrorAction SilentlyContinue } catch { }
    }
  }

  # Copy only artifacts created/updated during this run (avoid copying stale files from prior runs).
  if (Test-Path $summary) {
    $s = Get-Item -LiteralPath $summary
    if ($s.LastWriteTime -ge $start) { Copy-Item -LiteralPath $summary -Destination $summaryCopy -Force }
  }
  if (Test-Path $exception) {
    $e = Get-Item -LiteralPath $exception
    if ($e.LastWriteTime -ge $start) { Copy-Item -LiteralPath $exception -Destination $exceptionCopy -Force }
  }

  $artifacts = Get-ChildItem -Path $crashDir | Where-Object { $_.LastWriteTime -ge $start } | Sort-Object LastWriteTime -Descending
  $artifactLines = @()
  foreach ($a in $artifacts) {
    $artifactLines += "$($a.Name)`t$($a.Length)`t$($a.LastWriteTime.ToString('o'))"
  }

  $uiLog = Join-Path $publishDir "gatec-ui-smoke.log"
  @(
    "Gate C UI smoke",
    "Timestamp: $(Get-Date -Format o)",
    "Exe: $($exe.FullName)",
    "WorkingDir: $($exe.DirectoryName)",
    "Result: $uiResult",
    "ExitCode: $uiExitCode",
    "CrashDir: $crashDir",
    "SummaryCopied: $(if (Test-Path $summaryCopy) { $summaryCopy } else { '<missing>' })",
    "ExceptionCopied: $(if (Test-Path $exceptionCopy) { $exceptionCopy } else { '<missing>' })",
    "StartProcessErrorPresent: $(if ($startProcessError) { 'true' } else { 'false' })",
    "",
    $(if ($startProcessError) { "StartProcessErrorDetail:" } else { "" }),
    $(if ($startProcessError) { $startProcessError } else { "" }),
    "",
    "ArtifactsSinceStart (name\tbytes\tlastWriteTime):",
    $artifactLines
  ) | Set-Content -Path $uiLog

  Write-Host "UI smoke exit code: $uiExitCode"
  Write-Host "UI smoke log: $uiLog"

  @(
    "Timestamp: $(Get-Date -Format o)",
    "Binlog: $(Resolve-Path $BinlogPath)",
    "PublishDir: $publishDir",
    "Exe: $($exe.FullName)",
    "Result: ui_smoke",
    "UiSmokeResult: $uiResult",
    "UiSmokeExitCode: $uiExitCode",
    "UiSmokeLog: $uiLog",
    "UiSmokeSummary: $(if (Test-Path $summaryCopy) { $summaryCopy } else { '<missing>' })",
    "ExitCode: $uiExitCode"
  ) | Set-Content -Path $latestInfoPath

  exit $uiExitCode
}

Write-Host "Launching $($exe.FullName)..."
$proc = Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName -PassThru
$wait = Wait-Process -Id $proc.Id -Timeout $SmokeSeconds -ErrorAction SilentlyContinue
if ($wait) {
  $launchResult = "exited"
  $exitCode = $proc.ExitCode
}
else {
  # For a UI app, "still running after N seconds" is a PASS for boot smoke.
  $launchResult = "running_after_timeout"
  $exitCode = 0
  Write-Host "App is still running after $SmokeSeconds seconds (PASS). Terminating to keep the shell clean..."
  try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
}

$logLines = @(
  "Gate C publish+launch",
  "Timestamp: $(Get-Date -Format o)",
  "Exe: $($exe.FullName)",
  "Result: $launchResult",
  "ExitCode: $exitCode",
  "Binlog: $(Resolve-Path $BinlogPath)"
)
$logLines | Set-Content -Path $launchLog

Write-Host "Launch exit code: $exitCode"
Write-Host "Launch log: $launchLog"

@(
  "Timestamp: $(Get-Date -Format o)",
  "Binlog: $(Resolve-Path $BinlogPath)",
  "PublishDir: $publishDir",
  "Exe: $($exe.FullName)",
  "Result: $launchResult",
  "SmokeSeconds: $SmokeSeconds",
  "ExitCode: $exitCode"
) | Set-Content -Path $latestInfoPath

exit $exitCode
