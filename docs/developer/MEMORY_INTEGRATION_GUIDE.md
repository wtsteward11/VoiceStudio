# Memory Integration Guide

This guide documents VoiceStudio's memory integration system, which provides contextual
memory queries for AI agents using OpenMemory and vector-based retrieval.

## Overview

The memory integration provides:
- **OpenMemory integration** - Semantic memory via MCP or local file fallback
- **Vector memory** - Chroma-based similarity search for code semantics
- **Role-aware filtering** - Context tailored to specific roles
- **Persistent storage** - `openmemory.md` as the canonical local store

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                      Memory System Architecture                      │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Query                  Memory Service                  Results      │
│  ┌─────────┐           ┌──────────────┐               ┌─────────┐   │
│  │ Role    │──────────▶│              │──────────────▶│ Context │   │
│  │ Context │           │  MemoryService│               │ Bundle  │   │
│  └─────────┘           │              │               └─────────┘   │
│                        └──────────────┘                              │
│                               │                                      │
│               ┌───────────────┼───────────────┐                     │
│               ▼               ▼               ▼                     │
│  ┌──────────────────┐ ┌──────────────┐ ┌──────────────────┐        │
│  │ MemorySourceAdapter│ │VectorMemory │ │ OpenMemory MCP   │        │
│  │ (openmemory.md)   │ │(Chroma)      │ │ (openmemory_query)│        │
│  └──────────────────┘ └──────────────┘ └──────────────────┘        │
│         │                    │                   │                  │
│         ▼                    ▼                   ▼                  │
│  ┌──────────────────────────────────────────────────────┐          │
│  │               openmemory.md (Local SSOT)              │          │
│  └──────────────────────────────────────────────────────┘          │
└─────────────────────────────────────────────────────────────────────┘
```

## Components

### MemorySourceAdapter

Located at `tools/context/sources/memory_adapter.py`, this adapter:
- Reads from `openmemory.md` (local file SSOT)
- Optionally queries OpenMemory MCP for semantic search
- Parses sections and filters by role relevance
- Falls back gracefully when MCP is unavailable

### VectorMemoryAdapter

Located at `tools/context/sources/vector_memory_adapter.py`, this adapter:
- Uses Chroma for vector similarity search
- Enables semantic code search (CodeRAG pattern)
- Works offline when `offline=True`

### MemoryService

Located at `tools/context/services/memory_service.py`, this service:
- Combines file-based and vector-based memory
- Provides unified query/store interface
- Maps memory types to `openmemory.md` sections

## Configuration

### Environment Variables

| Variable | Purpose | Default |
|---|---|---|
| `OPENMEMORY_PATH` | Path to openmemory.md | Workspace root |
| `CHROMA_PERSIST_PATH` | Chroma database path | `.chroma/` |
| `MEMORY_MCP_ENABLED` | Enable OpenMemory MCP | `true` |

### Context Sources Config

Memory sources are configured in `tools/context/config/context-sources.json`:

```json
{
  "sources": {
    "memory": {
      "type": "memory",
      "weight": 0.8,
      "budget": 3000,
      "enabled": true
    },
    "vector_memory": {
      "type": "vector",
      "weight": 0.6,
      "budget": 2000,
      "enabled": true
    }
  }
}
```

## Usage Examples

### Query Memory

```python
from tools.context.services.memory_service import MemoryService
from tools.context.core.models import AllocationContext

# Initialize service
service = MemoryService()

# Query with role context
context = AllocationContext(
    task_id="TASK-0042",
    role="ui-engineer",
    phase="Construct",
)

memories = service.query("XAML binding patterns", context)
for mem in memories:
    print(f"[{mem.source}] {mem.content[:100]}...")
```

### Store Memory

```python
from tools.context.services.memory_service import MemoryService

service = MemoryService()

# Store a component memory
service.store(
    content="VoiceBrowserView uses CollectionViewSource for filtering",
    memory_type="component",
    section="Key components",  # Maps to openmemory.md section
    tags=["ui", "voice-browser"],
)
```

### Role-Aware Filtering

```python
from tools.context.sources.memory_adapter import MemorySourceAdapter

adapter = MemorySourceAdapter()

