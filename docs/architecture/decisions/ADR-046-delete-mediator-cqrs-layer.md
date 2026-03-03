# ADR-046: Delete Mediator/CQRS Layer (B-DELETE)

**Status:** Accepted
**Date:** 2026-03-03
**Decision Makers:** VoiceStudio Architecture Team
**Related:** VOICESTUDIO_COMPLETION_ROADMAP_V2.md Phase B

## Context

The `backend/application/` directory contains a CQRS-style mediator layer:

- `CommandDispatcher`, `QueryDispatcher` in `handlers/dispatcher.py`
- Command/query dataclasses in `commands/` and `queries/`
- Zero concrete handler implementations
- Zero routes call `dispatch_command()` or `dispatch_query()`
- Zero handlers registered at startup

This layer has been unused for 2+ months. Routes call services directly (the actual pattern in use). Gap 2 in the Completion Roadmap v2.0 requires a hard decision: wire it fully or delete it. Half-wiring is the worst outcome.

## Options

### Option A: B-WIRE

Implement handlers for every command/query, refactor all routes to dispatch through the mediator, add DI registration. Massive effort for zero user value. Would introduce indirection and complexity without benefit.

### Option B: B-DELETE

Remove the dead `backend/application/` directory. Routes continue calling services directly (proven pattern).

## Decision

**B-DELETE.** Remove the mediator/CQRS layer.

## Evidence

| Metric | Value |
|--------|-------|
| Files in `backend/application/` | 11 |
| Handlers registered at startup | 0 |
| Routes calling `dispatch_command` or `dispatch_query` | 0 |
| Concrete `CommandHandler` / `QueryHandler` implementations | 0 |
| External references (outside `backend/application/`) | 2 (validation scripts for layer classification only) |

## Consequences

- Routes continue calling services directly (unchanged behavior)
- ~500 lines of dead code removed
- No new indirection or complexity
- Validation scripts must be updated to remove `backend.application` from layer classification
- Single source of truth for request handling: route → service (no mediator in between)

## References

- `docs/governance/VOICESTUDIO_COMPLETION_ROADMAP_V2.md` — Gap 2, Phase B
- `backend/application/` — deleted by this decision
