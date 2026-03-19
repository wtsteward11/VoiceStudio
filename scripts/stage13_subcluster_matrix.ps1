<#
.SYNOPSIS
    Build Stage 13 subcluster matrix to isolate hang source.
.DESCRIPTION
    Partitions Services shard by class name prefix (A-C, D-G, M-P, R-Z).
    For each subcluster, runs: (1) isolation, (2) after Legacy, (3) after shards 1-7.
    Records results to artifacts/verify/stage13_subcluster_matrix.md.
    See stage_13_root_cause_diagnosis plan, Task A.
#>
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$TestProject = Join-Path $RootDir "src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj"
$ArtifactsDir = Join-Path $RootDir "artifacts\verify"
$MatrixFile = Join-Path $ArtifactsDir "stage13_subcluster_matrix.md"
New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

$BaseFilter = "TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke&FullyQualifiedName~VoiceStudio.App.Tests.Services"

# Subclusters by class name prefix (alphabetical)
$SubclusterAC = $BaseFilter + "&(FullyQualifiedName~.ABTestServiceTests|FullyQualifiedName~.AppStateStoreTests|FullyQualifiedName~.AudioPlayerServiceTests|FullyQualifiedName~.CommandRegistryTests|FullyQualifiedName~.ContextManagerTests|FullyQualifiedName~.DataEncryptionServiceTests|FullyQualifiedName~.StateCommandTests)"
$SubclusterDG = $BaseFilter + "&(FullyQualifiedName~.DegradedModeIntegrationTests|FullyQualifiedName~.DeferredServiceInitializerTests|FullyQualifiedName~.DragDropServiceTests|FullyQualifiedName~.DragPayloadTests|FullyQualifiedName~.WorkspaceRoundTripTests|FullyQualifiedName~.DropItemCommandTests|FullyQualifiedName~.ErrorCoordinatorTests|FullyQualifiedName~.EventAggregatorTests|FullyQualifiedName~.EventReplayServiceTests|FullyQualifiedName~.ExampleServiceTests|FullyQualifiedName~.GracefulDegradationServiceTests)"
$SubclusterMP = $BaseFilter + "&(FullyQualifiedName~.ModuleLoaderTests|FullyQualifiedName~.MultiSelectServiceTests|FullyQualifiedName~.OperationQueueServiceTests|FullyQualifiedName~.PanelRegistryTests|FullyQualifiedName~.PanelStateServiceTests|FullyQualifiedName~.PluginBridgeServiceTests|FullyQualifiedName~.PluginManagerTests|FullyQualifiedName~.ProfileEnhancementServiceTests|FullyQualifiedName~.ProfilesClientTests|FullyQualifiedName~.ProjectAudioClientTests|FullyQualifiedName~.ProjectsClientTests)"
$SubclusterRZ = $BaseFilter + "&(FullyQualifiedName~.RateLimitToastDedupeTests|FullyQualifiedName~.RequestCoordinatorTests|FullyQualifiedName~.RequestCoordinatorIntegrationTests|FullyQualifiedName~.RequestSignerTests|FullyQualifiedName~.SelectionBroadcastServiceTests|FullyQualifiedName~.StartupOverlayGatingTests|FullyQualifiedName~.StartupRetryCoordinatorTests|FullyQualifiedName~.StatusBarActivityServiceTests|FullyQualifiedName~.SynchronizedScrollServiceTests|FullyQualifiedName~.TimelineSynthesisServiceTests|FullyQualifiedName~.TimelineTrackServiceTests|FullyQualifiedName~.TimelineTranscriptionServiceTests|FullyQualifiedName~.ThemeManagerTests|FullyQualifiedName~.UndoRedoServiceTests|FullyQualifiedName~.VersionServiceTests|FullyQualifiedName~.ViewModelFactoryTests|FullyQualifiedName~.VoiceSynthesisServiceTests|FullyQualifiedName~.WorkflowCoordinatorServiceTests|FullyQualifiedName~.WorkspaceHistoryServiceTests)"

$Subclusters = @(
    @{ Name = "Services A-C"; Filter = $SubclusterAC }
    @{ Name = "Services D-G"; Filter = $SubclusterDG }
    @{ Name = "Services M-P"; Filter = $SubclusterMP }
    @{ Name = "Services R-Z"; Filter = $SubclusterRZ }
)

$LegacyFilter = "TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke&FullyQualifiedName~VoiceStudio.App.Tests.ViewModels&FullyQualifiedName!~SeamTests&FullyQualifiedName!~StalenessTests&TestCategory!=Lifecycle"

