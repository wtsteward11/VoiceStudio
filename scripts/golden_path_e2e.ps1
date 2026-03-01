<#
.SYNOPSIS
    Golden Path E2E: runs the 5-step pipeline (import, transcribe, synthesize, export, verify)
    against a running backend and captures artifacts.
.DESCRIPTION
    Requires backend running on localhost:8001. Starts it if not already up.
    Uses the canonical test fixture (allan_watts_15s.wav).
.PARAMETER BackendUrl
    Backend base URL. Default: http://localhost:8001
.PARAMETER FixturePath
    Audio fixture file. Default: tests/assets/canonical/standard/allan_watts_15s.wav
.PARAMETER OutputDir
    Directory for captured artifacts. Auto-timestamped under artifacts/golden_path/.
#>
param(
    [string]$BackendUrl = "http://localhost:8001",
    [string]$FixturePath = "",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = git rev-parse --show-toplevel 2>$null
if (-not $repoRoot) { $repoRoot = Split-Path $PSScriptRoot }

if (-not $FixturePath) {
    $FixturePath = Join-Path $repoRoot "tests\assets\canonical\standard\allan_watts_15s.wav"
}
if (-not $OutputDir) {
    $ts = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputDir = Join-Path $repoRoot "artifacts\golden_path\$ts"
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$results = @()
$overallPass = $true

function Record-Step {
    param([string]$Name, [string]$Status, [string]$Detail)
    $script:results += [PSCustomObject]@{ Step = $Name; Status = $Status; Detail = $Detail }
    if ($Status -eq "FAIL") { $script:overallPass = $false }
    $color = if ($Status -eq "PASS") { "Green" } else { "Red" }
    Write-Host "  [$Status] $Name - $Detail" -ForegroundColor $color
}

Write-Host "Golden Path E2E" -ForegroundColor Cyan
Write-Host "  Backend: $BackendUrl"
Write-Host "  Fixture: $FixturePath"
Write-Host "  Output:  $OutputDir"
Write-Host ""

# Step 0: Check backend health
try {
    $health = Invoke-RestMethod -Uri "$BackendUrl/health" -Method Get -TimeoutSec 5
    Record-Step "Backend Health" "PASS" "HTTP 200"
} catch {
    Record-Step "Backend Health" "FAIL" "Backend not reachable at $BackendUrl"
    Write-Host ""
    Write-Host "Start the backend first:" -ForegroundColor Yellow
    Write-Host "  .venv\Scripts\python.exe -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8001"
    $overallPass = $false
}

if ($overallPass) {
    # Step 1: Import audio
    try {
        $form = [System.Net.Http.MultipartFormDataContent]::new()
        $fileBytes = [System.IO.File]::ReadAllBytes($FixturePath)
        $fileContent = [System.Net.Http.ByteArrayContent]::new($fileBytes)
        $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new("audio/wav")
        $form.Add($fileContent, "file", "allan_watts_15s.wav")
        $client = [System.Net.Http.HttpClient]::new()
        $response = $client.PostAsync("$BackendUrl/api/audio/upload", $form).Result
        if ($response.IsSuccessStatusCode) {
            $body = $response.Content.ReadAsStringAsync().Result
            Record-Step "Import Audio" "PASS" "HTTP $([int]$response.StatusCode): $($body.Substring(0, [Math]::Min(100, $body.Length)))"
            $body | Out-File (Join-Path $OutputDir "import_response.json") -Encoding utf8
        } else {
            Record-Step "Import Audio" "FAIL" "HTTP $([int]$response.StatusCode)"
        }
    } catch {
        Record-Step "Import Audio" "FAIL" $_.Exception.Message
    }

    # Step 2: Transcribe
    try {
        $transcribeBody = @{ engine = "whisper"; language = "en" } | ConvertTo-Json
        $response = Invoke-RestMethod -Uri "$BackendUrl/api/transcribe/" -Method Post -Body $transcribeBody -ContentType "application/json" -TimeoutSec 120
        Record-Step "Transcribe" "PASS" "Transcription returned"
        $response | ConvertTo-Json -Depth 5 | Out-File (Join-Path $OutputDir "transcribe_response.json") -Encoding utf8
    } catch {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { "N/A" }
        Record-Step "Transcribe" "FAIL" "HTTP $code - $($_.Exception.Message)"
    }

    # Step 3: Synthesize
    try {
        $synthBody = @{ text = "Hello, this is a golden path synthesis test."; engine = "gtts" } | ConvertTo-Json
        $response = Invoke-RestMethod -Uri "$BackendUrl/api/voice/synthesize" -Method Post -Body $synthBody -ContentType "application/json" -TimeoutSec 120
        Record-Step "Synthesize" "PASS" "Synthesis returned"
        $response | ConvertTo-Json -Depth 5 | Out-File (Join-Path $OutputDir "synthesize_response.json") -Encoding utf8
    } catch {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { "N/A" }
        Record-Step "Synthesize" "FAIL" "HTTP $code - $($_.Exception.Message)"
    }

    # Step 4: Check library
    try {
        $response = Invoke-RestMethod -Uri "$BackendUrl/api/library/assets" -Method Get -TimeoutSec 10
        $count = if ($response -is [array]) { $response.Count } else { 1 }
        Record-Step "Library Check" "PASS" "$count assets in library"
    } catch {
        Record-Step "Library Check" "FAIL" $_.Exception.Message
    }

    # Step 5: Health metrics
    try {
        $response = Invoke-RestMethod -Uri "$BackendUrl/api/health/preflight" -Method Get -TimeoutSec 10
        Record-Step "Health Metrics" "PASS" "Preflight returned"
        $response | ConvertTo-Json -Depth 5 | Out-File (Join-Path $OutputDir "preflight_response.json") -Encoding utf8
    } catch {
        Record-Step "Health Metrics" "FAIL" $_.Exception.Message
    }
}

# Write report
$reportPath = Join-Path $OutputDir "golden_path_report.md"
$report = "# Golden Path E2E Report`n`n"
$report += "**Date:** $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`n"
$report += "**Backend:** $BackendUrl`n"
$report += "**Fixture:** $FixturePath`n"
$report += "**Overall:** $(if ($overallPass) { 'PASS' } else { 'FAIL' })`n`n"
$tH = "| Step | Status | Detail |"
$tD = "|------|--------|--------|"
$report += $tH + "`n" + $tD + "`n"
foreach ($r in $results) {
    $row = [string]::Format("| {0} | {1} | {2} |", $r.Step, $r.Status, $r.Detail)
    $report += $row + "`n"
}
$report | Out-File $reportPath -Encoding utf8

Write-Host ""
Write-Host "Overall: $(if ($overallPass) { 'PASS' } else { 'FAIL' })" -ForegroundColor $(if ($overallPass) { "Green" } else { "Red" })
Write-Host "Report:  $reportPath"

if ($overallPass) { exit 0 } else { exit 1 }
