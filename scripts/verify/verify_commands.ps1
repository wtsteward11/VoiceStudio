# VoiceStudio Command Registry Verification Script
# Runs command handler tests and validates command health
# Exit code: 0 = all tests pass, 1 = failures detected

param(
    [switch]$Verbose,
    [switch]$SkipBuild,
    [string]$Filter = "Commands"
)

$ErrorActionPreference = "Stop"
$script:exitCode = 0

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "=" * 60 -ForegroundColor Cyan
    Write-Host " $Message" -ForegroundColor Cyan
    Write-Host "=" * 60 -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[PASS] $Message" -ForegroundColor Green
}

function Write-Failure {
    param([string]$Message)
    Write-Host "[FAIL] $Message" -ForegroundColor Red
    $script:exitCode = 1
}

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Yellow
}

# Navigate to project root
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (Test-Path "$PSScriptRoot\..\src") {
    $projectRoot = Split-Path -Parent $PSScriptRoot
}
Set-Location $projectRoot
Write-Info "Project root: $projectRoot"

# Build if not skipped
if (-not $SkipBuild) {
    Write-Header "Building Test Project"
    
    $buildResult = & dotnet build src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Write-Failure "Build failed"
        if ($Verbose) {
            $buildResult | ForEach-Object { Write-Host $_ }
        }
        exit 1
    }
    Write-Success "Build completed"
}

# Run command handler tests
Write-Header "Running Command Handler Tests"

$testArgs = @(
    "test",
    "src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj",
    "-c", "Debug",
    "-p:Platform=x64",
    "--no-build",
    "--filter", "TestCategory=$Filter",
    "--logger", "console;verbosity=minimal"
)

if ($Verbose) {
    $testArgs[-1] = "console;verbosity=detailed"
}

Write-Info "Running: dotnet $($testArgs -join ' ')"

$testResult = & dotnet @testArgs 2>&1
$testExitCode = $LASTEXITCODE

# Parse test results
$passedTests = 0
$failedTests = 0
$skippedTests = 0

foreach ($line in $testResult) {
    if ($line -match "Passed:\s*(\d+)") {
        $passedTests = [int]$Matches[1]
    }
    if ($line -match "Failed:\s*(\d+)") {
        $failedTests = [int]$Matches[1]
    }
    if ($line -match "Skipped:\s*(\d+)") {
        $skippedTests = [int]$Matches[1]
    }
    
    # Always show test summary lines
    if ($line -match "(Passed|Failed|Skipped|Total tests)" -or $Verbose) {
        Write-Host $line
    }
}

# Report results
Write-Header "Test Summary"

Write-Host "  Passed:  $passedTests" -ForegroundColor $(if ($passedTests -gt 0) { "Green" } else { "Gray" })
Write-Host "  Failed:  $failedTests" -ForegroundColor $(if ($failedTests -gt 0) { "Red" } else { "Gray" })
Write-Host "  Skipped: $skippedTests" -ForegroundColor $(if ($skippedTests -gt 0) { "Yellow" } else { "Gray" })

if ($testExitCode -ne 0 -or $failedTests -gt 0) {
    Write-Failure "Command handler tests failed"
    $script:exitCode = 1
} else {
    Write-Success "All command handler tests passed"
}

# Check for command registration completeness
Write-Header "Command Registration Check"

$handlerFiles = Get-ChildItem -Path "src/VoiceStudio.App/Commands" -Filter "*Handler.cs" -ErrorAction SilentlyContinue
$expectedCategories = @("File", "Profile", "Playback", "Navigation", "Settings")
$foundCategories = @()

foreach ($handler in $handlerFiles) {
    $content = Get-Content $handler.FullName -Raw
    
    # Extract category from RegisterCommands
    if ($content -match 'Category\s*=\s*"([^"]+)"') {
        $category = $Matches[1]
        if ($category -notin $foundCategories) {
            $foundCategories += $category
        }
    }
    
    # Count command registrations
    $registrations = ([regex]::Matches($content, '_registry\.Register\(')).Count
    Write-Info "$($handler.BaseName): $registrations command(s) registered"
}

# Check all expected categories are covered
$missingCategories = $expectedCategories | Where-Object { $_ -notin $foundCategories }
if ($missingCategories.Count -gt 0) {
    Write-Failure "Missing command categories: $($missingCategories -join ', ')"
} else {
    Write-Success "All expected command categories covered"
}

# Final status
Write-Header "Verification Complete"

if ($script:exitCode -eq 0) {
    Write-Success "All verifications passed"
} else {
    Write-Failure "Some verifications failed"
}

exit $script:exitCode
