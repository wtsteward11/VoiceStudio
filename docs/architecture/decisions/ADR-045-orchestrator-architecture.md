# ADR-045: Intelligent Engine Orchestrator Architecture

**Status:** Accepted
**Date:** 2026-02-25
**Decision Makers:** Tyler (Lead), AI Architect

## Context

VoiceStudio has a mature engine protocol layer (73 engines), quality metrics system
(MOS, SNR, naturalness, similarity, artifact detection), and enhanced job queue
with priority scheduling. However, engine selection is manual or based on simple
fallback chains. There is no autonomous loop that:

1. Selects engines based on strategy and quality targets
2. Evaluates output quality against configurable thresholds
3. Adaptively retries with parameter mutation when quality is below threshold
4. Falls back to alternative engines automatically
5. Applies post-processing enhancement when needed
6. Streams execution events to the UI in real-time

## Options Considered

### Option A: Extend existing voice.py with orchestration logic
- Pros: No new modules; single endpoint
- Cons: voice.py is already 4350 lines; coupling increases; hard to test in isolation

### Option B: New OrchestrationService composing existing primitives (Selected)
- Pros: Clean separation; composes EngineRouter + QualityMetrics + JobQueue;
  independently testable; new API surface doesn't break existing endpoints
- Cons: New module to maintain; slight duplication of engine selection logic

### Option C: External orchestration tool
- Pros: Language-agnostic; could use existing workflow engines
- Cons: Breaks local-first principle; adds external dependency; latency

## Decision

**Option B** — Create `backend/orchestrator/` module with:
- `schemas.py` — Pydantic models (ProductionChain, OrchestrationRequest/Response, etc.)
- `service.py` — OrchestrationService with state machine and quality-driven retry loop
- `presets.py` — Strategy preset management (6 built-in: cinematic, audiobook, podcast, broadcast, game_character, conversational)
- `scheduler.py` — GPU-aware job scheduler with priority promotion and concurrency limits
- `gpu_tracker.py` — Periodic GPU utilization tracker
- `events.py` — Typed event emitter for WebSocket streaming

API routes at `/api/orchestrator/*` (9 endpoints including WebSocket).
3 new WinUI panels: Orchestration, Render Queue, Strategy Presets.

## Consequences

### Positive
- Engines compose into autonomous quality-optimizing pipelines
- Users get strategy presets for common production scenarios
- Real-time execution visibility via WebSocket events
- GPU-aware scheduling prevents OOM during concurrent synthesis
- Foundation for future AI Copilot and MCP expansion

### Negative
- 48 new Python tests to maintain
- 3 new XAML panels increase build scope
- Quality metrics accuracy limits adaptive retry effectiveness (MOS is heuristic)

### Risks
- GPU tracker polling adds background thread (mitigated: 5s interval, daemon thread)
- WebSocket stability for long jobs (mitigated: existing reconnect + polling fallback)

## Proof

- C# build: 0 errors, 419 warnings
- Python tests: 48/48 PASS
- Verification harness: ALL PASS
- 6 strategy presets load successfully
- All 3 panels registered and rendering
