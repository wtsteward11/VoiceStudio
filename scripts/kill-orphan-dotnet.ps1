<#
.SYNOPSIS
    Kill orphaned dotnet.exe processes that hold file locks.
.DESCRIPTION
    Terminates lingering dotnet.exe and XamlCompiler.exe processes
    that can cause BUILD-003 (NuGet DLL file lock race conditions).
    Run before builds in CI to prevent stale process interference.
.EXAMPLE
    .\scripts\kill-orphan-dotnet.ps1
#>
[CmdletBinding()]
param(
    [switch]$DryRun
)

$processNames = @("dotnet", "XamlCompiler", "MSBuild", "VBCSCompiler")
$killed = 0

foreach ($name in $processNames) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -ne $PID }

    foreach ($proc in $procs) {
        $age = (Get-Date) - $proc.StartTime
        if ($age.TotalMinutes -gt 5) {
            if ($DryRun) {
                Write-Host "[kill-orphan] Would kill: $($proc.ProcessName) (PID $($proc.Id), age $([int]$age.TotalMinutes)m)"
            } else {
                Write-Host "[kill-orphan] Killing: $($proc.ProcessName) (PID $($proc.Id), age $([int]$age.TotalMinutes)m)"
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                $killed++
            }
        }
    }
}

if ($killed -eq 0) {
    Write-Host "[kill-orphan] No orphaned processes found."
} else {
    Write-Host "[kill-orphan] Killed $killed orphaned process(es)."
    Start-Sleep -Seconds 2
}

exit 0
