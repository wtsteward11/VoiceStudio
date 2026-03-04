# VoiceStudio Quantum+ Installer Lifecycle Test Script
# Tests complete lifecycle: install → launch → upgrade → rollback → uninstall
# Designed for clean VM testing

param(
    [string]$InstallerV1Path,
    [string]$InstallerV2Path,
    [string]$InstallPath = "C:\Program Files\VoiceStudio",
    [string]$Version1 = "1.0.0",
    [string]$Version2 = "1.0.1",
    [string]$LogDir = "C:\logs"
)

$ErrorActionPreference = "Stop"

# Resolve LogDir to absolute path so uninstaller/installer logs always land in the right place
if (-not [System.IO.Path]::IsPathRooted($LogDir)) {
    $LogDir = Join-Path (Get-Location) $LogDir
}
$LogDir = (New-Item -ItemType Directory -Path $LogDir -Force).FullName

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Installer Lifecycle Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Ensure log directory exists
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

$lifecycleLog = Join-Path $LogDir "voicestudio_lifecycle_$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
$results = @{
    InstallV1             = $null
    LaunchV1              = $null
    UpgradeV1ToV2         = $null
    LaunchV2              = $null
    RollbackV2ToV1        = $null
    LaunchV1AfterRollback = $null
    UninstallV1           = $null
}

function Write-Log {
    param([string]$Message, [string]$Color = "White")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] $Message"
    Write-Host $logMessage -ForegroundColor $Color
    Add-Content -Path $lifecycleLog -Value $logMessage
}

function Test-Installation {
    param([string]$Version, [string]$InstallPath)
    
    $errors = @()
    
    # Check installation directory
    if (-not (Test-Path $InstallPath)) {
        $errors += "Installation directory not found: $InstallPath"
        return $false, $errors
    }
    
    # Check application executable
    $appExe = Join-Path $InstallPath "App\VoiceStudio.App.exe"
    if (-not (Test-Path $appExe)) {
        $errors += "Application executable not found: $appExe"
        return $false, $errors
    }
    
    # Check backend files
    $backendPath = Join-Path $InstallPath "Backend"
    if (-not (Test-Path $backendPath)) {
        $errors += "Backend directory not found: $backendPath"
        return $false, $errors
    }
    
    return $true, $errors
}

function Invoke-SilentInstall {
    param([string]$InstallerPath, [string]$InstallPath, [string]$Version, [string]$LogSuffix)
    
    $installLog = Join-Path $LogDir "voicestudio_install_${Version}_${LogSuffix}.log"
    Write-Log "Installing version $Version (silent)..." "Yellow"
    Write-Log "Installer: $InstallerPath" "Cyan"
    Write-Log "Install log: $installLog" "Cyan"
    
    try {
        $process = Start-Process -FilePath $InstallerPath -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/DIR=`"$InstallPath`"", "/LOG=`"$installLog`"" -Wait -PassThru -NoNewWindow
        
        if ($process.ExitCode -eq 0) {
            Write-Log "[OK] Installation completed (exit code: $($process.ExitCode))" "Green"
            
            # Verify installation
            Start-Sleep -Seconds 2
            $isValid, $errors = Test-Installation -Version $Version -InstallPath $InstallPath
            if ($isValid) {
                Write-Log "[OK] Installation verification passed" "Green"
                return $true, $installLog
            }
            else {
                Write-Log "[ERROR] Installation verification failed:" "Red"
                foreach ($error in $errors) {
                    Write-Log "  - $error" "Red"
                }
                return $false, $installLog
            }
        }
        else {
            Write-Log "[ERROR] Installation failed with exit code: $($process.ExitCode)" "Red"
            return $false, $installLog
        }
    }
    catch {
        Write-Log "[ERROR] Installation exception: $_" "Red"
        return $false, $installLog
    }
}

function Stop-VoiceStudioProcesses {
    Write-Log "Stopping any running VoiceStudio processes..." "Yellow"
    Get-Process -Name "VoiceStudio.App" -ErrorAction SilentlyContinue | ForEach-Object {
        try { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } catch {}
    }
    Start-Sleep -Seconds 1
}

function Invoke-SilentUninstall {
    param([string]$InstallPath, [string]$Version)
    
    Stop-VoiceStudioProcesses
    
    Write-Log "Uninstalling version $Version (silent)..." "Yellow"
    
    $uninstaller = Get-ChildItem -Path $InstallPath -Filter "unins*.exe" -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
    
    if (-not $uninstaller) {
        Write-Log "[ERROR] Uninstaller not found under $InstallPath" "Red"
        return $false
    }
    
    $uninstallLog = Join-Path $LogDir "voicestudio_uninstall_${Version}.log"
    Write-Log "Uninstaller: $uninstaller" "Cyan"
    Write-Log "Uninstall log: $uninstallLog" "Cyan"
    
    try {
        $process = Start-Process -FilePath $uninstaller -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=`"$uninstallLog`"" -Wait -PassThru -NoNewWindow
        
        Start-Sleep -Seconds 2
        
        if ($process.ExitCode -eq 0) {
            if (-not (Test-Path $InstallPath)) {
                Write-Log "[OK] Uninstallation completed and directory removed" "Green"
                return $true, $uninstallLog
            }
            else {
                Write-Log "[WARN] Uninstallation completed but directory still exists (may be intentional)" "Yellow"
                return $true, $uninstallLog
            }
        }
        else {
            Write-Log "[ERROR] Uninstallation failed with exit code: $($process.ExitCode)" "Red"
            return $false, $uninstallLog
        }
    }
    catch {
        Write-Log "[ERROR] Uninstallation exception: $_" "Red"
        return $false, $uninstallLog
    }
}

