#!/bin/bash
#
# Sets up OpenMemory MCP dependencies for VoiceStudio.
#
# This script validates and installs dependencies required for OpenMemory MCP integration:
# - Python 'mcp' package
# - Node.js and npx availability
# - OpenMemory MCP server availability
#
# Usage:
#   ./setup_openmemory.sh [--skip-npx] [--force] [--verbose]
#

set -e

# Parse arguments
SKIP_NPX=false
FORCE=false
VERBOSE=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --skip-npx)
            SKIP_NPX=true
            shift
            ;;
        --force)
            FORCE=true
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Status functions
status_ok() {
    echo -e "${GREEN}[OK]${NC} $1"
}

status_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

status_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

status_info() {
    echo -e "${CYAN}[INFO]${NC} $1"
}

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

echo ""
echo -e "${CYAN}========================================"
echo "  OpenMemory MCP Setup for VoiceStudio"
echo "========================================${NC}"
echo ""

# Step 1: Check Python
status_info "Checking Python installation..."
if command -v python3 &> /dev/null; then
    PYTHON_CMD="python3"
elif command -v python &> /dev/null; then
    PYTHON_CMD="python"
else
    status_error "Python not found. Please install Python 3.10+"
    exit 1
fi

PYTHON_VERSION=$($PYTHON_CMD --version 2>&1)
status_ok "Python found: $PYTHON_VERSION"

# Step 2: Check/Install mcp Python package
status_info "Checking 'mcp' Python package..."
MCP_INSTALLED=false

if $PYTHON_CMD -c "import mcp; print(mcp.__version__)" 2>/dev/null; then
    MCP_VERSION=$($PYTHON_CMD -c "import mcp; print(mcp.__version__)" 2>&1)
    status_ok "mcp package found: $MCP_VERSION"
    MCP_INSTALLED=true
fi

if [ "$MCP_INSTALLED" = false ] || [ "$FORCE" = true ]; then
    status_info "Installing mcp Python package..."
    if pip install mcp --quiet 2>/dev/null || pip3 install mcp --quiet 2>/dev/null; then
        status_ok "mcp package installed successfully"
    else
        status_error "Failed to install mcp package"
        status_info "Try: pip install mcp"
        exit 1
    fi
fi

# Step 3: Check Node.js and npx
if [ "$SKIP_NPX" = false ]; then
    status_info "Checking Node.js and npx..."
    
    if command -v node &> /dev/null; then
        NODE_VERSION=$(node --version 2>&1)
        status_ok "Node.js found: $NODE_VERSION"
    else
        status_warn "Node.js not found. Required for MCP servers."
        status_info "Install from https://nodejs.org or via package manager"
    fi
    
    if command -v npx &> /dev/null; then
        NPX_VERSION=$(npx --version 2>&1)
        status_ok "npx found: $NPX_VERSION"
    else
        status_warn "npx not found. It comes with Node.js."
    fi
fi

# Step 4: Verify OpenMemory MCP server can be discovered
status_info "Verifying OpenMemory MCP server availability..."
if command -v npx &> /dev/null; then
    if npx -y openmemory-mcp --help 2>&1 | grep -q "openmemory\|Usage\|Options"; then
        status_ok "OpenMemory MCP server available"
    else
        status_warn "OpenMemory MCP server may not be available"
        status_info "The system will fall back to openmemory.md file"
    fi
else
    status_warn "Could not verify OpenMemory MCP. Fallback will be used."
fi

# Step 5: Check configuration
status_info "Checking VoiceStudio configuration..."
CONFIG_PATH="$PROJECT_ROOT/tools/context/config/context-sources.json"
if [ -f "$CONFIG_PATH" ]; then
    if grep -q '"mcp_enabled": true' "$CONFIG_PATH"; then
        status_ok "MCP is enabled in configuration"
    else
        status_warn "MCP is disabled in configuration. Enable in: $CONFIG_PATH"
    fi
else
    status_warn "Configuration file not found: $CONFIG_PATH"
fi

# Step 6: Check openmemory.md fallback file
status_info "Checking openmemory.md fallback file..."
OPENMEMORY_PATH="$PROJECT_ROOT/openmemory.md"
if [ -f "$OPENMEMORY_PATH" ]; then
    SIZE=$(wc -c < "$OPENMEMORY_PATH")
    status_ok "openmemory.md found ($SIZE bytes)"
else
    status_info "openmemory.md not found. Consider creating one for offline fallback."
fi

echo ""
echo -e "${GREEN}========================================"
echo "  Setup Complete!"
echo "========================================${NC}"
echo ""
status_ok "OpenMemory MCP integration is ready."
status_info "Run tests: pytest tests/unit/tools_tests/context/test_memory_adapter.py"
echo ""
