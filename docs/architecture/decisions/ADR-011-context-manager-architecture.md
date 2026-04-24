# ADR-011: Context Manager Architecture

## Status

**Accepted** (2026-02-04)

## Context

VoiceStudio's AI agents require context from multiple sources to make informed decisions:
- Session state (`.cursor/STATE.md`)
- Task briefs (`docs/tasks/TASK-*.md`)
- Project rules (`.cursor/rules/**/*.mdc`)
- Quality ledger (`docs/archive/Recovery_Plan/QUALITY_LEDGER.md`)
- Git history and diffs
- OpenMemory (persistent facts across sessions)
- Audit logs (`.buildlogs/audit/`)

A Context Manager is needed to:
1. Aggregate context from diverse sources
2. Allocate context budget based on role
3. Compose context packages for agents
4. Support extensibility for new sources

## Options Considered

### Option 1: Monolithic Module
Single module handles all context retrieval and composition.

**Pros:**
- Simple implementation
- No abstraction overhead

**Cons:**
- Hard to extend with new sources
- Testing requires mocking entire module
- Tight coupling to source formats

### Option 2: Adapter-Based (Selected)
Pluggable source adapters with a central registry.

**Pros:**
- Easy to add new sources
- Each adapter independently testable
- Clear separation of concerns
- Supports role-based allocation

**Cons:**
- More abstraction layers
- Need adapter interface definition
- Registration overhead

### Option 3: Pipeline-Based
Sequential processing stages (fetch → filter → compose).

**Pros:**
- Clear processing flow
- Easy to add transformations

**Cons:**
- Rigid ordering
- Hard to handle async sources
- Less flexible for role-specific needs

## Decision

**Option 2: Adapter-Based Architecture** with the following design:

```
┌─────────────────────────────────────────────────────────┐
│                   Context Manager                        │
│  ┌─────────────────────────────────────────────────┐    │
│  │              Context Allocator                   │    │
│  │  - Role profiles (budget, weights)              │    │
│  │  - Budget enforcement                           │    │
│  └─────────────────────────────────────────────────┘    │
│                          │                               │
│  ┌─────────────────────────────────────────────────┐    │
│  │            Source Adapter Registry              │    │
│  └─────────────────────────────────────────────────┘    │
│       │         │         │         │         │         │
│  ┌────▼───┐ ┌───▼────┐ ┌──▼───┐ ┌───▼───┐ ┌───▼────┐   │
│  │ State  │ │  Task  │ │Rules │ │Memory │ │ Audit  │   │
│  │Adapter │ │Adapter │ │Adapt │ │Adapter│ │Adapter │   │
│  └────────┘ └────────┘ └──────┘ └───────┘ └────────┘   │
│       │         │         │         │         │         │
└───────┼─────────┼─────────┼─────────┼─────────┼─────────┘
        │         │         │         │         │
   STATE.md   TASK-*.md  *.mdc   OpenMemory  audit/*.json
```

### Core Components

#### 1. Source Adapter Protocol

```python
# tools/context/core/protocols.py
class SourceAdapter(Protocol):
    """Protocol for context source adapters."""
    
    @property
    def source_id(self) -> str:
        """Unique identifier for this source."""
        ...
    
    async def fetch(self, config: SourceConfig) -> ContextChunk:
        """Fetch context from this source."""
        ...
    
    def estimate_size(self) -> int:
        """Estimate token count for budget allocation."""
        ...
```

#### 2. Context Allocator

```python
# tools/context/core/allocator.py
class ContextAllocator:
    """Allocates context budget across sources based on role."""
    
    def allocate(self, role: str, sources: list[SourceAdapter]) -> ContextBundle:
        """Allocate budget and compose context package."""
        profile = self.role_profiles.get(role)
        budget = profile.total_budget
        weights = profile.source_weights
        
        # Weighted allocation
        for source in sources:
            allocation = budget * weights.get(source.source_id, 0.1)
            # Fetch and add to bundle
```

#### 3. Role Profiles

```json
// tools/context/config/roles/overseer.json
{
  "role_id": "overseer",
  "total_budget": 50000,
  "source_weights": {
    "state": 0.25,
    "task": 0.20,
    "rules": 0.15,
    "ledger": 0.20,
    "memory": 0.10,
    "audit": 0.10
  }
}
```

### Implemented Adapters

| Adapter | Source | Path | Status |
|---------|--------|------|--------|
| StateAdapter | SESSION STATE | `.cursor/STATE.md` | ✅ |
| TaskAdapter | TASK BRIEFS | `docs/tasks/TASK-*.md` | ✅ |
| RulesAdapter | PROJECT RULES | `.cursor/rules/**/*.mdc` | ✅ |
| LedgerAdapter | QUALITY LEDGER | `docs/archive/Recovery_Plan/QUALITY_LEDGER.md` | ✅ |
| MemoryAdapter | OPENMEMORY | OpenMemory MCP | ✅ |
| GitAdapter | GIT HISTORY | git diff, git log | ✅ |
| IssuesAdapter | ISSUE SYSTEM | `tools/overseer/issues/` | ✅ |
| AuditAdapter | AUDIT LOGS | `.buildlogs/audit/` | ✅ |

### Extension Points

Adding a new source:

```python
# 1. Create adapter in tools/context/sources/
class NewSourceAdapter:
    source_id = "new_source"
    
    async def fetch(self, config: SourceConfig) -> ContextChunk:
        # Implementation
        
    def estimate_size(self) -> int:
        # Estimate tokens

# 2. Register in tools/context/core/registry.py
ADAPTERS = {
    "new_source": NewSourceAdapter,
    # ...
}

# 3. Add weight to role profiles
# tools/context/config/roles/*.json
```

## Implementation Status

| Component | Status | Evidence |
|-----------|--------|----------|
| Core protocols | ✅ Complete | `tools/context/core/protocols.py` |
| Allocator | ✅ Complete | `tools/context/core/allocator.py` |
| Registry | ✅ Complete | `tools/context/core/registry.py` |
| Role profiles | ✅ Complete | `tools/context/config/roles/*.json` |
| Source adapters | ✅ Complete | `tools/context/sources/*.py` |
| Verification | ✅ Complete | `scripts/verify_context_manager.py` |

## Consequences

### Positive
- Easy to add new context sources
- Role-based allocation respects token budgets
- Each adapter independently testable
- Clear separation of source-specific logic
- Supports offline-first operation

### Negative
- More abstraction than monolithic approach
- Adapter registration overhead
- Role profiles need maintenance
- Budget estimation is approximate

### Neutral
- Token counting is heuristic-based
- Memory adapter requires OpenMemory MCP
- Some adapters may be disabled in config

## References

- `tools/context/` - Context manager implementation
- `scripts/verify_context_manager.py` - Verification script
- `scripts/context_manager_health.py` - Health monitoring
- `.cursor/rules/workflows/context-strategy.mdc` - Usage guidelines
- ADR-015: Architecture Integration Contract
