<#
.SYNOPSIS
    Analyze MSBuild binlog to extract XamlCompiler.exe invocations and identify failing XAML files.

.DESCRIPTION
    This script parses an MSBuild binary log (.binlog) to extract XAML compiler information,
    helping diagnose silent WinUI 3 XAML compiler failures where XamlCompiler.exe exits
    with code 1 and produces no output.json.

    The script:
    - Searches for XamlCompiler.exe task invocations
    - Extracts command line, input.json path, and output.json path
    - Parses input.json to identify XamlPages array
    - Detects Views subfolder patterns (GitHub #10947)
    - Generates actionable remediation suggestions

    References:
    - GitHub Issue #10027: Can't get error output from XamlCompiler.exe
    - GitHub Issue #10947: XamlCompiler.exe exits code 1 for Views subfolders

.PARAMETER BinlogPath
    Path to the MSBuild binary log file. Supports wildcards.
    Default: .buildlogs\build_diagnostic_*.binlog (most recent)

.PARAMETER OutputFormat
    Output format: 'text' (default), 'json', or 'summary'

.PARAMETER ShowAllTasks
    If specified, shows all XamlCompiler tasks (not just failures)

.EXAMPLE
    .\scripts\analyze-binlog.ps1 -BinlogPath .buildlogs\build_diagnostic_20260204.binlog
    # Analyzes a specific binlog file

.EXAMPLE
    .\scripts\analyze-binlog.ps1
    # Analyzes the most recent diagnostic binlog

.PARAMETER OutputFile
    Path to write the analysis output (for CI consumption).
    If not specified, output is written to console only.

.EXAMPLE
    .\scripts\analyze-binlog.ps1 -OutputFormat json
    # Outputs analysis results as JSON for programmatic consumption

.EXAMPLE
    .\scripts\analyze-binlog.ps1 -OutputFile .buildlogs\analysis-output.txt
    # Writes analysis results to file for CI consumption
#>

Param(
    [string]$BinlogPath = "",
    
    [ValidateSet("text", "json", "summary")]
    [string]$OutputFormat = "text",
    
    [string]$OutputFile = "",
    
    [switch]$ShowAllTasks
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ============================================================================
# Configuration
# ============================================================================

$repoRoot = Resolve-Path "$PSScriptRoot\.."
if ($repoRoot -is [System.Management.Automation.PathInfo]) {
    $repoRoot = $repoRoot.Path
}

$buildlogsDir = Join-Path $repoRoot ".buildlogs"

# ============================================================================
# Find binlog file
# ============================================================================

if (-not $BinlogPath -or $BinlogPath.Trim() -eq "") {
    # Find most recent diagnostic binlog
    $binlogs = Get-ChildItem -Path $buildlogsDir -Filter "build_diagnostic_*.binlog" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending
    
    if ($binlogs.Count -eq 0) {
        # Try any binlog
        $binlogs = Get-ChildItem -Path $buildlogsDir -Filter "*.binlog" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending
    }
    
    if ($binlogs.Count -eq 0) {
        Write-Host "ERROR: No binlog files found in $buildlogsDir" -ForegroundColor Red
        Write-Host ""
        Write-Host "Run a diagnostic build first:" -ForegroundColor Yellow
        Write-Host "  .\scripts\build-with-binlog.ps1"
        exit 1
    }
    
    $BinlogPath = $binlogs[0].FullName
    Write-Host "Using most recent binlog: $BinlogPath" -ForegroundColor Cyan
} elseif ($BinlogPath.Contains("*")) {
    # Wildcard path - find matching files
    $binlogs = Get-ChildItem -Path $BinlogPath -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending
    
    if ($binlogs.Count -eq 0) {
        Write-Host "ERROR: No binlog files match pattern: $BinlogPath" -ForegroundColor Red
        exit 1
    }
    
    $BinlogPath = $binlogs[0].FullName
    Write-Host "Using most recent matching binlog: $BinlogPath" -ForegroundColor Cyan
}

if (-not (Test-Path $BinlogPath)) {
    Write-Host "ERROR: Binlog file not found: $BinlogPath" -ForegroundColor Red
    exit 1
}

# ============================================================================
# Analysis results structure
# ============================================================================

$analysisResults = @{
    BinlogPath = $BinlogPath
    Timestamp = (Get-Date).ToString("o")
    XamlCompilerTasks = @()
    InputJsonFiles = @()
    XamlPages = @()
    NestedViewsXaml = @()
    Warnings = @()
    Recommendations = @()
}

# ============================================================================
# Method 0: Try StructuredLogger CLI (preferred - more accurate)
# ============================================================================

function Search-WithStructuredLogger {
    param([string]$BinlogPath)
    
    # Check if structuredlogviewer (slv) CLI is available
    $structuredLoggerAvailable = $false
    $slvPath = $null
    
    # Try to find the tool in PATH
    try {
        $slvPath = Get-Command "slv" -ErrorAction SilentlyContinue
        if ($slvPath) {
            $structuredLoggerAvailable = $true
        }
    } catch {}
    
    # Try dotnet tool
    if (-not $structuredLoggerAvailable) {
        try {
            $toolList = & dotnet tool list --global 2>&1
            if ($toolList -match "msbuild.structuredlogger") {
                $structuredLoggerAvailable = $true
                $slvPath = "slv"
            }
        } catch {}
    }
    
    if (-not $structuredLoggerAvailable) {
        Write-Host "  StructuredLogger CLI not available - using binary fallback" -ForegroundColor Gray
        return $null
    }
    
    Write-Host "  Using StructuredLogger CLI for analysis" -ForegroundColor Green
    
    $results = @{
        XamlCompilerCommands = @()
        InputJsonPaths = @()
        OutputJsonPaths = @()
        Tasks = @()
    }
    
    try {
        # Use slv to search for XamlCompiler tasks
        # The slv tool can search binlogs: slv search <binlog> <pattern>
        $searchOutput = & slv search $BinlogPath "XamlCompiler" 2>&1
        
        if ($LASTEXITCODE -eq 0 -and $searchOutput) {
            foreach ($line in $searchOutput) {
                $lineStr = $line.ToString()
                if ($lineStr -match "XamlCompiler") {
                    $results.XamlCompilerCommands += $lineStr
                }
                if ($lineStr -match "input\.json") {
                    if ($lineStr -match '([A-Za-z]:\\[^"<>|*?\s]+input\.json)') {
                        $results.InputJsonPaths += $Matches[1]
                    }
                }
            }
        }
        
        # Also search for errors
        $errorOutput = & slv search $BinlogPath "error" 2>&1
        if ($LASTEXITCODE -eq 0 -and $errorOutput) {
            $results.Tasks = @($errorOutput | Where-Object { $_ -match "XAML|xaml|XamlCompiler" })
        }
        
        return $results
    }
    catch {
        Write-Host "  StructuredLogger search failed: $_" -ForegroundColor Yellow
        return $null
    }
}

# ============================================================================
# Method 1: Search binlog content directly (fallback - less accurate)
# ============================================================================

function Search-BinlogContent {
    param([string]$BinlogPath)
    
    # Binlogs are binary, but we can try to extract text patterns
    # This is a fallback method when StructuredLogger is not available
    
    $results = @{
        XamlCompilerCommands = @()
        InputJsonPaths = @()
        OutputJsonPaths = @()
    }
    
    # Try to find XamlCompiler invocations in the binary content
    try {
        $bytes = [System.IO.File]::ReadAllBytes($BinlogPath)
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        
        # Search for XamlCompiler.exe patterns
        $xamlCompilerMatches = [regex]::Matches($text, 'XamlCompiler\.exe[^\x00]{0,500}')
        foreach ($match in $xamlCompilerMatches) {
            $results.XamlCompilerCommands += $match.Value -replace '[\x00-\x1F]', ' '
        }
        
        # Search for input.json patterns
        $inputJsonMatches = [regex]::Matches($text, '[^\x00]{0,200}input\.json')
        foreach ($match in $inputJsonMatches) {
            $path = $match.Value -replace '[\x00-\x1F]', ''
            if ($path -match '([A-Za-z]:\\[^"<>|*?\x00]+input\.json)') {
                $results.InputJsonPaths += $Matches[1]
            }
        }
        
        # Search for output.json patterns
        $outputJsonMatches = [regex]::Matches($text, 'output\.json[^\x00]{0,200}')
        foreach ($match in $outputJsonMatches) {
            $path = $match.Value -replace '[\x00-\x1F]', ''
            if ($path -match '(output\.json)') {
                $results.OutputJsonPaths += $path
            }
        }
    }
    catch {
        Write-Host "  Warning: Could not parse binlog binary content: $_" -ForegroundColor Yellow
    }
    
    return $results
}

# ============================================================================
# Method 2: Parse input.json files
# ============================================================================

function Parse-InputJson {
    param([string]$InputJsonPath)
    
    $result = @{
        XamlPages = @()
        ApplicationDefinition = $null
        RootNamespace = $null
    }
    
    if (-not (Test-Path $InputJsonPath)) {
        return $result
    }
    
    try {
        $json = Get-Content $InputJsonPath -Raw | ConvertFrom-Json
        
        if ($json.XamlPages) {
            $result.XamlPages = @($json.XamlPages | ForEach-Object {
                @{
                    FullPath = $_.FullPath
                    ItemSpec = $_.ItemSpec
                    Link = $_.Link
                }
            })
        }
        
        if ($json.ApplicationDefinition) {
            $result.ApplicationDefinition = $json.ApplicationDefinition.FullPath
        }
        
        if ($json.RootNamespace) {
            $result.RootNamespace = $json.RootNamespace
        }
    }
    catch {
        Write-Host "  Warning: Could not parse input.json: $_" -ForegroundColor Yellow
    }
    
    return $result
}

# ============================================================================
# Detect nested Views XAML (GitHub #10947)
# ============================================================================

function Detect-NestedViewsXaml {
    param([array]$XamlPages)
    
    $nested = @()
    
    foreach ($page in $XamlPages) {
        $fullPath = $page.FullPath
        if (-not $fullPath) { continue }
        
        # Pattern: Views\subfolder\file.xaml (more than one level under Views)
        if ($fullPath -match '\\Views\\[^\\]+\\[^\\]+\.xaml$') {
            $nested += @{
                FullPath = $fullPath
                Issue = "XAML file in nested Views subfolder may cause XamlCompiler.exe to fail silently (GitHub #10947)"
                Recommendation = "Move to Views/ root: $(Split-Path $fullPath -Leaf)"
            }
        }
    }
    
    return $nested
}

# ============================================================================
# Main analysis
# ============================================================================

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  MSBuild Binlog Analysis for XAML Compiler Issues" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Binlog: $BinlogPath" -ForegroundColor Gray
Write-Host "Size  : $([math]::Round((Get-Item $BinlogPath).Length / 1MB, 2)) MB" -ForegroundColor Gray
Write-Host ""

# Search binlog content - try StructuredLogger first, fall back to binary parsing
Write-Host "Step 1: Searching binlog for XamlCompiler invocations..." -ForegroundColor Yellow

# Method 0: Try StructuredLogger CLI (preferred)
$binlogContent = Search-WithStructuredLogger -BinlogPath $BinlogPath

# Method 1: Fall back to binary parsing if StructuredLogger not available
if (-not $binlogContent) {
    Write-Host "  Falling back to binary content parsing..." -ForegroundColor Gray
    $binlogContent = Search-BinlogContent -BinlogPath $BinlogPath
}

if ($binlogContent.XamlCompilerCommands.Count -gt 0) {
    Write-Host "  Found $($binlogContent.XamlCompilerCommands.Count) XamlCompiler references" -ForegroundColor Green
    $analysisResults.XamlCompilerTasks = $binlogContent.XamlCompilerCommands | Select-Object -Unique
} else {
    Write-Host "  No XamlCompiler references found in binlog" -ForegroundColor Yellow
    $analysisResults.Warnings += "No XamlCompiler.exe invocations found in binlog"
}

# Find input.json files
Write-Host ""
Write-Host "Step 2: Locating input.json files..." -ForegroundColor Yellow

$inputJsonPaths = $binlogContent.InputJsonPaths | Select-Object -Unique
if ($inputJsonPaths.Count -eq 0) {
    # Fallback: search in known obj locations
    $objDirs = @(
        (Join-Path $repoRoot "src\VoiceStudio.App\obj"),
        (Join-Path $repoRoot "obj")
    )
    
    foreach ($objDir in $objDirs) {
        $found = Get-ChildItem -Path $objDir -Filter "input.json" -Recurse -ErrorAction SilentlyContinue
        $inputJsonPaths += $found.FullName
    }
}

$analysisResults.InputJsonFiles = $inputJsonPaths

if ($inputJsonPaths.Count -gt 0) {
    Write-Host "  Found $($inputJsonPaths.Count) input.json file(s)" -ForegroundColor Green
    
    foreach ($inputJsonPath in $inputJsonPaths) {
        if (Test-Path $inputJsonPath) {
            Write-Host "  Parsing: $inputJsonPath" -ForegroundColor Gray
            $parsed = Parse-InputJson -InputJsonPath $inputJsonPath
            $analysisResults.XamlPages += $parsed.XamlPages
        }
    }
} else {
    Write-Host "  No input.json files found" -ForegroundColor Yellow
    $analysisResults.Warnings += "No input.json files found - build may not have reached XAML compilation"
}

# Analyze XAML pages
Write-Host ""
Write-Host "Step 3: Analyzing XAML pages for known issues..." -ForegroundColor Yellow

$uniquePages = $analysisResults.XamlPages | ForEach-Object { $_.FullPath } | Select-Object -Unique
Write-Host "  Total XAML pages: $($uniquePages.Count)" -ForegroundColor Gray

# Detect nested Views
$nestedViews = Detect-NestedViewsXaml -XamlPages $analysisResults.XamlPages
$analysisResults.NestedViewsXaml = $nestedViews

if ($nestedViews.Count -gt 0) {
    Write-Host "  WARNING: Found $($nestedViews.Count) XAML file(s) in nested Views subfolders!" -ForegroundColor Red
    foreach ($nested in $nestedViews) {
        $analysisResults.Warnings += $nested.Issue
    }
} else {
    Write-Host "  No nested Views subfolders detected (good)" -ForegroundColor Green
}

# Generate recommendations
Write-Host ""
Write-Host "Step 4: Generating recommendations..." -ForegroundColor Yellow

if ($nestedViews.Count -gt 0) {
    $analysisResults.Recommendations += "CRITICAL: Flatten XAML files in nested Views subfolders to Views/ root"
    $analysisResults.Recommendations += "See GitHub #10947: https://github.com/microsoft/microsoft-ui-xaml/issues/10947"
}

if ($analysisResults.Warnings.Count -eq 0) {
    $analysisResults.Recommendations += "Build appears healthy. If issues persist:"
    $analysisResults.Recommendations += "  1. Open binlog in MSBuild Structured Log Viewer"
    $analysisResults.Recommendations += "  2. Search for 'XamlCompiler' to find exact task"
    $analysisResults.Recommendations += "  3. Check for TextElement.* attached properties on ContentPresenter"
    $analysisResults.Recommendations += "  4. Run xaml-binary-search.ps1 to isolate problematic file"
}

# ============================================================================
# Output results
# ============================================================================

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  ANALYSIS RESULTS" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# Build output content for file writing
$outputContent = ""

switch ($OutputFormat) {
    "json" {
        $jsonOutput = $analysisResults | ConvertTo-Json -Depth 10
        Write-Host $jsonOutput
        $outputContent = $jsonOutput
    }
    "summary" {
        $summaryLines = @()
        $summaryLines += "XAML Pages     : $($uniquePages.Count)"
        $summaryLines += "Nested Views   : $($nestedViews.Count)"
        $summaryLines += "Warnings       : $($analysisResults.Warnings.Count)"
        $summaryLines += ""
        if ($analysisResults.Recommendations.Count -gt 0) {
            $summaryLines += "Recommendations:"
            foreach ($rec in $analysisResults.Recommendations) {
                $summaryLines += "  - $rec"
            }
        }
        foreach ($line in $summaryLines) {
            Write-Host $line
        }
        $outputContent = $summaryLines -join "`n"
    }
    default {
        # Text output - capture for file
        $textLines = @()
        $textLines += "============================================================"
        $textLines += "  XAML COMPILER BINLOG ANALYSIS"
        $textLines += "============================================================"
        $textLines += ""
        $textLines += "Binlog: $BinlogPath"
        $textLines += "Analyzed: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        $textLines += ""
        
        # First XAML file (likely culprit)
        if ($analysisResults.XamlPages.Count -gt 0) {
            $firstXamlFile = $analysisResults.XamlPages[0].FullPath
            $textLines += "============================================================"
            $textLines += "  LIKELY CULPRIT (first XAML in input.json)"
            $textLines += "============================================================"
            $textLines += ""
            $textLines += "  $firstXamlFile"
            $textLines += ""
            
            # Check if this file is nested
            if ($firstXamlFile -match '\\Views\\[^\\]+\\[^\\]+\.xaml$') {
                $textLines += "  This file is in a nested Views subfolder (GitHub #10947)."
                $textLines += ""
                $fileName = Split-Path $firstXamlFile -Leaf
                $textLines += "  FIX: Move to Views root:"
                $textLines += "    Move-Item `"$firstXamlFile`" `"$(Split-Path (Split-Path $firstXamlFile))\$fileName`""
            }
            $textLines += ""
        }
        
        if ($nestedViews.Count -gt 0) {
            $textLines += "============================================================"
            $textLines += "  NESTED VIEWS XAML (likely cause of silent failures)"
            $textLines += "============================================================"
            $textLines += ""
            foreach ($nested in $nestedViews) {
                Write-Host "  - $($nested.FullPath)" -ForegroundColor Red
                Write-Host "    Recommendation: $($nested.Recommendation)" -ForegroundColor Yellow
                $textLines += "  - $($nested.FullPath)"
                $textLines += "    Recommendation: $($nested.Recommendation)"
                
                # Generate specific Move-Item command
                $fullPath = $nested.FullPath
                $fileName = Split-Path $fullPath -Leaf
                $parentDir = Split-Path (Split-Path $fullPath)  # Go up one level from the nested subfolder
                $textLines += ""
                $textLines += "    FIX: Move-Item `"$fullPath`" `"$parentDir\$fileName`""
            }
            $textLines += ""
            $textLines += "  Alternative: Enable automatic flattener in .csproj:"
            $textLines += "    <EnableViewsFlattener>true</EnableViewsFlattener>"
            $textLines += ""
        } else {
            Write-Host "  No nested Views subfolders detected (good)" -ForegroundColor Green
        }
        
        if ($analysisResults.Warnings.Count -gt 0) {
            Write-Host "WARNINGS:" -ForegroundColor Yellow
            $textLines += "WARNINGS:"
            foreach ($warning in $analysisResults.Warnings) {
                Write-Host "  - $warning" -ForegroundColor Yellow
                $textLines += "  - $warning"
            }
            $textLines += ""
            Write-Host ""
        }
        
        Write-Host "RECOMMENDATIONS:" -ForegroundColor Cyan
        $textLines += "RECOMMENDATIONS:"
        foreach ($rec in $analysisResults.Recommendations) {
            Write-Host "  $rec"
            $textLines += "  $rec"
        }
        $textLines += ""
        Write-Host ""
        
        Write-Host "NEXT STEPS:" -ForegroundColor Cyan
        $textLines += "NEXT STEPS:"
        Write-Host "  1. Open binlog in MSBuild Structured Log Viewer:"
        $textLines += "  1. Open binlog in MSBuild Structured Log Viewer:"
        Write-Host "     https://msbuildlog.com/" -ForegroundColor Gray
        $textLines += "     https://msbuildlog.com/"
        Write-Host ""
        $textLines += ""
        Write-Host "  2. Search for 'XamlCompiler.exe' to find compiler task"
        $textLines += "  2. Search for 'XamlCompiler.exe' to find compiler task"
        Write-Host ""
        $textLines += ""
        Write-Host "  3. If exit code 1 with no error, check:"
        $textLines += "  3. If exit code 1 with no error, check:"
        Write-Host "     - XAML Change Protocol: docs/developer/XAML_CHANGE_PROTOCOL.md"
        $textLines += "     - XAML Change Protocol: docs/developer/XAML_CHANGE_PROTOCOL.md"
        Write-Host "     - Run: .\scripts\xaml-binary-search.ps1"
        $textLines += "     - Run: .\scripts\xaml-binary-search.ps1"
        Write-Host ""
        
        $outputContent = $textLines -join "`n"
    }
}

# Write to file if OutputFile specified
if ($OutputFile -and $OutputFile.Trim() -ne "") {
    # Ensure parent directory exists
    $outputDir = Split-Path $OutputFile -Parent
    if ($outputDir -and -not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }
    
    $outputContent | Set-Content -Path $OutputFile -Encoding UTF8
    Write-Host ""
    Write-Host "Analysis written to: $OutputFile" -ForegroundColor Gray
}

# Return exit code based on findings
if ($nestedViews.Count -gt 0) {
    exit 1
} else {
    exit 0
}
