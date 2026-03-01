<#
.SYNOPSIS
    Clean build script for VoiceStudio with logging.
.DESCRIPTION
    Performs a clean build of VoiceStudio.sln with output logged to .buildlogs/build/.
    Kills any running VoiceStudio instances before building.
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Debug
.PARAMETER Clean
    If specified, removes bin/obj directories before building.
.EXAMPLE
    .\scripts\build.ps1
    .\scripts\build.ps1 -Configuration Release -Clean
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$BuildLogsDir = Join-Path $RootDir ".buildlogs\build"
$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$LogFile = Join-Path $BuildLogsDir "build_$Timestamp.log"

# Create build logs directory
if (-not (Test-Path $BuildLogsDir)) {
    New-Item -ItemType Directory -Path $BuildLogsDir -Force | Out-Null
}

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $LogEntry = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [$Level] $Message"
    Write-Host $LogEntry
    Add-Content -Path $LogFile -Value $LogEntry
}

Write-Log "=== VoiceStudio Build Script ===" "INFO"
Write-Log "Configuration: $Configuration" "INFO"
Write-Log "Log file: $LogFile" "INFO"

# Kill any running VoiceStudio instances
Write-Log "Checking for running VoiceStudio processes..." "INFO"
$processes = Get-Process -Name "VoiceStudio*" -ErrorAction SilentlyContinue
if ($processes) {
    Write-Log "Killing $($processes.Count) VoiceStudio process(es)..." "WARN"
    $processes | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# Clean bin/obj if requested
if ($Clean) {
    Write-Log "Cleaning bin and obj directories..." "INFO"
    $dirsToClean = @(
        (Join-Path $RootDir "src\VoiceStudio.App\bin"),
        (Join-Path $RootDir "src\VoiceStudio.App\obj"),
        (Join-Path $RootDir "src\VoiceStudio.Core\bin"),
        (Join-Path $RootDir "src\VoiceStudio.Core\obj"),
        (Join-Path $RootDir "src\VoiceStudio.App.Tests\bin"),
        (Join-Path $RootDir "src\VoiceStudio.App.Tests\obj")
    )
    foreach ($dir in $dirsToClean) {
        if (Test-Path $dir) {
            Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Log "Removed: $dir" "INFO"
        }
    }
}

# Restore packages
Write-Log "Restoring NuGet packages..." "INFO"
$restoreOutput = & dotnet restore "$RootDir\VoiceStudio.sln" 2>&1
$restoreOutput | Out-File -FilePath $LogFile -Append
if ($LASTEXITCODE -ne 0) {
    Write-Log "Package restore failed with exit code $LASTEXITCODE" "ERROR"
    exit $LASTEXITCODE
}
Write-Log "Package restore completed." "INFO"

# Build solution
Write-Log "Building VoiceStudio.sln ($Configuration, x64)..." "INFO"
$buildArgs = @(
    "build",
    "$RootDir\VoiceStudio.sln",
    "-c", $Configuration,
    "-p:Platform=x64",
    "--no-restore",
    "-bl:$BuildLogsDir\build_$Timestamp.binlog"
)

$buildOutput = & dotnet $buildArgs 2>&1
$buildOutput | Out-File -FilePath $LogFile -Append
$BuildExitCode = $LASTEXITCODE

# Parse build output for summary
$errors = ($buildOutput | Select-String -Pattern "\d+ Error\(s\)" | Select-Object -First 1)
$warnings = ($buildOutput | Select-String -Pattern "\d+ Warning\(s\)" | Select-Object -First 1)

if ($BuildExitCode -eq 0) {
    Write-Log "Build SUCCEEDED" "INFO"
    Write-Log "Summary: $errors, $warnings" "INFO"
} else {
    Write-Log "Build FAILED with exit code $BuildExitCode" "ERROR"
    Write-Log "Summary: $errors, $warnings" "ERROR"
    Write-Log "See log file for details: $LogFile" "ERROR"
}

Write-Log "Build log: $LogFile" "INFO"
Write-Log "Binlog: $BuildLogsDir\build_$Timestamp.binlog" "INFO"
Write-Log "=== Build Complete ===" "INFO"

exit $BuildExitCode