$Shard1_7Filters = @(
    "TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke&TestCategory!=Lifecycle&FullyQualifiedName~VoiceStudio.App.Tests.ViewModels&(FullyQualifiedName~SeamTests|FullyQualifiedName~StalenessTests)&(FullyQualifiedName~.Advanced|FullyQualifiedName~.Analyzer|FullyQualifiedName~.APIKey|FullyQualifiedName~.Assistant|FullyQualifiedName~.Audio|FullyQualifiedName~.Automation|FullyQualifiedName~.Analytics|FullyQualifiedName~.AIMixing|FullyQualifiedName~.AIProduction|FullyQualifiedName~.Backup|FullyQualifiedName~.Batch|FullyQualifiedName~.Dataset|FullyQualifiedName~.Deepfake|FullyQualifiedName~.Diagnostics)"
    "TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke&TestCategory!=Lifecycle&FullyQualifiedName~VoiceStudio.App.Tests.ViewModels&(FullyQualifiedName~SeamTests|FullyQualifiedName~StalenessTests)&(FullyQualifiedName~.Emotion|FullyQualifiedName~.Embedding|FullyQualifiedName~.Engine|FullyQualifiedName~.Ensemble|FullyQualifiedName~.Effects|FullyQualifiedName~.GPU|FullyQualifiedName~.Global|FullyQualifiedName~.Help)"
    "TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke&TestCategory!=Lifecycle&FullyQualifiedName~VoiceStudio.App.Tests.ViewModels&(FullyQualifiedName~SeamTests|FullyQualifiedName~StalenessTests)&(FullyQualifiedName~.Image|FullyQualifiedName~.Job|FullyQualifiedName~.Keyboard|FullyQualifiedName~.Library|FullyQualifiedName~.Lexicon)"
    "TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke&TestCategory!=Lifecycle&FullyQualifiedName~VoiceStudio.App.Tests.ViewModels&(FullyQualifiedName~SeamTests|FullyQualifiedName~StalenessTests)&(FullyQualifiedName~.Macro|FullyQualifiedName~.Model|FullyQualifiedName~.Multilingual|FullyQualifiedName~.MCP|FullyQualifiedName~.Mix|FullyQualifiedName~.Marker|FullyQualifiedName~.Multi)"
    "TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke&TestCategory!=Lifecycle&FullyQualifiedName~VoiceStudio.App.Tests.ViewModels&(FullyQualifiedName~SeamTests|FullyQualifiedName~StalenessTests)&(FullyQualifiedName~.Plugin|FullyQualifiedName~.Preset|FullyQualifiedName~.Pipeline|FullyQualifiedName~.Prosody|FullyQualifiedName~.Profile|FullyQualifiedName~.Pronunciation|FullyQualifiedName~.Quality|FullyQualifiedName~.RealTime|FullyQualifiedName~.Recording|FullyQualifiedName~.Sonography|FullyQualifiedName~.Spatial|FullyQualifiedName~.Spectrogram|FullyQualifiedName~.SSML|FullyQualifiedName~.Settings|FullyQualifiedName~.SLO|FullyQualifiedName~.Script|FullyQualifiedName~.StyleTransfer|FullyQualifiedName~.Scene|FullyQualifiedName~.Text|FullyQualifiedName~.Todo|FullyQualifiedName~.Tag|FullyQualifiedName~.Training|FullyQualifiedName~.Template|FullyQualifiedName~.Transcribe|FullyQualifiedName~.Ultimate|FullyQualifiedName~.Upscaling|FullyQualifiedName~.Video|FullyQualifiedName~.Voice|FullyQualifiedName~.Workflow)"
    "TestCategory=Lifecycle&FullyQualifiedName~ViewModels"
    $LegacyFilter
)

function Invoke-Cleanup {
    Get-Process -Name "testhost" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process -Name "VoiceStudio.App" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
}

function Run-Tests {
    param([string]$Filter, [int]$TimeoutSec = 120)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $out = & dotnet test $TestProject -c Debug -p:Platform=x64 --no-build --filter $Filter --logger "console;verbosity=minimal" 2>&1
    $sw.Stop()
    $exitCode = $LASTEXITCODE
    return @{ ExitCode = $exitCode; RuntimeSec = [int]$sw.Elapsed.TotalSeconds; Output = $out }
}

