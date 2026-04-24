#Requires -Version 5.1
<#
.SYNOPSIS
  Targeted STT / preflight regression pack (Tasks 35 + 31 truth surfaces).

.DESCRIPTION
  Not a replacement for scripts/verify.ps1 -Quick or full verify.
  Run from repo root after activating the Python environment.

  Engine truth: runs ``python scripts/generate_engine_truth.py`` (default = v1 inventory)
  then ``python scripts/generate_engine_truth.py --schema v2`` — same outcome as
  ``--schema all`` but two invocations (do not document the pack as only ``--schema all``).

  Summary JSON: ``passed_count`` uses the **last** ``N passed`` match in full pytest output
  (not the first), so it matches pytest's session footer when earlier lines mention "passed".

.EXAMPLE
  .\scripts\stt_hardening_regress.ps1
#>
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot\..

Write-Host "== STT hardening regression pack ==" -ForegroundColor Cyan

$pytestArgs = @(
  "tests/unit/core/engines/test_router_stt_policy.py",
  "tests/unit/core/engines/test_whisper_cpp_engine.py",
  "tests/unit/backend/services/test_model_preflight.py",
  "tests/unit/backend/services/test_preflight_registry.py",
  "tests/unit/backend/api/routes/test_health.py::TestDetailedHealth::test_preflight_check",
  "tests/unit/scripts/test_generate_engine_truth.py",
  "tests/unit/scripts/test_truth_doc_markdown_links.py",
  "tests/unit/scripts/test_truth_session_verify_date_alignment.py",
  "tests/unit/scripts/test_engine_truth_overrides_references.py",
  "tests/unit/scripts/test_stt_hardening_regress_pack.py",
  "tests/unit/scripts/test_state_ledger_contract.py",
  "tests/unit/scripts/test_engine_truth_verify_artifact_alignment.py",
  "-q", "--tb=short"
)
# Pytest (and deps) may write benign warnings to stderr; do not let stderr records abort under $ErrorActionPreference = Stop.
$prevEap = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$pytestOutput = & python -m pytest @pytestArgs 2>&1 | Out-String
$pytestExit = $LASTEXITCODE
$ErrorActionPreference = $prevEap
if ($pytestExit -ne 0) {
  Write-Host $pytestOutput
  exit $pytestExit
}

Write-Host "== generate_engine_truth v1 + v2 ==" -ForegroundColor Cyan
& python scripts/generate_engine_truth.py
$genV1 = $LASTEXITCODE
if ($genV1 -ne 0) { exit $genV1 }
& python scripts/generate_engine_truth.py --schema v2
$genV2 = $LASTEXITCODE
if ($genV2 -ne 0) { exit $genV2 }

$passed = $null
$failed = $null
$passMatches = [regex]::Matches($pytestOutput, '(\d+)\s+passed')
if ($passMatches.Count -gt 0) {
  $passed = [int]$passMatches[$passMatches.Count - 1].Groups[1].Value
}
if ($pytestOutput -match '(\d+)\s+failed') { $failed = [int]$Matches[1] }

if ($pytestExit -eq 0) {
  if ($null -eq $passed) {
    Write-Error "STT pack: pytest exit 0 but could not parse passed count from output."
    exit 1
  }
  if ($null -eq $failed) { $failed = 0 }
}

$summaryDir = Join-Path $PSScriptRoot "..\docs\reports\verification\generated"
$summaryPath = Join-Path $summaryDir "stt_hardening_regress_summary.json"
New-Item -ItemType Directory -Force -Path $summaryDir | Out-Null
$summary = [ordered]@{
  schema_version       = 1
  timestamp_utc        = (Get-Date).ToUniversalTime().ToString("o")
  pytest_args          = @($pytestArgs)
  pytest_exit_code     = $pytestExit
  passed_count         = $passed
  failed_count         = $failed
  generate_engine_truth_v1_exit_code = $genV1
  generate_engine_truth_v2_exit_code = $genV2
  pytest_stdout_tail   = if ($pytestOutput.Length -gt 8000) {
    $pytestOutput.Substring($pytestOutput.Length - 8000)
  } else { $pytestOutput }
}
$jsonText = $summary | ConvertTo-Json -Depth 6
# UTF-8 without BOM so ``json.loads`` in schema tests does not fail (PowerShell ``Set-Content -Encoding utf8`` writes BOM).
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($summaryPath, $jsonText, $utf8NoBom)
Write-Host "Wrote $summaryPath" -ForegroundColor DarkGray

Write-Host "== STT pack summary schema test ==" -ForegroundColor Cyan
& python -m pytest "tests/unit/scripts/test_stt_hardening_regress_summary_schema.py" -q --tb=short
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== STT pack: PASS ==" -ForegroundColor Green
exit 0
