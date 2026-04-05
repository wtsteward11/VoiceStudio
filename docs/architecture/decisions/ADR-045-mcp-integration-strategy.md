# ADR-045: MCP Integration Strategy

**Status:** Proposed
**Date:** 2026-02-21
**Decision Makers:** Arch Review Task 1.6
**Phase:** 1 - Stabilization

## Context

VoiceStudio architecture diagrams and documentation have referenced "MCP integration" as a capability. Per Principal Architect Review Finding 4.5, the MCP bridge is a **stub masquerading as a feature**:

- `backend/mcp_bridge/` contains only `pdf_unlocker_client.py` and README
- The MCP dashboard route (`mcp_dashboard.py`) was removed in Task 1.4 (archived)
- No full MCP server orchestration, design token sync, or AI engine routing exists

## Decision

1. **Document actual state**: MCP integration is a **proof-of-concept** (PDF unlocker client only). Update all docs to reflect this.

2. **Planned capabilities** (future):
   - Design token sync (Figma, Magic UI, Flux UI, Shadcn)
   - AI model / TTS engine routing via MCP
   - Engine discovery via MCP
   - Authentication model for MCP servers

3. **Timeline**: No committed timeline. Track in FUTURE_WORK.md (MCP-1 through MCP-4).

4. **Authentication**: When implemented, MCP servers will use least-privilege tokens; no shared secrets in code (see `api-key-management.mdc`).

## Rationale

- Prevents overclaiming capabilities
- Aligns documentation with implementation
- Provides clear roadmap for future work

## Consequences

### Positive

- Honest representation of current capabilities
- Clear POC vs. production distinction
- ADR provides single reference for MCP strategy

### Negative

- May disappoint users expecting full MCP support
- PDF unlocker remains the only MCP-style integration

### Integration Points

- `backend/mcp_bridge/README.md` — POC status banner
- `docs/governance/FUTURE_WORK.md` — MCP roadmap
- `README.md` — "MCP integration planned (proof-of-concept available)"
