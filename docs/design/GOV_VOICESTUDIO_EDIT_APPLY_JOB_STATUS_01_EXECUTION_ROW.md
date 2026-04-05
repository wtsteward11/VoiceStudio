# GOV-VOICESTUDIO-EDIT-APPLY-JOB-STATUS-01 — Transcript apply/regenerate job visibility (GAP-045 bounded slice)

## 0. Status

- **State:** **Closed** 2026-04-01 — closure [VOICESTUDIO_EDIT_APPLY_JOB_STATUS_LANE_CLOSURE_2026-04-01.md](../reports/verification/VOICESTUDIO_EDIT_APPLY_JOB_STATUS_LANE_CLOSURE_2026-04-01.md).
- **Product scope:** **GAP-045** — **Open**; **GAP-047** — **Open**; this lane adds **session-local operator-visible job status** for transcript apply/regenerate only (no product “editing complete”).
- **Depends on:** Inline apply, multi-segment apply, regenerate-segment, jobs API polling — existing infrastructure; **no new backend routes**.

## Changelog

- **2026-04-01:** Lane opened — frozen contract: session-only cap, Transcribe panel UI, backend lifecycle mapping, coordinator `IProgress` seam.
- **2026-04-01:** Lane closed — `TranscriptApplyJobStatus*` models + coordinator progress + `TranscribeViewModel` + Transcribe list UI + tests; **no new backend routes**.

## 1. Objective

Surface **queued / running / succeeded / failed** state for transcript-driven **apply** and **regenerate** operations in the Transcribe workflow, correlated with **job id**, **operation kind**, **segment ids**, **clip id**, and **timestamps**, reusing **`POST /api/transcribe/regenerate-segment`** and **`GET /api/jobs/{id}`** only.

## 2. Verification provenance (oversight guard)

Closure of this lane MUST state **one** of:

| Label | When |
|--------|------|
| `Independently repo-verified locally` | A maintainer ran the verification matrix on a full checkout and captured outputs. |
| `Connector-limited architectural review only` | Tooling or environment prevented local execution; closure documents reason and fallback evidence (e.g. reviewed diffs + test design only). |

Do not claim independent verification without artifact-backed commands.

## 3. Backend raw → operator status (frozen)

| Backend `Job.Status` (or synthetic) | Operator status | Notes |
|-------------------------------------|-----------------|--------|
| `pending` | **Queued** | After job accepted. |
| `running`, `paused` | **Running** | Paused: message may include `(paused)` / step text. |
| `completed` (job only) | **Running** | Synthetic UX: “Synthesis complete; applying to timeline…”. |
| `session_succeeded` (synthetic, end of coordinator success) | **Succeeded** | After clip update + linkage events succeed. |
| `failed` | **Failed** | Prefer `ErrorMessage`. |
| `cancelled` | **Failed** | |
| `timeout` (synthetic poll deadline) | **Failed** | |
| `apply_failed` (synthetic clip apply error) | **Failed** | After job succeeded but `UpdateClipAsync` failed. |

## 4. Source-of-truth fields per status row (frozen)

| Field | Description |
|-------|-------------|
| `OperationId` | Client correlation GUID (session). |
| `OperationKind` | `TranscriptEditOperationKind` aligned with edit history. |
| `SegmentIds` | Affected segments (range = contiguous ids). |
| `ClipId` | Pre-resolved clip when known; else null. |
| `JobId` | Backend job id when assigned. |
| `CreatedUtc` | When the row is created. |
| `CompletedUtc` | When operator status becomes terminal (`Succeeded` / `Failed`). |
| `OperatorStatus` | Queued / Running / Succeeded / Failed. |
| `StatusMessage` | Operator-facing detail (step, error snippet, paused note). |

## 5. Session authority (frozen)

- **Storage:** `TranscribeViewModel` `ObservableCollection` only; **not** persisted.
- **Retention:** Newest-first; **max 15** visible rows; oldest dropped on overflow.
- **Lifecycle:** Cleared only via **Clear** on the status strip or process exit — not tied to project/transcription switches (same session posture as edit history list).
- **Busy marker:** Existing `RegeneratingSegmentId` remains authoritative for segment row UI; status strip complements it.

## 6. Architecture seam (frozen)

- **`TranscriptSegmentRegenerationCoordinator.TryExecuteAsync`** accepts optional `IProgress<TranscriptRegenerationJobProgressReport>?` and `operationCorrelationId`.
- **No** new FastAPI routes or job store schemas.
- **Polling:** Existing `WaitForTerminalJobAsync` loop emits deduped reports on status/progress/step changes + terminal + timeout + synthetic apply outcome.

## 7. Hard IN

- Execution row + closure provenance subsection.
- Coordinator progress emission + VM mapping + Transcribe UI + automation IDs + registry.
- MSTest: coordinator progress lifecycle; VM association + failure-before-job; busy/history coexistence (where covered).
- Full verification matrix on closure (see §9).

## 8. Hard OUT

- New backend `/api/*` routes.
- Persistent job history store or cross-session dashboard.
- Generic job panel redesign; collaboration; timeline rewrite.

## 9. Verification (closure)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
- `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64`
- `python -m pytest tests/ci/ -q --randomly-seed=12345`
- `.\scripts\verify.ps1 -Quick`
- `python scripts/run_verification.py` — **completion_guard** PASS

## 10. Binary acceptance

- [x] Status rows appear for apply/regenerate with correct operation kind and segment/clip context.
- [x] Transitions: Queued → Running → Succeeded / Failed; timeout and apply-failed visible as Failed.
- [x] Failure before job id (e.g. coordinator missing) still yields a Failed row when invocation attempted.
- [x] Edit history still appends once per operation outcome; no duplicate history rows from this lane.
- [x] No new backend route files in this lane’s scope.
