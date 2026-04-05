# GOV-VOICESTUDIO-EDIT-APPLY-RETRY-RECOVERY-01 — Operator retry for failed transcript apply/regenerate (GAP-045)

## 0. Status / Overseer snapshot

- **State:** **Closed** (2026-04-01) — closure [VOICESTUDIO_EDIT_APPLY_RETRY_RECOVERY_LANE_CLOSURE_2026-04-01.md](../reports/verification/VOICESTUDIO_EDIT_APPLY_RETRY_RECOVERY_LANE_CLOSURE_2026-04-01.md).
- **Product scope:** **GAP-045** (text-based audio editing) remains **Open**; this lane is a **bounded sub-lane** only (session-local retry; no new backend routes).
- **Depends on:** Edit-apply job status + inline/regenerate/multi-segment lanes **Closed**; coordinator + `POST /api/transcribe/regenerate-segment` canonical.
- **Gate posture (planning/closure):** Gates C–H PASS where applicable; treat VS-0025 N/A fields per existing ledger. This row does not reset Gate B.
- **Verification provenance (mandatory label on closure):**
  - **`Independently repo-verified locally`** — agent/Developer ran the full matrix on a machine with repo + toolchain access and attached artifact paths; **or**
  - **`Connector-limited architectural review only`** — IDE/connector instability (e.g. Project Access **424/502**) prevented a full local run; closure must **not** claim green CI without file-backed proof.
- **Connector oversight (424/502):** Tracked as environment/connector reliability — see **§10** and `QUALITY_LEDGER.md` until restored; fallback labeling is **Connector-limited** when runs cannot be completed.

## 1. Objective

When a transcript **apply** or **regenerate** job row ends in a **retryable failure**, the operator can **Retry** in-session using a **frozen snapshot** of the request (replacement text, range, anchor geometry, transcription/project/clip correlation). No persistent retry queue.

## 2. Hard IN scope

- Session-local `TranscriptApplyJobStatusEntry` extended with **retry snapshot** fields (see §5).
- **Retry** affordance on **failed** rows only; replay via existing `TranscribeViewModel.RegenerateSegmentAudioAsync` + `TranscriptSegmentRegenerationCoordinator`.
- **Fail-closed** preflight if transcription, project, segment timing, or resolver clip no longer match the snapshot.
- **Append** a new job row / history semantics on retry (no rewrite of prior failed row).
- MSTest coverage: success path replay, fail-closed paths, snapshot vs live draft divergence.

## 3. Hard OUT of scope

- New FastAPI routes or job types; durable/persisted retry queues; cross-user orchestration.
- Cancellation redesign; timeline rewrite authority changes.
- Retry for rows that are not **apply/regenerate** job failures (e.g. **Filler cleanup** draft-only flows do not use this job-status path).

## 4. Retryable operator / backend states (frozen)

Rows with `TranscriptApplyOperatorJobStatus.Failed` where the terminal backend/coordinator status maps via `TranscriptApplyJobStatusMapper` from any of:

- `failed`, `cancelled`, `timeout`, `apply_failed`
- Coordinator unavailable preflight (synthetic row already created as Failed)

**Not retryable:** `Succeeded`, in-flight `Queued`/`Running`, or `FillerCleanupDraft` operation kind on the entry.

## 5. Deterministic snapshot (source of truth at attempt start)

Each job row stores at construction time:

| Field | Purpose |
|--------|--------|
| `TranscriptionId` | Must match `SelectedTranscription.Id` on retry |
| `ProjectId` | When non-null, must match active `SelectedProjectId` |
| `ReplacementTextSnapshot` | Exact string passed to coordinator for synthesis (null = pure regenerate) |
| `RangeEndInclusiveIndex` | Multi-segment range end index; null = single segment |
| `AnchorSegmentStart` / `AnchorSegmentEnd` | Anchor segment timing; must match current segment on retry (epsilon below) |
| `ClipId` | When non-null, resolver must still resolve anchor to this clip |

**Epsilon:** `1e-6` seconds for start/end equality.

Retry **must not** read replacement text from the live editing draft; only from `ReplacementTextSnapshot`.

## 6. UI (frozen)

- Transcribe panel **Apply / regenerate jobs** list: per-row **Retry** when `CanShowRetry`.
- Optional compact hint in status summary: retry available (non-blocking).
- New control **`TranscribeView_ApplyJobRetryButton`** (automation id).

## 7. Acceptance criteria

- Retry visible only on retryable failed rows.
- Retry calls backend with snapshot payload (replacement + range + kinds), not mutable draft.
- Stale context → clear operator feedback, **no** `StartRegenerateSegment` call.
- Successful retry leaves prior failed row unchanged; adds new attempt row / history consistent with existing regen attempt recording.

## 8. Verification

- `dotnet build`, `dotnet test` (App.Tests: Transcribe VM + mapper as needed), `pytest tests/ci`, `verify.ps1 -Quick`, `run_verification.py` (**completion_guard** PASS) when provenance is **Independently repo-verified locally**.
- Closure report under `docs/reports/verification/` with explicit **provenance** label.

## 9. Rollback

- Revert: `TranscriptApplyJobStatusModels`, `TranscribeViewModel`, `TranscribeView` (+ code-behind), tests, automation docs, this row, closure report, registry/tracker/STATE edits tied to this lane.

## 10. Connector / Project Access blocker

- **Symptom:** Repeated **424/502** on Project Access / connector paths blocking full verification.
- **Owner:** Overseer + environment (restore stable workspace access).
- **Until fixed:** Use **Connector-limited architectural review only** in closure if full matrix cannot be run; log in `QUALITY_LEDGER.md` with date and owner.

## Changelog

- **2026-04-01:** Lane closed — snapshot model, `TranscribeViewModel.RetryTranscriptApplyJobAsync`, Transcribe Retry UI, tests, governance sync; provenance **Independently repo-verified locally** per closure §0.
- **2026-04-01:** Initial execution row — GOV-VOICESTUDIO-EDIT-APPLY-RETRY-RECOVERY-01.
