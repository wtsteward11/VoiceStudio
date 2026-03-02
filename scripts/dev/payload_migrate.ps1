<#
.SYNOPSIS
    Migrate large payload files from repo to external payload root (M8 Repo Payload Detox).

.DESCRIPTION
    Reads .ci/repo_payload_policy.json large_file_exceptions and migrates tracked files
    to VOICESTUDIO_PAYLOADS_ROOT. Replaces each with a .payload_pointer.json file.

.PARAMETER DryRun
    Default. Print what would move without executing.

.PARAMETER Execute
    Perform the migration (move files, create pointers, git rm/add).

.EXAMPLE
    .\payload_migrate.ps1
    .\payload_migrate.ps1 --dry-run
    .\payload_migrate.ps1 --execute
#>

param(
    [switch]$DryRun,
    [switch]$Execute
)
if ($Execute) { $DryRun = $false } elseif (-not $PSBoundParameters.ContainsKey("DryRun")) { $DryRun = $true }

$ErrorActionPreference = "Stop"
$RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$PolicyPath = Join-Path $RepoRoot ".ci\repo_payload_policy.json"
$PayloadRoot = if ($env:VOICESTUDIO_PAYLOADS_ROOT) { $env:VOICESTUDIO_PAYLOADS_ROOT } else { Join-Path $env:LOCALAPPDATA "VoiceStudioPayloads" }

if (-not (Test-Path $PolicyPath)) {
    Write-Error "Policy not found: $PolicyPath"
}

$policy = Get-Content $PolicyPath -Raw | ConvertFrom-Json
$exceptions = @($policy.large_file_exceptions)
if ($exceptions.Count -eq 0) {
    Write-Host "large_file_exceptions is empty. Nothing to migrate."
    exit 0
}

# Get tracked files from git
$tracked = & git -C $RepoRoot ls-files 2>$null
$trackedSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($t in $tracked) { [void]$trackedSet.Add($t.Replace("/", "\")) }

$toMigrate = @()
foreach ($e in $exceptions) {
    $pathStr = $e.path -replace "/", "\"
    $fullPath = Join-Path $RepoRoot $pathStr
    if (-not (Test-Path $fullPath -PathType Leaf)) { continue }
    if (-not $trackedSet.Contains($pathStr)) { continue }
    $toMigrate += @{
        Path       = $pathStr
        FullPath   = $fullPath
        SizeBytes  = (Get-Item $fullPath).Length
    }
}

if ($toMigrate.Count -eq 0) {
    Write-Host "No tracked large files found to migrate."
    exit 0
}

$manifest = @{
    payload_root   = $PayloadRoot
    repo_root      = $RepoRoot
    moved_at       = (Get-Date -Format "o")
    dry_run        = -not $Execute
    moved_files    = @()
}

function Get-FileSha256 {
    param([string]$Path)
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $hash = [System.Security.Cryptography.SHA256]::Create()
        $bytes = $hash.ComputeHash($stream)
        return [BitConverter]::ToString($bytes).Replace("-", "").ToLowerInvariant()
    } finally { $stream.Close() }
}

foreach ($item in $toMigrate) {
    $pathStr = $item.Path
    $fullPath = $item.FullPath
    $pointerPath = "$fullPath.payload_pointer.json"
    $payloadDest = Join-Path $PayloadRoot $pathStr

    if ($DryRun -and -not $Execute) {
        Write-Host "[DRY-RUN] Would migrate: $pathStr -> $payloadDest"
        $manifest.moved_files += @{
            original_path = $pathStr
            payload_path  = $payloadDest
            size_bytes    = $item.SizeBytes
            sha256        = "(computed on execute)"
        }
        continue
    }

    # Execute
    $shaBefore = Get-FileSha256 -Path $fullPath
    $dir = Split-Path $payloadDest -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Move-Item -Path $fullPath -Destination $payloadDest -Force
    $shaAfter = Get-FileSha256 -Path $payloadDest
    if ($shaBefore -ne $shaAfter) {
        Move-Item -Path $payloadDest -Destination $fullPath -Force
        Write-Error "SHA256 mismatch for $pathStr; restored original"
    }

    $pointer = @{
        original_path = $pathStr
        payload_path  = (Resolve-Path $payloadDest).Path
        sha256        = $shaAfter
        size_bytes    = $item.SizeBytes
        moved_at     = (Get-Date -Format "o")
    } | ConvertTo-Json -Compress

    [System.IO.File]::WriteAllText($pointerPath, $pointer, [System.Text.UTF8Encoding]::new($false))

    & git -C $RepoRoot rm --cached $pathStr 2>$null
    $pointerRel = $pathStr + ".payload_pointer.json"
    if ($pathStr -match "^installer\\Output\\") {
        & git -C $RepoRoot add -f $pointerRel
    } else {
        & git -C $RepoRoot add $pointerRel
    }

    $manifest.moved_files += @{
        original_path = $pathStr
        payload_path  = $payloadDest
        sha256        = $shaAfter
        size_bytes    = $item.SizeBytes
    }
    Write-Host "Migrated: $pathStr"
}

$reportDir = Join-Path $RepoRoot "docs\reports\verification"
if (-not (Test-Path $reportDir)) { New-Item -ItemType Directory -Path $reportDir -Force | Out-Null }
$dateStr = Get-Date -Format "yyyy-MM-dd_HHmm"
$manifestPath = Join-Path $reportDir "PAYLOAD_MIGRATION_MANIFEST_$dateStr.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content $manifestPath -Encoding UTF8
Write-Host "Manifest: $manifestPath"
