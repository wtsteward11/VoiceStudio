# VoiceStudio Runtime Bundle Preparation Script
# Prepares embedded Python and FFmpeg for the installer
#
# Usage:
#   .\prepare-runtime.ps1                   # Prepare both Python and FFmpeg
#   .\prepare-runtime.ps1 -SkipPython       # Only prepare FFmpeg
#   .\prepare-runtime.ps1 -SkipFFmpeg       # Only prepare Python
#   .\prepare-runtime.ps1 -IncludeModels    # Also download starter model pack

param(
    [switch]$SkipPython,
    [switch]$SkipFFmpeg,
    [switch]$IncludeModels,
    [string]$PythonVersion = "3.11.9",
    [string]$FFmpegVersion = "7.0",
    [string]$OutputRoot = $env:VOICESTUDIO_BUILD_SANDBOX,
    [switch]$ForceInRepo
)

$ErrorActionPreference = "Stop"
$InstallerDir = $PSScriptRoot
$RepoRoot = Split-Path -Parent $InstallerDir

if (-not $OutputRoot -or $OutputRoot.Trim() -eq "") {
    $OutputRoot = Join-Path $env:LOCALAPPDATA "VoiceStudioBuild"
}

$repoFull = [System.IO.Path]::GetFullPath($RepoRoot)
$outFull = [System.IO.Path]::GetFullPath($OutputRoot)

if ($outFull.StartsWith($repoFull, [System.StringComparison]::OrdinalIgnoreCase) -and (-not $ForceInRepo)) {
    throw "Refusing to write runtime bundle inside repo: $outFull. Use -OutputRoot outside repo, or pass -ForceInRepo (NOT RECOMMENDED)."
}

