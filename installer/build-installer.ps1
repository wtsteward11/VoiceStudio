# VoiceStudio Quantum+ Installer Build Script
# Builds the application and creates installer package

param(
    [string]$InstallerType = "InnoSetup",
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0",
    # Optional override for locating the Inno Setup compiler (ISCC.exe) when not installed in the default path.
    [string]$InnoSetupPath = "",
    # If set, always re-run the Gate C publish step even when a valid publish output already exists.
    [switch]$ForcePublish,
    # Optional path to runtime bundle (from prepare-runtime.ps1). Defaults to sandbox or installer/runtime.
    [string]$RuntimePath = ""
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "VoiceStudio Quantum+ Installer Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Set paths
$RootDir = Split-Path -Parent $PSScriptRoot
$InstallerDir = Join-Path $RootDir "installer"
$OutputDir = Join-Path $InstallerDir "Output"
$GateCProofScript = Join-Path $RootDir "scripts\gatec-publish-launch.ps1"
$GateCPublishDir = Join-Path $RootDir ".buildlogs\x64\$Configuration\gatec-publish"
$BackendPath = Join-Path $RootDir "backend"

# Ensure output directory exists (do NOT delete; we want multiple versioned installers side-by-side)
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# Remove only the specific versioned output file (avoid stale overwrite while preserving other versions)
$expectedInstallerOutput = $null
if ($InstallerType -eq "InnoSetup") {
    $expectedInstallerOutput = Join-Path $OutputDir "VoiceStudio-Setup-v$Version.exe"
}
elseif ($InstallerType -eq "WiX") {
    $expectedInstallerOutput = Join-Path $OutputDir "VoiceStudio-Setup-v$Version.msi"
}

if ($expectedInstallerOutput -and (Test-Path $expectedInstallerOutput)) {
    Remove-Item -Path $expectedInstallerOutput -Force
}

# Resolve installer toolchain early (fail fast before publishing)
$IsccPath = $null
if ($InstallerType -eq "InnoSetup") {
    $resolvedIscc = $null

    if ($InnoSetupPath -and (Test-Path $InnoSetupPath)) {
        $resolvedIscc = $InnoSetupPath
    }
    else {
        $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
        if ($cmd -and $cmd.Path) {
            $resolvedIscc = $cmd.Path
        }
        else {
            $candidates = @(
                (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
                "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
                "C:\Program Files\Inno Setup 6\ISCC.exe",
                (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 5\ISCC.exe"),
                "C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
                "C:\Program Files\Inno Setup 5\ISCC.exe"
            )
            foreach ($c in $candidates) {
                if (Test-Path $c) { $resolvedIscc = $c; break }
            }
        }
    }

    if (-not $resolvedIscc) {
        Write-Host "Error: Inno Setup compiler (ISCC.exe) not found." -ForegroundColor Red
        Write-Host "Install Inno Setup 6.2+ or add ISCC.exe to PATH, or pass -InnoSetupPath <full path to ISCC.exe>." -ForegroundColor Yellow
        Write-Host "Download: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
        exit 1
    }
    $IsccPath = $resolvedIscc
}
elseif ($InstallerType -eq "WiX") {
    $WixCandle = Get-Command "candle.exe" -ErrorAction SilentlyContinue
    $WixLight = Get-Command "light.exe" -ErrorAction SilentlyContinue
    if (-not $WixCandle -or -not $WixLight) {
        Write-Host "Error: WiX Toolset not found in PATH (requires candle.exe + light.exe)." -ForegroundColor Red
        Write-Host "Install WiX Toolset v3.11+ and ensure it is on PATH." -ForegroundColor Yellow
        Write-Host "Download: https://wixtoolset.org/releases/" -ForegroundColor Yellow
        exit 1
    }
}

# Publish frontend (canonical Gate C artifact)
try {
    if (-not (Test-Path $GateCProofScript)) {
        throw "Gate C script not found: $GateCProofScript"
    }

    $publishIsReusable = $false
    if (Test-Path $GateCPublishDir) {
        $requiredPublishFiles = @(
            (Join-Path $GateCPublishDir "VoiceStudio.App.exe"),
            (Join-Path $GateCPublishDir "VoiceStudio.App.dll"),
            (Join-Path $GateCPublishDir "VoiceStudio.App.deps.json"),
            (Join-Path $GateCPublishDir "VoiceStudio.App.runtimeconfig.json")
        )
        $missing = @()
        foreach ($p in $requiredPublishFiles) {
            if (-not (Test-Path $p)) { $missing += $p }
        }

        # WinUI resource indexes (.pri) are required for ms-appx resource resolution in the unpackaged lane.
        $requiredPriFiles = @(
            (Join-Path $GateCPublishDir "VoiceStudio.App.pri"),
            (Join-Path $GateCPublishDir "Microsoft.UI.pri"),
            (Join-Path $GateCPublishDir "Microsoft.UI.Xaml.Controls.pri"),
            (Join-Path $GateCPublishDir "Microsoft.WindowsAppRuntime.pri")
        )
        foreach ($p in $requiredPriFiles) {
            if (-not (Test-Path $p)) { $missing += $p }
        }

        # For unpackaged apps we require compiled XAML payload (XBF) or raw XAML.
        $xbfFiles = Get-ChildItem -Path $GateCPublishDir -Filter "*.xbf" -Recurse -ErrorAction SilentlyContinue
        $xamlFiles = Get-ChildItem -Path $GateCPublishDir -Filter "*.xaml" -Recurse -ErrorAction SilentlyContinue
        if ($xbfFiles.Count -eq 0 -and $xamlFiles.Count -eq 0) {
            $missing += "<XAML payload missing: expected at least one .xbf or .xaml in $GateCPublishDir>"
        }

        if ($missing.Count -eq 0) {
            $publishIsReusable = $true

            $gatecLaunchLog = Join-Path $GateCPublishDir "gatec-launch.log"
            if (Test-Path $gatecLaunchLog) {
                $gatecLaunchLogText = Get-Content -LiteralPath $gatecLaunchLog -Raw -ErrorAction SilentlyContinue
                if ($gatecLaunchLogText -notmatch '(?m)^ExitCode:\\s*0\\s*$') {
                    Write-Host "Warning: Gate C launch log exists but does not show ExitCode: 0. Publish output appears complete; reusing. Use -ForcePublish to regenerate." -ForegroundColor Yellow
                }
            }
            else {
                Write-Host "Warning: Gate C launch log not found. Publish output appears complete; reusing. Use -ForcePublish to regenerate." -ForegroundColor Yellow
            }
        }
        else {
            Write-Host "Gate C publish output is present but incomplete; will re-publish. Missing:" -ForegroundColor Yellow
            foreach ($m in $missing) { Write-Host "  - $m" -ForegroundColor Yellow }
        }
    }

    if ($publishIsReusable -and -not $ForcePublish) {
        Write-Host "Reusing existing Gate C publish output (skipping publish):" -ForegroundColor Yellow
        Write-Host "PublishDir: $GateCPublishDir" -ForegroundColor Cyan
    }
    else {
        Write-Host "Publishing frontend (Gate C artifact)..." -ForegroundColor Yellow

        # Ensure we don't accidentally package stale publish output.
        if (Test-Path $GateCPublishDir) {
            Remove-Item -Path $GateCPublishDir -Recurse -Force
        }

        & $GateCProofScript -Configuration $Configuration -RuntimeIdentifier win-x64 -NoLaunch
        if ($LASTEXITCODE -ne 0) {
            throw "Gate C publish failed (ExitCode=$LASTEXITCODE)"
        }

        $appExe = Join-Path $GateCPublishDir "VoiceStudio.App.exe"
        if (-not (Test-Path $appExe)) {
            throw "Gate C publish output missing: $appExe"
        }

        Write-Host "Gate C publish successful!" -ForegroundColor Green
        Write-Host "PublishDir: $GateCPublishDir" -ForegroundColor Cyan
    }
}
catch {
    Write-Host "Frontend publish failed: $_" -ForegroundColor Red
    exit 1
}

# Verify backend files exist
Write-Host "Verifying backend files..." -ForegroundColor Yellow
$BackendMain = Join-Path $BackendPath "api\main.py"
if (-not (Test-Path $BackendMain)) {
    Write-Host "Warning: Backend main.py not found at $BackendMain" -ForegroundColor Yellow
}
else {
    Write-Host "Backend files verified!" -ForegroundColor Green
}

# Verify engine manifests
Write-Host "Verifying engine manifests..." -ForegroundColor Yellow
$EngineManifests = Get-ChildItem -Path (Join-Path $RootDir "engines") -Filter "engine.manifest.json" -Recurse
if ($EngineManifests.Count -eq 0) {
    Write-Host "Warning: No engine manifests found" -ForegroundColor Yellow
}
else {
    Write-Host "Found $($EngineManifests.Count) engine manifest(s)!" -ForegroundColor Green
}

# Check for bundled runtime (optional - prepared by prepare-runtime.ps1)
# prepare-runtime.ps1 outputs to $OutputRoot/installer_runtime where OutputRoot defaults to
# $env:VOICESTUDIO_BUILD_SANDBOX or %LOCALAPPDATA%\VoiceStudioBuild. build-installer prefers
# that sandbox path, then falls back to legacy installer/runtime.
if (-not $RuntimePath -or $RuntimePath.Trim() -eq "") {
    $sandbox = if ($env:VOICESTUDIO_BUILD_SANDBOX) { $env:VOICESTUDIO_BUILD_SANDBOX } else { Join-Path $env:LOCALAPPDATA "VoiceStudioBuild" }
    $RuntimePath = Join-Path $sandbox "installer_runtime"
}
$RuntimePython = Join-Path $RuntimePath "python\python.exe"
$RuntimeFFmpeg = Join-Path $RuntimePath "ffmpeg\ffmpeg.exe"
# Fallback to legacy installer/runtime if sandbox not present
if (-not (Test-Path $RuntimePython)) {
    $RuntimePython = Join-Path $InstallerDir "runtime\python\python.exe"
}
if (-not (Test-Path $RuntimeFFmpeg)) {
    $RuntimeFFmpeg = Join-Path $InstallerDir "runtime\ffmpeg\ffmpeg.exe"
}
if (Test-Path $RuntimePython) {
    Write-Host "Bundled Python runtime found - will be included in installer." -ForegroundColor Green
}
else {
    Write-Host "No bundled Python runtime. Run prepare-runtime.ps1 to bundle Python. Use -RuntimePath to point to sandbox output." -ForegroundColor Yellow
}
if (Test-Path $RuntimeFFmpeg) {
    Write-Host "Bundled FFmpeg found - will be included in installer." -ForegroundColor Green
}
else {
    Write-Host "No bundled FFmpeg. Run prepare-runtime.ps1 to bundle FFmpeg. Use -RuntimePath to point to sandbox output." -ForegroundColor Yellow
}

# Build Installer
Write-Host ""
Write-Host "Building installer ($InstallerType)..." -ForegroundColor Yellow

if ($InstallerType -eq "InnoSetup") {
    # Build Inno Setup installer
    $InnoScript = Join-Path $InstallerDir "VoiceStudio.iss"
    if (-not (Test-Path $InnoScript)) {
        Write-Host "Error: Inno Setup script not found at $InnoScript" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Compiling Inno Setup installer..." -ForegroundColor Yellow
    # Ensure the installer always packages the publish output we just produced (matches -Configuration).
    # Note: avoid embedding quotes in the /D value; on some shells this can be passed through as `\E:\...` and
    # Inno Setup will reject it as an "Unknown filename prefix".
    # Keep this as a repo-relative path (relative to the .iss file location) for maximum determinism.
    $innoSourceDir = "..\.buildlogs\x64\$Configuration\gatec-publish"
    Write-Host "ISCC: $IsccPath /DMyAppVersion=$Version /DMyAppSourceDir=$innoSourceDir $InnoScript" -ForegroundColor Cyan
    & $IsccPath "/DMyAppVersion=$Version" "/DMyAppSourceDir=$innoSourceDir" $InnoScript
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Inno Setup compilation failed!" -ForegroundColor Red
        exit 1
    }

    if ($expectedInstallerOutput -and (-not (Test-Path $expectedInstallerOutput))) {
        Write-Host "Error: Expected installer output not found: $expectedInstallerOutput" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Inno Setup installer created successfully!" -ForegroundColor Green
    Write-Host "Output: $OutputDir" -ForegroundColor Cyan
}
elseif ($InstallerType -eq "WiX") {
    # Build WiX installer
    $WixCandle = "candle.exe"
    $WixLight = "light.exe"
    
    # Check if WiX is in PATH
    $WixPath = Get-Command $WixCandle -ErrorAction SilentlyContinue
    if (-not $WixPath) {
        Write-Host "Error: WiX Toolset not found in PATH" -ForegroundColor Red
        Write-Host "Please install WiX Toolset v3.11+ from https://wixtoolset.org/releases/" -ForegroundColor Yellow
        exit 1
    }
    
    $WixScript = Join-Path $InstallerDir "VoiceStudio.wxs"
    if (-not (Test-Path $WixScript)) {
        Write-Host "Error: WiX script not found at $WixScript" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Compiling WiX installer..." -ForegroundColor Yellow
    Push-Location $InstallerDir
    try {
        & $WixCandle "-dProductVersion=$Version" VoiceStudio.wxs
        if ($LASTEXITCODE -ne 0) {
            throw "WiX candle failed"
        }
        
        & $WixLight VoiceStudio.wixobj -ext WixUIExtension -out "Output\VoiceStudio-Setup-v$Version.msi"
        if ($LASTEXITCODE -ne 0) {
            throw "WiX light failed"
        }
        
        Write-Host "WiX installer created successfully!" -ForegroundColor Green
        Write-Host "Output: $OutputDir" -ForegroundColor Cyan
    }
    catch {
        Write-Host "WiX compilation failed: $_" -ForegroundColor Red
        exit 1
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Host "Error: Unknown installer type: $InstallerType" -ForegroundColor Red
    Write-Host "Supported types: InnoSetup, WiX" -ForegroundColor Yellow
    exit 1
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Installer Type: $InstallerType" -ForegroundColor White
Write-Host "Configuration: $Configuration" -ForegroundColor White
Write-Host "Version: $Version" -ForegroundColor White
Write-Host "Output Directory: $OutputDir" -ForegroundColor White
Write-Host ""

# List output files
if (Test-Path $OutputDir) {
    Write-Host "Output Files:" -ForegroundColor Cyan
    Get-ChildItem -Path $OutputDir | ForEach-Object {
        Write-Host "  - $($_.Name) ($([math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor White
    }
}

Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "1. Test installer on clean Windows 10 VM" -ForegroundColor White
Write-Host "2. Test installer on clean Windows 11 VM" -ForegroundColor White
Write-Host "3. Test upgrade from previous version" -ForegroundColor White
Write-Host "4. Test uninstallation" -ForegroundColor White
Write-Host "5. Code sign installer (if applicable)" -ForegroundColor White
Write-Host ""

Write-Host ""