function Test-Launch {
    param([string]$AppExePath, [int]$TimeoutSeconds = 30)
    
    Write-Log "Testing application launch..." "Yellow"
    Write-Log "Executable: $AppExePath" "Cyan"
    
    if (-not (Test-Path $AppExePath)) {
        Write-Log "[ERROR] Application executable not found: $AppExePath" "Red"
        return $false
    }
    
    try {
        # Launch with --smoke-exit to test boot stability
        $process = Start-Process -FilePath $AppExePath -ArgumentList "--smoke-exit" -PassThru -NoNewWindow
        
        # Wait for process to exit (with timeout)
        $finished = $process.WaitForExit($TimeoutSeconds * 1000)
        
        if (-not $finished) {
            Write-Log "[WARN] Application did not exit within $TimeoutSeconds seconds, terminating..." "Yellow"
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            return $false
        }
        
        if ($process.ExitCode -eq 0) {
            Write-Log "[OK] Application launched and exited successfully (exit code: 0)" "Green"
            return $true
        }
        else {
            Write-Log "[ERROR] Application exited with code: $($process.ExitCode)" "Red"
            return $false
        }
    }
    catch {
        Write-Log "[ERROR] Launch exception: $_" "Red"
        return $false
    }
}

# Main lifecycle test
Write-Log "Starting installer lifecycle test..." "Cyan"
Write-Log "Version 1: $Version1" "White"
Write-Log "Version 2: $Version2" "White"
Write-Log "Install Path: $InstallPath" "White"
Write-Log "Log Directory: $LogDir" "White"
Write-Log ""

# Step 1: Install V1
Write-Log "=== STEP 1: Install Version $Version1 ===" "Cyan"
if (-not $InstallerV1Path) {
    $InstallerV1Path = Join-Path $PSScriptRoot "Output\VoiceStudio-Setup-v$Version1.exe"
}
if (-not (Test-Path $InstallerV1Path)) {
    Write-Log "[ERROR] Installer V1 not found: $InstallerV1Path" "Red"
    exit 1
}

$success, $log = Invoke-SilentInstall -InstallerPath $InstallerV1Path -InstallPath $InstallPath -Version $Version1 -LogSuffix "initial"
$results.InstallV1 = @{ Success = $success; Log = $log }
if (-not $success) {
    Write-Log "[FAIL] Step 1 failed: Install V1" "Red"
    exit 1
}

# Step 2: Launch V1
Write-Log ""
Write-Log "=== STEP 2: Launch Version $Version1 ===" "Cyan"
$appExe = Join-Path $InstallPath "App\VoiceStudio.App.exe"
$success = Test-Launch -AppExePath $appExe
$results.LaunchV1 = @{ Success = $success }
if (-not $success) {
    Write-Log "[FAIL] Step 2 failed: Launch V1" "Red"
    exit 1
}