$RuntimeDir = Join-Path $OutputRoot "installer_runtime"
$RootDir = $RepoRoot
New-Item -ItemType Directory -Path $RuntimeDir -Force | Out-Null

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "VoiceStudio Runtime Bundle Preparation"  -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# ── Embedded Python ──────────────────────────────────────────────────────
if (-not $SkipPython) {
    $PythonDir = Join-Path $RuntimeDir "python"
    $PythonZip = Join-Path $RuntimeDir "python-embed.zip"
    $PythonUrl = "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip"
    $GetPipUrl = "https://bootstrap.pypa.io/get-pip.py"

    Write-Host ""
    Write-Host "Preparing embedded Python $PythonVersion..." -ForegroundColor Yellow

    if (Test-Path $PythonDir) {
        Write-Host "  Removing previous Python bundle..." -ForegroundColor Gray
        Remove-Item -Recurse -Force $PythonDir
    }
    New-Item -ItemType Directory -Path $PythonDir -Force | Out-Null

    # Download embeddable Python
    if (-not (Test-Path $PythonZip)) {
        Write-Host "  Downloading Python embeddable package..." -ForegroundColor Gray
        Invoke-WebRequest -Uri $PythonUrl -OutFile $PythonZip -UseBasicParsing
    }
    Write-Host "  Extracting..." -ForegroundColor Gray
    Expand-Archive -Path $PythonZip -DestinationPath $PythonDir -Force

    # Enable pip: uncomment 'import site' in pythonXY._pth
    $pthFile = Get-ChildItem -Path $PythonDir -Filter "python*._pth" | Select-Object -First 1
    if ($pthFile) {
        $content = Get-Content $pthFile.FullName -Raw
        $content = $content -replace '#import site', 'import site'
        Set-Content -Path $pthFile.FullName -Value $content
        Write-Host "  Enabled site-packages in $($pthFile.Name)" -ForegroundColor Gray
    }

    # Bootstrap pip
    $getPipPath = Join-Path $RuntimeDir "get-pip.py"
    if (-not (Test-Path $getPipPath)) {
        Write-Host "  Downloading get-pip.py..." -ForegroundColor Gray
        Invoke-WebRequest -Uri $GetPipUrl -OutFile $getPipPath -UseBasicParsing
    }
    $pythonExe = Join-Path $PythonDir "python.exe"
    Write-Host "  Installing pip..." -ForegroundColor Gray
    & $pythonExe $getPipPath --quiet 2>&1 | Out-Null

    # Install core backend dependencies
    $reqFile = Join-Path $RootDir "requirements.txt"
    if (Test-Path $reqFile) {
        Write-Host "  Installing core backend dependencies..." -ForegroundColor Gray
        & $pythonExe -m pip install -r $reqFile --quiet 2>&1 | Out-Null
        Write-Host "  Core dependencies installed." -ForegroundColor Green
    }

    # Cleanup
    Remove-Item -Path $getPipPath -ErrorAction SilentlyContinue
    Remove-Item -Path $PythonZip -ErrorAction SilentlyContinue

    $size = [math]::Round((Get-ChildItem -Recurse $PythonDir | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
    Write-Host "  Python bundle ready: $size MB" -ForegroundColor Green
}

# ── FFmpeg ───────────────────────────────────────────────────────────────
if (-not $SkipFFmpeg) {
    $FFmpegDir = Join-Path $RuntimeDir "ffmpeg"
    $FFmpegZip = Join-Path $RuntimeDir "ffmpeg.zip"
    $FFmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n$FFmpegVersion-latest-win64-gpl.zip"

    Write-Host ""
    Write-Host "Preparing FFmpeg $FFmpegVersion..." -ForegroundColor Yellow

    if (Test-Path $FFmpegDir) {
        Remove-Item -Recurse -Force $FFmpegDir
    }
    New-Item -ItemType Directory -Path $FFmpegDir -Force | Out-Null

    if (-not (Test-Path $FFmpegZip)) {
        Write-Host "  Downloading FFmpeg..." -ForegroundColor Gray
        try {
            Invoke-WebRequest -Uri $FFmpegUrl -OutFile $FFmpegZip -UseBasicParsing
        }
        catch {
            Write-Host "  Download failed: $_" -ForegroundColor Yellow
            Write-Host "  Trying alternative URL..." -ForegroundColor Yellow
            $FFmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
            Invoke-WebRequest -Uri $FFmpegUrl -OutFile $FFmpegZip -UseBasicParsing
        }
    }

    Write-Host "  Extracting..." -ForegroundColor Gray
    $tempExtract = Join-Path $RuntimeDir "ffmpeg-temp"
    Expand-Archive -Path $FFmpegZip -DestinationPath $tempExtract -Force

    # Find the bin directory inside the extracted folder
    $binDir = Get-ChildItem -Path $tempExtract -Filter "bin" -Recurse -Directory | Select-Object -First 1
    if ($binDir) {
        Copy-Item -Path "$($binDir.FullName)\*" -Destination $FFmpegDir -Force
    }
    else {
        # Flat structure — copy exe files directly
        Get-ChildItem -Path $tempExtract -Filter "*.exe" -Recurse | ForEach-Object {
            Copy-Item $_.FullName -Destination $FFmpegDir -Force
        }
    }

    Remove-Item -Recurse -Force $tempExtract -ErrorAction SilentlyContinue
    Remove-Item -Path $FFmpegZip -ErrorAction SilentlyContinue

    $ffmpegExe = Join-Path $FFmpegDir "ffmpeg.exe"
    if (Test-Path $ffmpegExe) {
        $size = [math]::Round((Get-Item $ffmpegExe).Length / 1MB, 1)
        Write-Host "  FFmpeg bundle ready: $size MB" -ForegroundColor Green
    }
    else {
        Write-Host "  Warning: ffmpeg.exe not found after extraction" -ForegroundColor Yellow
    }
}

# ── Model Packs (optional) ──────────────────────────────────────────────
if ($IncludeModels) {
    Write-Host ""
    Write-Host "Model pack preparation is manual." -ForegroundColor Yellow
    Write-Host "Place model files in:" -ForegroundColor Gray
    Write-Host "  $RuntimeDir/models/piper/     - Piper voice models (.onnx + .json)" -ForegroundColor Gray
    Write-Host "  $RuntimeDir/models/whisper/    - Whisper models (tiny.bin, base.bin)" -ForegroundColor Gray
    Write-Host "  $RuntimeDir/models/espeak/     - eSpeak-NG data" -ForegroundColor Gray
}

# ── Summary ──────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Runtime preparation complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next: Run build-installer.ps1 to create the installer." -ForegroundColor White
Write-Host "Runtime bundles are in: $RuntimeDir" -ForegroundColor White
Write-Host "Pass -RuntimePath to build-installer.ps1 to include them, or copy to installer staging as needed." -ForegroundColor White