# Fetch memories for UI Engineer
context = AllocationContext(role="ui-engineer")
result = adapter.fetch(context)

# Result contains filtered memories relevant to UI work
for item in result.content.get("memories", []):
    print(f"[{item['source']}] {item['content'][:80]}...")
```

### Vector Search

```python
from tools.context.sources.vector_memory_adapter import VectorMemoryAdapter

adapter = VectorMemoryAdapter(offline=False)

# Semantic code search
context = AllocationContext(
    task_id="TASK-0042",
    query="audio processing pipeline",
)
result = adapter.fetch(context)

for match in result.content.get("matches", []):
    print(f"Score: {match['score']:.2f} - {match['path']}")
```

## OpenMemory MCP Integration

When MCP is available, the memory adapter queries the `openmemory_query` tool:

```python
# MCP configuration (from memory_adapter.py)
MCP_PROVIDERS = {
    "openmemory": {
        "command": "npx",
        "args": ["-y", "openmemory-mcp"],
        "tool_name": "openmemory_query",
    },
    "mem0": {
        "command": "npx",
        "args": ["-y", "@mem0/mcp-server"],
        "tool_name": "search_memory",
    },
}
```

### MCP Fallback Behavior

Per ADR-015, when MCP is unavailable:

1. Log warning once (not every query)
2. Fall back to file-based `openmemory.md`
3. Continue operation without error

```python
# Automatic fallback in MemorySourceAdapter.fetch()
try:
    mcp_memories = self._call_openmemory_mcp(query, max_results)
except MCPUnavailableError:
    if not self._logged_mcp_warning:
        logger.warning("OpenMemory MCP unavailable; using file fallback")
        self._logged_mcp_warning = True
    # Continue with file-based query
```

## openmemory.md Structure

The canonical local store follows this structure:

```markdown
# VoiceStudio — OpenMemory

## Overview
[Project summary and architecture overview]

## Gate status (A–H)
[Current gate completion status]

## Key components and contracts
[Component documentation]

## Governance source of truth
[Links to canonical governance docs]

## Engine notes
[Engine-specific implementation details]

## Context management system
[Context system documentation]

## User Defined Namespaces
[User-defined memory categories]
```

### Section Mapping

Memory types map to sections:

| Memory Type | openmemory.md Section |
|---|---|
| `component` | Key components and contracts |
| `implementation` | Key components and contracts |
| `project_info` | Overview |
| `governance` | Governance source of truth |
| `engine` | Engine notes |
| `context` | Context management system |

## Role-Specific Memory Filtering

Different roles receive filtered memory context:

### UI Engineer (Role 3)
- Key components (UI controls, ViewModels)
- XAML patterns and bindings
- UI-related engine notes

### Core Platform (Role 4)
- Backend services
- Context management system
- Job/artifact storage patterns

### Engine Engineer (Role 5)
- Engine notes (all)
- Quality metrics
- Runtime patterns

### Overseer (Role 0)
- All sections (full context)
- Gate status
- Governance documents

## Integration with Context System

### Memory in Context Bundles

Memory is integrated into the P.A.R.T. context structure:

```python
from tools.context.core.manager import ContextManager

manager = ContextManager.from_config()
bundle = manager.allocate(AllocationContext(
    task_id="TASK-0042",
    role="core-platform",
    level=ContextLevel.MID,
))

# Memory is included in the bundle
print(bundle.to_part_markdown())
# P.A.R.T. structure includes memory under Resources
```

### Progressive Disclosure

Memory loading respects context levels:

| Level | Memory Included |
|---|---|
| HIGH | Essential memories only (current task) |
| MID | + Role-relevant sections |
| LOW | Full memory (all sections) |

## Storing New Memories

### Via MemoryService

```python
from tools.context.services.memory_service import MemoryService

service = MemoryService()

# Store component documentation
service.store(
    content="AudioArtifactRegistry uses atomic writes for durability",
    memory_type="component",
    section="Key components",
    metadata={
        "path": "backend/services/AudioArtifactRegistry.py",
        "category": "storage",
    },
)
```

### Via MCP (When Available)

```python
# Using OpenMemory MCP add_memory tool
from tools.context.sources.memory_adapter import store_memory_mcp