# Step 3: Upgrade to V2
Write-Log ""
Write-Log "=== STEP 3: Upgrade to Version $Version2 ===" "Cyan"
if (-not $InstallerV2Path) {
    $InstallerV2Path = Join-Path $PSScriptRoot "Output\VoiceStudio-Setup-v$Version2.exe"
}
if (-not (Test-Path $InstallerV2Path)) {
    Write-Log "[ERROR] Installer V2 not found: $InstallerV2Path" "Red"
    Write-Log "Gate H requires upgrade/rollback proof. Build v2 (or pass -InstallerV2Path) and re-run." "Red"
    exit 1
}
else {
    $success, $log = Invoke-SilentInstall -InstallerPath $InstallerV2Path -InstallPath $InstallPath -Version $Version2 -LogSuffix "upgrade"
    $results.UpgradeV1ToV2 = @{ Success = $success; Log = $log }
    if (-not $success) {
        Write-Log "[FAIL] Step 3 failed: Upgrade V1 to V2" "Red"
        exit 1
    }
    
    # Step 4: Launch V2
    Write-Log ""
    Write-Log "=== STEP 4: Launch Version $Version2 ===" "Cyan"
    $success = Test-Launch -AppExePath $appExe
    $results.LaunchV2 = @{ Success = $success }
    if (-not $success) {
        Write-Log "[FAIL] Step 4 failed: Launch V2" "Red"
        exit 1
    }
    
    # Step 5: Rollback to V1
    Write-Log ""
    Write-Log "=== STEP 5: Rollback to Version $Version1 ===" "Cyan"
    $success, $uninstallLog = Invoke-SilentUninstall -InstallPath $InstallPath -Version $Version2
    if (-not $success) {
        Write-Log "[FAIL] Step 5a failed: Uninstall V2 for rollback" "Red"
        exit 1
    }
    
    $success, $log = Invoke-SilentInstall -InstallerPath $InstallerV1Path -InstallPath $InstallPath -Version $Version1 -LogSuffix "rollback"
    $results.RollbackV2ToV1 = @{ Success = $success; Log = $log }
    if (-not $success) {
        Write-Log "[FAIL] Step 5b failed: Install V1 for rollback" "Red"
        exit 1
    }
    
    # Step 6: Launch V1 after rollback
    Write-Log ""
    Write-Log "=== STEP 6: Launch Version $Version1 (after rollback) ===" "Cyan"
    $success = Test-Launch -AppExePath $appExe
    $results.LaunchV1AfterRollback = @{ Success = $success }
    if (-not $success) {
        Write-Log "[FAIL] Step 6 failed: Launch V1 after rollback" "Red"
        exit 1
    }
}

# Step 7: Uninstall V1
Write-Log ""
Write-Log "=== STEP 7: Uninstall Version $Version1 ===" "Cyan"
$success, $uninstallLog = Invoke-SilentUninstall -InstallPath $InstallPath -Version $Version1
$results.UninstallV1 = @{ Success = $success; Log = $uninstallLog }
if (-not $success) {
    Write-Log "[FAIL] Step 7 failed: Uninstall V1" "Red"
    exit 1
}

# Summary
Write-Log ""
Write-Log "========================================" "Cyan"
Write-Log "Lifecycle Test Summary" "Cyan"
Write-Log "========================================" "Cyan"
Write-Log ""

$allPassed = $true
foreach ($key in $results.Keys) {
    $result = $results[$key]
    if ($result.Skipped) {
        Write-Log "$key : SKIPPED" "Yellow"
    }
    elseif ($result.Success -eq $true) {
        Write-Log "$key : PASS" "Green"
    }
    elseif ($result.Success -eq $false) {
        Write-Log "$key : FAIL" "Red"
        $allPassed = $false
    }
    else {
        Write-Log "$key : UNKNOWN" "Yellow"
    }
}

Write-Log ""
Write-Log "Lifecycle log: $lifecycleLog" "Cyan"
Write-Log ""

if ($allPassed) {
    Write-Log "[OK] All lifecycle tests passed!" "Green"
    exit 0
}
else {
    Write-Log "[ERROR] Some lifecycle tests failed. Review logs above." "Red"
    exit 1
}
