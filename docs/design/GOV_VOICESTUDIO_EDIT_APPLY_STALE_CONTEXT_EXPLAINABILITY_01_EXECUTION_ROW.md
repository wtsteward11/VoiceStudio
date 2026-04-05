# GOV-VOICESTUDIO-EDIT-APPLY-STALE-CONTEXT-EXPLAINABILITY-01 — Stale-context explainability (GAP-045)

## 0. Status / Overseer snapshot

- **State:** **Closed** (2026-04-02) — closure [VOICESTUDIO_EDIT_APPLY_STALE_CONTEXT_EXPLAINABILITY_LANE_CLOSURE_2026-04-02.md](../reports/verification/VOICESTUDIO_EDIT_APPLY_STALE_CONTEXT_EXPLAINABILITY_LANE_CLOSURE_2026-04-02.md).
- **Product:** **GAP-045** remains **Open**; bounded sub-lane only.
- **Depends on:** Edit-apply job status, retry/recovery, context jump, `TranscriptOperatorMessage`, retry toasts.
- **Verification provenance (closure):** **Independently repo-verified locally** when full matrix is run in a working tree; otherwise label per closure report.

## 1. Objective

When **retry** or **context jump** is blocked, the operator sees a **precise, category-consistent** explanation (`Jump blocked:` / `Retry blocked:`) naming the failed invariant—not vague “unable to navigate” copy.

## 2. Failure taxonomy (frozen)

| Category | Jump surface | Retry surface |
| --- | --- | --- |
| Missing transcription id | Operator line | N/A (row gated elsewhere—or retry uses job row) |
| Project mismatch | Operator line | Toast |
| Transcription not in session list | Operator line | Toast |
| Transcription mismatch (active vs row) | (via list / selection) | Toast |
| No segment target | Operator line | N/A |
| Segment not in transcription | Operator line | Toast |
| Range/timing invalidated | N/A | Toast |
| Resolver not registered | Operator line | Toast |
| Resolver unresolved (by kind) | Operator line (mapped from `TranscriptSegmentTargetResolutionKind`) | N/A |
| Clip mismatch row vs resolve | Operator line | Toast |

## 3. Hard IN

- Shared copy in [TranscriptStaleContextExplainability.cs](../../src/VoiceStudio.App/Services/TranscriptStaleContextExplainability.cs).
- Wire [TranscribeViewModel.cs](../../src/VoiceStudio.App/Views/Panels/TranscribeViewModel.cs): `JumpTranscriptRowToSourceContext`, `OnTargetTranscriptionSegmentTapped`, `RetryTranscriptApplyJobAsync`, early navigation exits.
- Explicit `TranscriptOperatorMessage` on jump early-return (including empty transcription id on history/job navigate).
- MSTest coverage per category touched by existing VM tests.
- **No** new backend routes.

## 4. Hard OUT

- Backend API / schema work.
- Persistence of error codes.
- PanelHost / timeline authority redesign.

## 5. Verification

- Full matrix: `dotnet build`, full App.Tests, `pytest tests/ci`, `verify.ps1 -Quick`, `run_verification.py` (**completion_guard** PASS).
- Optional fresh backend smoke: [VOICESTUDIO_RUNTIME_STARTUP_SMOKE_2026-04-02.md](../reports/verification/VOICESTUDIO_RUNTIME_STARTUP_SMOKE_2026-04-02.md).

## 6. Rollback

- Revert lane-only helper, VM string wiring, tests, docs.

## Changelog

- **2026-04-02:** Execution row frozen; implementation and closure in same bounded batch.