store_memory_mcp(
    content="New engine integration pattern discovered",
    memory_type="implementation",
    project_id="VoiceStudio",
    tags=["engine", "integration"],
)
```

### Direct File Edit

For bulk updates, edit `openmemory.md` directly following the section structure.

## Best Practices

### 1. Keep openmemory.md Current

Update `openmemory.md` when:
- New components are added
- Architecture changes
- Gates are completed
- Important patterns are discovered

### 2. Use Specific Queries

```python
# Good - specific query
memories = service.query("AudioArtifactRegistry atomic write pattern")

# Bad - too broad
memories = service.query("storage")
```

### 3. Include Context

```python
# Good - includes role context
context = AllocationContext(role="core-platform", task_id="TASK-0042")
memories = service.query("job persistence", context)

# Less effective - no role filtering
memories = service.query("job persistence")
```

### 4. Store with Metadata

```python
# Good - rich metadata
service.store(
    content="Pattern description...",
    memory_type="implementation",
    metadata={
        "path": "path/to/file.py",
        "related_tasks": ["TASK-0040", "TASK-0041"],
        "category": "async-patterns",
    },
)
```

### 5. Handle MCP Gracefully

```python
# Good - graceful fallback
try:
    result = await mcp_query(query)
except MCPUnavailableError:
    result = file_fallback(query)
    # Don't fail the operation
```

## CLI Tools

### Query Memory

```bash
# Query with role context
python -c "
from tools.context.services.memory_service import MemoryService
from tools.context.core.models import AllocationContext
svc = MemoryService()
ctx = AllocationContext(role='core-platform')
for m in svc.query('job state', ctx)[:3]:
    print(m.content[:100])
"
```

### Check Memory Adapter Status

```bash
python -c "
from tools.context.sources.memory_adapter import MemorySourceAdapter
adapter = MemorySourceAdapter()
print(f'MCP enabled: {adapter._mcp_enabled}')
print(f'File path: {adapter._openmemory_path}')
"
```

## Setup

### Quick Setup

Run the setup script to validate and install dependencies:

**Windows (PowerShell):**
```powershell
.\scripts\setup_openmemory.ps1
```

**Linux/macOS (Bash):**
```bash
./scripts/setup_openmemory.sh
```

### Manual Setup

1. **Install Python mcp package:**
   ```bash
   pip install mcp
   ```

2. **Ensure Node.js is available (for npx):**
   ```bash
   node --version
   npx --version
   ```

3. **Verify MCP is enabled in config:**
   
   Check `tools/context/config/context-sources.json`:
   ```json
   {
     "memory": {
       "mcp_enabled": true,
       ...
     }
   }
   ```

### Running Tests

```bash
# Unit tests for memory adapter
pytest tests/unit/tools_tests/context/test_memory_adapter.py -v

# Integration tests for MCP
pytest tests/integration/tools/test_mcp_integration.py -v
```

## Troubleshooting

### MCP Not Available

**Symptom**: Warning "OpenMemory MCP unavailable"

**Resolution**:
1. Run `scripts/setup/setup_openmemory.ps1` to verify dependencies
2. Check if MCP server is running
3. Verify `cursor.mcp.json` configuration
4. Set `memory.mcp_enabled: false` in `context-sources.json` to disable MCP queries

### Empty Results

**Symptom**: Query returns no results

**Resolution**:
1. Check `openmemory.md` exists in workspace root
2. Verify query matches content in relevant sections
3. Check role filtering isn't excluding results

### Vector Search Slow

**Symptom**: Vector queries take too long

**Resolution**:
1. Enable offline mode: `VectorMemoryAdapter(offline=True)`
2. Check Chroma database size
3. Limit result count in queries

## Related Documentation

- [OpenMemory Rules](../../.cursor/rules/openmemory.mdc)
- [Context Strategy](../../.cursor/rules/workflows/context-strategy.mdc)
- [Memory Source Adapter](../../tools/context/sources/memory_adapter.py)
- [Vector Memory Adapter](../../tools/context/sources/vector_memory_adapter.py)
- [Memory Service](../../tools/context/services/memory_service.py)
- [MCP Optimization Guide](MCP_OPTIMIZATION_GUIDE.md)
