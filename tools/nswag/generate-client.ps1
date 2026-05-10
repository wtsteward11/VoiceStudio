# VoiceStudio C# Client Generation from OpenAPI
# Uses NSwag to generate typed HTTP client from backend OpenAPI schema

param(
    [switch]$ValidateOnly,      # Only validate, don't regenerate
    [switch]$Force,              # Regenerate even if up-to-date
    [string]$OpenApiPath = "",   # Override OpenAPI path
    [switch]$Verbose             # Verbose output
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $PSScriptRoot
$projectRoot = Split-Path -Parent $scriptDir
$configFile = Join-Path $PSScriptRoot "nswag.json"

# Paths
if ([string]::IsNullOrEmpty($OpenApiPath)) {
    $OpenApiPath = Join-Path $projectRoot "docs\api\openapi.json"
}
$outputPath = Join-Path $projectRoot "src\VoiceStudio.App\Services\Generated\BackendClient.g.cs"
$outputDir = Split-Path -Parent $outputPath

Write-Host "======================================" -ForegroundColor Cyan
Write-Host " VoiceStudio C# Client Generation" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Validate OpenAPI schema exists
if (-not (Test-Path $OpenApiPath)) {
    Write-Host "[ERROR] OpenAPI schema not found: $OpenApiPath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Generate the schema first:" -ForegroundColor Yellow
    Write-Host "  python scripts/export_openapi_schema.py" -ForegroundColor Gray
    Write-Host "  OR" -ForegroundColor Gray
    Write-Host "  Start backend and export from /openapi.json" -ForegroundColor Gray
    exit 1
}

Write-Host "[OK] OpenAPI schema found: $OpenApiPath" -ForegroundColor Green

# Check if NSwag is installed
function Test-NSwag {
    try {
        $version = nswag version 2>&1
        if ($LASTEXITCODE -eq 0) {
            return $version
        }
    }
    catch {
        # Not installed
    }
    return $null
}

$nswagVersion = Test-NSwag
if (-not $nswagVersion) {
    Write-Host ""
    Write-Host "[WARN] NSwag not installed. Installing..." -ForegroundColor Yellow
    
    try {
        dotnet tool install -g NSwag.ConsoleCore
        if ($LASTEXITCODE -ne 0) {
            throw "Installation failed"
        }
        $nswagVersion = Test-NSwag
        Write-Host "[OK] NSwag installed: $nswagVersion" -ForegroundColor Green
    }
    catch {
        Write-Host "[ERROR] Failed to install NSwag" -ForegroundColor Red
        Write-Host "  Install manually: dotnet tool install -g NSwag.ConsoleCore" -ForegroundColor Gray
        exit 1
    }
}
else {
    Write-Host "[OK] NSwag version: $nswagVersion" -ForegroundColor Green
}

# Check if regeneration is needed
if (-not $Force -and (Test-Path $outputPath)) {
    $schemaTime = (Get-Item $OpenApiPath).LastWriteTime
    $clientTime = (Get-Item $outputPath).LastWriteTime
    
    if ($clientTime -gt $schemaTime) {
        Write-Host ""
        Write-Host "[INFO] Client is up-to-date (generated after schema)" -ForegroundColor Cyan
        Write-Host "  Schema: $schemaTime" -ForegroundColor Gray
        Write-Host "  Client: $clientTime" -ForegroundColor Gray
        
        if (-not $ValidateOnly) {
            Write-Host ""
            Write-Host "Use -Force to regenerate anyway" -ForegroundColor Gray
        }
        exit 0
    }
}

if ($ValidateOnly) {
    Write-Host ""
    Write-Host "[WARN] Client needs regeneration" -ForegroundColor Yellow
    Write-Host "  Run without -ValidateOnly to regenerate" -ForegroundColor Gray
    exit 1
}

# Ensure output directory exists
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    Write-Host "[OK] Created output directory: $outputDir" -ForegroundColor Green
}

# Generate client
Write-Host ""
Write-Host "Generating C# client..." -ForegroundColor Cyan

# Create temp config with resolved paths
$configContent = Get-Content $configFile -Raw
$configContent = $configContent.Replace('$(OpenApiPath)', $OpenApiPath.Replace('\', '/'))
$configContent = $configContent.Replace('$(OutputPath)', $outputPath.Replace('\', '/'))

$tempConfig = Join-Path $env:TEMP "nswag-voicestudio-$(Get-Random).json"
$configContent | Out-File -FilePath $tempConfig -Encoding UTF8

try {
    if ($Verbose) {
        Write-Host "  Config: $tempConfig" -ForegroundColor Gray
        Write-Host "  Command: nswag run $tempConfig" -ForegroundColor Gray
    }
    
    $output = nswag run $tempConfig 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] NSwag generation failed" -ForegroundColor Red
        Write-Host $output -ForegroundColor Red
        exit 1
    }
    
    if ($Verbose) {
        Write-Host $output -ForegroundColor Gray
    }
    
    # Verify output
    if (Test-Path $outputPath) {
        # Post-processing: Fix NSwag pragma bug
        # NSwag incorrectly generates "#pragma restore disable" instead of "#pragma warning restore"
        $content = Get-Content $outputPath -Raw
        # Strip UTF-8 BOM if present so committed client matches CI (Validate API Contracts drift gate).
        if ($content.Length -ge 1 -and [int][char]$content[0] -eq 0xFEFF) {
            $content = $content.Substring(1)
        }
        $originalContent = $content
        $content = $content -replace '#pragma restore disable', '#pragma warning restore'
        $utf8NoBom = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($outputPath, $content, $utf8NoBom)
        if ($content -ne $originalContent) {
            Write-Host "[FIX] Corrected pragma directive in generated file" -ForegroundColor Yellow
        }
        
        $size = (Get-Item $outputPath).Length
        $lines = (Get-Content $outputPath).Count
        
        Write-Host ""
        Write-Host "[OK] C# client generated successfully!" -ForegroundColor Green
        Write-Host "  Output: $outputPath" -ForegroundColor Gray
        Write-Host "  Size: $([math]::Round($size / 1024, 1)) KB" -ForegroundColor Gray
        Write-Host "  Lines: $lines" -ForegroundColor Gray
    }
    else {
        Write-Host "[ERROR] Generated file not found" -ForegroundColor Red
        exit 1
    }
}
finally {
    if (Test-Path $tempConfig) {
        Remove-Item $tempConfig -Force
    }
}

# Summary
Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host " Generation Complete" -ForegroundColor Cyan  
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Build solution to verify no compile errors" -ForegroundColor Gray
Write-Host "  2. Run contract tests: dotnet test --filter Category=Contract" -ForegroundColor Gray
Write-Host "  3. Commit generated file if changes are intentional" -ForegroundColor Gray

exit 0