function Run-Shards1To7 {
    Invoke-Cleanup
    Start-Sleep -Seconds 3
    foreach ($f in $Shard1_7Filters) {
        $r = Run-Tests -Filter $f
        if ($r.ExitCode -ne 0) { return $r }
        Invoke-Cleanup
        Start-Sleep -Milliseconds 500
    }
    return @{ ExitCode = 0 }
}

# Build
Write-Host "Stage 13 Subcluster Matrix" -ForegroundColor Cyan
Write-Host "=========================" -ForegroundColor Cyan
Write-Host "[Build] Building..." -ForegroundColor Yellow
dotnet build $TestProject -c Debug -p:Platform=x64 --verbosity minimal -nologo
if ($LASTEXITCODE -ne 0) { exit 1 }

$results = @()
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

foreach ($sc in $Subclusters) {
    Write-Host ""
    Write-Host "--- $($sc.Name) ---" -ForegroundColor Cyan

    # 1. Isolation
    Write-Host "  [1/3] Isolation..." -ForegroundColor Yellow
    Invoke-Cleanup
    Start-Sleep -Seconds 3
    $r1 = Run-Tests -Filter $sc.Filter
    $results += [PSCustomObject]@{ Subset = $sc.Name; Mode = "Isolation"; Preceding = "None"; Pass = ($r1.ExitCode -eq 0); Runtime = $r1.RuntimeSec; Notes = "" }

    # 2. After Legacy
    Write-Host "  [2/3] After Legacy..." -ForegroundColor Yellow
    Invoke-Cleanup
    Start-Sleep -Seconds 3
    $rLegacy = Run-Tests -Filter $LegacyFilter
    if ($rLegacy.ExitCode -ne 0) { $results += [PSCustomObject]@{ Subset = $sc.Name; Mode = "After Legacy"; Preceding = "Legacy"; Pass = $false; Runtime = $rLegacy.RuntimeSec; Notes = "Legacy failed" }; continue }
    Invoke-Cleanup
    Start-Sleep -Seconds 5
    $r2 = Run-Tests -Filter $sc.Filter
    $results += [PSCustomObject]@{ Subset = $sc.Name; Mode = "After Legacy"; Preceding = "Legacy"; Pass = ($r2.ExitCode -eq 0); Runtime = $r2.RuntimeSec; Notes = "" }

    # 3. After shards 1-7
    Write-Host "  [3/3] After shards 1-7..." -ForegroundColor Yellow
    $rPre = Run-Shards1To7
    if ($rPre.ExitCode -ne 0) { $results += [PSCustomObject]@{ Subset = $sc.Name; Mode = "After 1-7"; Preceding = "Shards 1-7"; Pass = $false; Runtime = 0; Notes = "Shards 1-7 failed" }; continue }
    Invoke-Cleanup
    Start-Sleep -Seconds 5
    $r3 = Run-Tests -Filter $sc.Filter
    $results += [PSCustomObject]@{ Subset = $sc.Name; Mode = "After 1-7"; Preceding = "Shards 1-7"; Pass = ($r3.ExitCode -eq 0); Runtime = $r3.RuntimeSec; Notes = "" }
}

# Write matrix markdown
$md = @"
# Stage 13 Subcluster Matrix

**Generated:** $timestamp
**Purpose:** Isolate smallest reproducible hang source in Services shard.

## Results

| Subset | Mode | Preceding Sequence | Pass | Runtime (s) | Notes |
|--------|------|-------------------|------|-------------|-------|
"@
foreach ($row in $results) {
    $passStr = if ($row.Pass) { "PASS" } else { "FAIL" }
    $md += "`n| $($row.Subset) | $($row.Mode) | $($row.Preceding) | $passStr | $($row.Runtime) | $($row.Notes) |"
}
$md += @"

## Commands (for manual re-run)

``````powershell
# Services A-C
dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "$SubclusterAC"

# Services D-G
dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "$SubclusterDG"

# Services M-P
dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "$SubclusterMP"

# Services R-Z
dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "$SubclusterRZ"
``````

## Next Steps

- Identify smallest subcluster that FAILs after Legacy or after 1-7.
- Inspect that subcluster's fixtures (Task B).
"@

$md | Out-File -FilePath $MatrixFile -Encoding utf8
Write-Host ""
Write-Host "Matrix written to: $MatrixFile" -ForegroundColor Green
$failCount = ($results | Where-Object { -not $_.Pass }).Count
if ($failCount -gt 0) {
    Write-Host "FAIL count: $failCount" -ForegroundColor Red
    exit 1
}
exit 0
