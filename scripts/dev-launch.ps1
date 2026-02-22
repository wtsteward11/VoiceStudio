<#
.SYNOPSIS
    Debug launch script for VoiceStudio.
.DESCRIPTION
    Publishes and launches VoiceStudio in Debug configuration using proven Gate C
    build settings (WindowsAppSDKSelfContained=false, DisableXbfGeneration=false).
    Validates PRI files, XAML payload, and boot smoke before returning.
.PARAMETER NoLaunch
    Publish only; do not launch the app.
.PARAMETER SmokeSeconds
    Seconds to wait for boot smoke. Default: 10.
.EXAMPLE
    .\scripts\dev-launch.ps1
    .\scripts\dev-launch.ps1 -NoLaunch
#>
Param(
  [switch]$NoLaunch,
  [int]$SmokeSeconds = 10
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$gatecScript = Join-Path $scriptDir "gatec-publish-launch.ps1"

if (-not (Test-Path $gatecScript)) {
  Write-Error "gatec-publish-launch.ps1 not found at $gatecScript"
}

& $gatecScript -Configuration Debug -SmokeSeconds $SmokeSeconds -NoLaunch:$NoLaunch
