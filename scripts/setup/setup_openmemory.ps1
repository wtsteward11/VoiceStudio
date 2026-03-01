<#
.SYNOPSIS
    Sets up OpenMemory MCP dependencies for VoiceStudio.

.DESCRIPTION
    This script validates and installs dependencies required for OpenMemory MCP integration:
    - Python 'mcp' package
    - Node.js and npx availability
    - OpenMemory MCP server availability

.PARAMETER SkipNpxCheck
    Skip the npx availability check.

.PARAMETER Force
    Force reinstall of Python dependencies even if already installed.

.PARAMETER Verbose
    Enable verbose output.

.EXAMPLE
    .\setup_openmemory.ps1
    
.EXAMPLE
    .\setup_openmemory.ps1 -Force -Verbose
#>

param(
    [switch]$SkipNpxCheck,
    [switch]$Force,
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

function Write-Status {
    param([string]$Message, [string]$Status = "INFO")
    $color = switch ($Status) {
        "OK"    { "Green" }
        "WARN"  { "Yellow" }
        "ERROR" { "Red" }
        "INFO"  { "Cyan" }
        default { "White" }
    }
    Write-Host "[$Status] " -ForegroundColor $color -NoNewline
    Write-Host $Message
}

function Test-Command {
    param([string]$Command)
    try {
        $null = Get-Command $Command -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  OpenMemory MCP Setup for VoiceStudio" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check Python
Write-Status "Checking Python installation..."
if (Test-Command "python") {
    $pythonVersion = python --version 2>&1
    Write-Status "Python found: $pythonVersion" "OK"
} else {
    Write-Status "Python not found. Please install Python 3.10+ from https://python.org" "ERROR"
    exit 1
}

# Step 2: Check/Install mcp Python package
Write-Status "Checking 'mcp' Python package..."
$mcpInstalled = $false
try {
    $result = python -c "import mcp; print(mcp.__version__)" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Status "mcp package found: $result" "OK"
        $mcpInstalled = $true
    }
}
catch {
    $mcpInstalled = $false
}

if (-not $mcpInstalled -or $Force) {
    Write-Status "Installing mcp Python package..." "INFO"
    try {
        pip install mcp --quiet
        if ($LASTEXITCODE -eq 0) {
            Write-Status "mcp package installed successfully" "OK"
        } else {
            Write-Status "Failed to install mcp package" "ERROR"
            Write-Status "Try: pip install mcp" "INFO"
            exit 1
        }
    }
    catch {
        Write-Status "Error installing mcp: $_" "ERROR"
        exit 1
    }
}

# Step 3: Check Node.js and npx
if (-not $SkipNpxCheck) {
    Write-Status "Checking Node.js and npx..."
    
    if (Test-Command "node") {
        $nodeVersion = node --version 2>&1
        Write-Status "Node.js found: $nodeVersion" "OK"
    } else {
        Write-Status "Node.js not found. Required for MCP servers." "WARN"
        Write-Status "Install from https://nodejs.org or via: winget install OpenJS.NodeJS.LTS" "INFO"
    }
    
    if (Test-Command "npx") {
        $npxVersion = npx --version 2>&1
        Write-Status "npx found: $npxVersion" "OK"
    } else {
        Write-Status "npx not found. It comes with Node.js." "WARN"
    }
}

# Step 4: Verify OpenMemory MCP server can be discovered
Write-Status "Verifying OpenMemory MCP server availability..."
try {
    $result = npx -y openmemory-mcp --help 2>&1
    if ($LASTEXITCODE -eq 0 -or $result -match "openmemory") {
        Write-Status "OpenMemory MCP server available" "OK"
    } else {
        Write-Status "OpenMemory MCP server may not be available" "WARN"
        Write-Status "The system will fall back to openmemory.md file" "INFO"
    }
}
catch {
    Write-Status "Could not verify OpenMemory MCP. Fallback will be used." "WARN"
}

# Step 5: Check configuration
Write-Status "Checking VoiceStudio configuration..."
$configPath = Join-Path $PSScriptRoot "..\tools\context\config\context-sources.json"
if (Test-Path $configPath) {
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    if ($config.memory.mcp_enabled) {
        Write-Status "MCP is enabled in configuration" "OK"
    } else {
        Write-Status "MCP is disabled in configuration. Enable in: $configPath" "WARN"
    }
} else {
    Write-Status "Configuration file not found: $configPath" "WARN"
}

# Step 6: Check openmemory.md fallback file
Write-Status "Checking openmemory.md fallback file..."
$openmemoryPath = Join-Path $PSScriptRoot "..\openmemory.md"
if (Test-Path $openmemoryPath) {
    $size = (Get-Item $openmemoryPath).Length
    Write-Status "openmemory.md found ($size bytes)" "OK"
} else {
    Write-Status "openmemory.md not found. Consider creating one for offline fallback." "INFO"
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Status "OpenMemory MCP integration is ready." "OK"
Write-Status "Run tests: pytest tests/unit/tools_tests/context/test_memory_adapter.py" "INFO"
Write-Host ""
