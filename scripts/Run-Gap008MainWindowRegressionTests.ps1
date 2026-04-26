# Runs the canonical GAP-008 MainWindow regression spine (MSTest filter from tools/gap008_mainwindow_regression_filter.txt).
# Strips # comment lines before --filter; writes discovery + summary under .buildlogs/gap008_spine/ (Tasks 418–419).
# Exit code follows dotnet test (non-zero on failure).
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$filterPath = Join-Path (Join-Path $repoRoot 'tools') 'gap008_mainwindow_regression_filter.txt'
if (-not (Test-Path -LiteralPath $filterPath)) {
    Write-Error "Missing filter file: $filterPath"
    exit 2
}

$rawLines = Get-Content -LiteralPath $filterPath
$effectiveLines = foreach ($line in $rawLines) {
    $t = $line.Trim()
    if ($t.Length -eq 0) { continue }
    if ($t.StartsWith('#')) { continue }
    $t
}
$filter = ($effectiveLines -join [Environment]::NewLine).Trim()
if ([string]::IsNullOrWhiteSpace($filter)) {
    Write-Error "Filter file has no non-comment filter line: $filterPath"
    exit 2
}

$artifactDir = Join-Path $repoRoot (Join-Path '.buildlogs' 'gap008_spine')
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$trxPath = Join-Path $artifactDir "gap008_spine_$stamp.trx"
$discoveryPath = Join-Path $artifactDir 'last_discovery.txt'
$summaryPath = Join-Path $artifactDir 'last_run_summary.json'

$csproj = Join-Path (Join-Path (Join-Path $repoRoot 'src') 'VoiceStudio.App.Tests') 'VoiceStudio.App.Tests.csproj'

Write-Host "[GAP-008] Discovery: dotnet test --list-tests (effective filter, no # lines)"
$disc = @(dotnet test $csproj -c Debug -p:Platform=x64 --list-tests --filter "$filter" 2>&1 | ForEach-Object { "$_" })
$disc | Out-File -LiteralPath $discoveryPath -Encoding utf8
$marker = ($disc | Select-String -SimpleMatch 'The following Tests are available:' | Select-Object -First 1)
$listed = 0
if ($null -ne $marker) {
    $idx = [array]::IndexOf($disc, $marker.Line)
    if ($idx -ge 0) {
        $listed = @($disc[($idx + 1)..($disc.Count - 1)] | Where-Object { $_ -match '^\s{4}\S' }).Count
    }
}

Write-Host "[GAP-008] Run: dotnet test (trx: $trxPath)"
dotnet test $csproj -c Debug -p:Platform=x64 --filter "$filter" -v q --logger "trx;LogFileName=$trxPath" @args
$exitCode = $LASTEXITCODE

$passed = $null
$failed = $null
$skipped = $null
if (Test-Path -LiteralPath $trxPath) {
    $trxRaw = Get-Content -LiteralPath $trxPath -Raw
    if ($trxRaw -match '\bpassed="(\d+)"') { $passed = [int]$Matches[1] }
    if ($trxRaw -match '\bfailed="(\d+)"') { $failed = [int]$Matches[1] }
    if ($trxRaw -match '\bnotExecuted="(\d+)"') { $skipped = [int]$Matches[1] }
}

$summary = [ordered]@{
    timestampUtc     = (Get-Date).ToUniversalTime().ToString('o')
    filterPath       = $filterPath
    effectiveFilter  = $filter
    discoveryPath    = $discoveryPath
    listedTestCount  = $listed
    trxPath          = $trxPath
    passed           = $passed
    failed           = $failed
    skippedApprox    = $skipped
    dotnetExitCode   = $exitCode
}
($summary | ConvertTo-Json -Depth 6) | Out-File -LiteralPath $summaryPath -Encoding utf8
Write-Host "[GAP-008] Artifacts: $discoveryPath , $summaryPath"
exit $exitCode
