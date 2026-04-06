# Cold-launch / startup stability — five-run evidence table (2026-04-05)

**Purpose:** Satisfy **Startup Truth Certification** plan Phase 1–2: record **five** distinct harness sessions with artifact pointers and an explicit stability decision. Complements operator checklist in [VOICESTUDIO_STARTUP_TRUTH_FINAL_CERTIFICATION_2026-04-05.md](VOICESTUDIO_STARTUP_TRUTH_FINAL_CERTIFICATION_2026-04-05.md).

## Stability decision

- **Startup blocker:** **None** (all listed sessions **PASS**).
- **Scope:** WinUI harness stages (build where applicable + SkipBuild OnlyStage smokes). **True** manual cold launches (exit → relaunch) remain on the checklist in the final certification doc; capture `%LOCALAPPDATA%\VoiceStudio\crashes\startup_decision.json` per run when executing manually.

## Per-run evidence

| Run | Session type | Artifact / folder | Result |
|-----|----------------|-------------------|--------|
| 1 | Full Quick (includes build + CI gates) | `artifacts/verify/20260405_075900/` | PASS |
| 2 | OnlyStage **UI Self-Test** (`-SkipBuild`) | `artifacts/verify/20260405_080443/` | PASS |
| 3 | OnlyStage **Icon-Launch Smoke** | `artifacts/verify/20260405_080450/` | PASS |
| 4 | OnlyStage **Failure-Path Smoke** | `artifacts/verify/20260405_080459/` | PASS |
| 5 | OnlyStage **Runtime-Missing Failure Smoke** | `artifacts/verify/20260405_080517/` | PASS |

**Rolling validator:** `.buildlogs/verification/last_run.json` **20260405-080424** (**completion_guard** PASS) — see [VOICESTUDIO_GAP045_TRANSCRIPT_CROSS_CONSUMER_COHERENCE_CLOSURE_2026-04-05.md](VOICESTUDIO_GAP045_TRANSCRIPT_CROSS_CONSUMER_COHERENCE_CLOSURE_2026-04-05.md) §2.

## Post–GAP-045 project-switch slice (same plan wave)

Repeat pass after [VOICESTUDIO_GAP045_TIMELINE_SUBTITLE_PROJECT_SWITCH_CLOSURE_2026-04-05.md](VOICESTUDIO_GAP045_TIMELINE_SUBTITLE_PROJECT_SWITCH_CLOSURE_2026-04-05.md) (run OnlyStages **sequentially** if harness artifact dirs contend):

| Run | Session type | Artifact / folder | Result |
|-----|----------------|-------------------|--------|
| 1 | Full Quick | `artifacts/verify/20260405_190541/` | PASS |
| 2 | UI Self-Test | `artifacts/verify/20260405_191157/` | PASS |
| 3 | Icon-Launch Smoke | `artifacts/verify/20260405_191214/` | PASS |
| 4 | Failure-Path Smoke | `artifacts/verify/20260405_191246/` | PASS |
| 5 | Runtime-Missing Failure Smoke | `artifacts/verify/20260405_191312/` | PASS |

**Rolling validator:** `.buildlogs/verification/last_run.json` **20260405-191135** — see project-switch closure §2.

## Manual operator cold launches (exit → relaunch)

Harness tables above do **not** replace true desktop cold launches. Per [VOICESTUDIO_STARTUP_TRUTH_FINAL_CERTIFICATION_2026-04-05.md](VOICESTUDIO_STARTUP_TRUTH_FINAL_CERTIFICATION_2026-04-05.md), operators record **3–5** runs: fully exit the app, relaunch from the installed EXE or Start Menu (not `dotnet run`), then capture `%LOCALAPPDATA%\VoiceStudio\crashes\startup_decision.json` and note whether the shell reaches a usable state.

| Run | `timestamp_utc` (from artifact) | `decision` | `health_probe_result` | `elapsed_ms` | UI shell usable? | Class (PASS / timeout / port_collision / other) |
|-----|-------------------------------|------------|----------------------|--------------|------------------|------------------------------------------------|
| 1 | | | | | | *Operator to fill after run* |
| 2 | | | | | | *Operator to fill after run* |
| 3 | | | | | | *Operator to fill after run* |
| 4 | | | | | | *Optional* |
| 5 | | | | | | *Optional* |

**Stability (manual):** *Pending until the table above is populated with real runs.*

## Related

- [VOICESTUDIO_UI_STARTUP_BOUNDARY_2026-04-05.md](VOICESTUDIO_UI_STARTUP_BOUNDARY_2026-04-05.md)
- [VOICESTUDIO_RUNTIME_CHAIN_PROOF_2026-04-05.md](VOICESTUDIO_RUNTIME_CHAIN_PROOF_2026-04-05.md)
