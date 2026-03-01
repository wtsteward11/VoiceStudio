<#
.SYNOPSIS
    Generates a SHA256 lockfile for critical build configuration files.

.DESCRIPTION
    Creates buildconfig.lock.json containing SHA256 hashes of all protected
    build configuration files. This lockfile can be committed to the repo and
    verified before builds to detect unauthorized changes.

.PARAMETER OutputPath
    Path to write the lockfile. Defaults to buildconfig.lock.json in repo root.

.EXAMPLE
    .\Update-BuildConfigLock.ps1
#>
param(
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (git rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) { $repoRoot = $PSScriptRoot | Split-Path | Split-Path }

if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot "buildconfig.lock.json"
}

$protectedFiles = @(
    "Directory.Build.props",
    "Directory.Build.targets",
    "global.json",
    "src\VoiceStudio.App\VoiceStudio.App.csproj",
    "src\VoiceStudio.Core\VoiceStudio.Core.csproj",
    "VoiceStudio.sln"
)

$entries = @{}

foreach ($relPath in $protectedFiles) {
    $fullPath = Join-Path $repoRoot $relPath
    if (Test-Path $fullPath) {
        $hash = (Get-FileHash $fullPath -Algorithm SHA256).Hash
        $entries[$relPath] = $hash
    } else {
        Write-Host "[WARN] File not found: $relPath" -ForegroundColor Yellow
    }
}

$lockData = [ordered]@{
    generated    = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
    description  = "SHA256 hashes of protected build config files. Verify with Verify-BuildConfigLock.ps1."
    reference    = "docs/reports/BUILD_PROTECTION_PROTOCOL_20260228.md"
    files        = $entries
}

$lockData | ConvertTo-Json -Depth 3 | Set-Content $OutputPath -Encoding UTF8

Write-Host "[DONE] Build config lockfile written to: $OutputPath" -ForegroundColor Green
Write-Host "       Tracked files: $($entries.Count)" -ForegroundColor Cyan
