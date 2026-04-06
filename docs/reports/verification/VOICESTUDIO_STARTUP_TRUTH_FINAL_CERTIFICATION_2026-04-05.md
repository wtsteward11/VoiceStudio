# Startup truth — final certification (2026-04-05)

**Purpose:** Satisfy **Startup Truth Final Certification** plan: WinUI boundary stages re-run after GAP-045 cross-consumer work; capture harness artifact folders; **no active startup blocker** when stages are green.

## Decision

- **Startup blocker:** **None** (harness UI stages PASS on this matrix run).
- **Stability note:** Prior certification artifacts from reload/rehydrate closure remain valid: [VOICESTUDIO_UI_STARTUP_BOUNDARY_2026-04-05.md](VOICESTUDIO_UI_STARTUP_BOUNDARY_2026-04-05.md). This run adds a **post-slice** confirmation on the same stage names.

## Evidence bundle (harness)

Per-run folders (SkipBuild OnlyStage):

| Run | Stage | Artifact folder |
|-----|--------|-------------------|
| 1 | UI Self-Test | `artifacts/verify/20260405_080443/` |
| 2 | Icon-Launch Smoke | `artifacts/verify/20260405_080450/` |
| 3 | Failure-Path Smoke | `artifacts/verify/20260405_080459/` |
| 4 | Runtime-Missing Failure Smoke | `artifacts/verify/20260405_080517/` |

Full Quick gate (includes build + tests + critical gates): `artifacts/verify/20260405_075900/`.

## Operator cold-launch checklist (manual)

For **3–5** true cold launches (exit app between runs), capture each time:

1. `%LOCALAPPDATA%\VoiceStudio\crashes\startup_decision.json` (includes `timeout_seconds` per `BackendProcessManager`)
2. App startup diagnostics log (if enabled)
3. Whether UI reaches **BackendReady** / usable shell (per product UX)

Classify failures as: timeout budget, authority drift, handshake/gating, WebSocket path, artifact contamination — see [VOICESTUDIO_UI_STARTUP_BOUNDARY_2026-04-05.md](VOICESTUDIO_UI_STARTUP_BOUNDARY_2026-04-05.md).

## Related

- [VOICESTUDIO_GAP045_TRANSCRIPT_CROSS_CONSUMER_COHERENCE_CLOSURE_2026-04-05.md](VOICESTUDIO_GAP045_TRANSCRIPT_CROSS_CONSUMER_COHERENCE_CLOSURE_2026-04-05.md)
