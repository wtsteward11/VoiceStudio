# Generated-audio WinUI **operator** proof (2026-04-29)

**Mission:** Human-operator proof of synthesis -> library -> timeline -> durability -> playback, with API/log corroboration only (no API substitute for UI). This document remains separate from [GENERATED_AUDIO_WORKFLOW_WINUI_PROOF_2026-04-29.md](GENERATED_AUDIO_WORKFLOW_WINUI_PROOF_2026-04-29.md) (launch-smoke / agent preflight).

**Repo SHA:** `66e9f6892834a12080b8602028eefba26b175e68` (`HEAD` == `origin/main` at evidence time).

**Backend mode:** `uvicorn backend.api.main:app` on `127.0.0.1:8000` via `.venv`; `VOICESTUDIO_TEST_MODE` unset; engine availability from manifests / safe mode (`initialized_engines`: 0).

---

## 1. Preflight - build and gates

| Step | Result |
|------|--------|
| `git fetch origin` + `git status -sb` + `git rev-parse HEAD` + `git rev-parse origin/main` + `git diff --cached --name-only` | `HEAD` == `origin/main` at `66e9f689`; staged index empty; working tree contained only expected local noise (`AGENTS.md`, `.vscode/settings.json`, `backend/data/voicestudio.db`, `docs/reports/audit/*.md`). |
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **Succeeded** - 0 errors, 5 nullable warnings (pre-existing warnings in app code). |
| `.venv\\Scripts\\python.exe scripts/run_verification.py` | **Overall PASS** (`.buildlogs/verification/last_run.json`, `2026-04-29T06:16:11.960160+00:00`). Advisories: `runtime_proof_staleness`, `slo_baseline_freshness`, `backend_smoke_freshness`. |

---

## 2. Backend health and readiness

`GET http://127.0.0.1:8000/api/health/` -> **HTTP 200**

- Top-level `status`: `degraded`
- `checks.database`: `healthy`
- `checks.engines`: `healthy`
- Degraded check: GPU probe skipped / unavailable (`VOICESTUDIO_HEALTH_ENABLE_TORCH` guidance in message)
- `details.engines`: `available_engines` 64, `initialized_engines` 0, `total_engines` 64, message `"Engine availability derived from manifests (safe mode)"`

`GET http://127.0.0.1:8000/api/health/readiness` -> **HTTP 200**

- `ready`: `true`
- `status`: `ready`

Observed backend listener PID: `28856`.

---

## 3. WinUI launch (Phase 2)

| Field | Evidence |
|-------|----------|
| Executable | `e:\\VoiceStudio\\.buildlogs\\x64\\Debug\\net8.0-windows10.0.19041.0\\VoiceStudio.App.exe` |
| Automated launch checks | Agent launch recorded `LAUNCH_PID=7480`, then `AFTER5S_RUNNING=0`, `AFTER30S_RUNNING=0`. |
| Manual operator note | Operator reported app starts when launched manually via `Start-Process`, but no stable PID/title capture was preserved in terminal evidence. |
| Stability verdict | **Unstable/inconclusive** in automated capture; manual observation lacks durable runtime fields required by checklist evidence table. |

---

## 4. Operator workflow execution (Phase 3)

Human checklist evidence was not captured in the required structured form for:

- selected engine/profile,
- synthesis success,
- generated output fields (id/reference/duration/quality),
- add-to-library status,
- add-to-timeline status,
- timeline clip details,
- reload/reopen persistence,
- restart persistence,
- playback outcome.

Because required step-level artifacts were missing, the workflow cannot be credited as successful proof.

---

## 5. Generated audio evidence

No verifiable generated-audio UI artifact was captured (no confirmed audio id/reference/path/duration/quality entry from the human checklist).

---

## 6. Library evidence

No verifiable Add-to-Library success artifact was captured from UI.

---

## 7. Timeline evidence

No verifiable Add-to-Timeline UI artifact was captured.

---

## 8. Durability evidence

No verifiable reload/reopen or restart persistence evidence was captured from the UI checklist.

---

## 9. Playback evidence

No audibility result (`heard yes` / `not heard environment` / `not tested`) was captured in structured operator evidence.

---

## 10. API and log cross-check (Phase 4)

API corroboration (non-substitute):

- `GET /api/timeline/state?session_id=default` returned timeline id `19d7b5b4-9b97-404f-82b4-cac86d4e424d`, `revision` `7`, `tracks` `[]`, `updated_at` `2026-04-29T00:54:10.008260`.
- No UI-derived `session_id` attributable to the required operator checklist was discovered in captured evidence.

Cross-check conclusion: API response does not corroborate a successful generated-audio timeline insertion for this run.

---

## 11. Defects found

1. **Launch stability defect/investigation gap:** automated app process exits before 5 seconds in agent launch checks; manual start claim exists but lacks durable runtime evidence.
2. **Proof evidence gap:** required human checklist outputs were not captured, preventing verification of synthesis/library/timeline/durability/playback.

No code fix was applied in this run because the proof failure was evidence/operability capture failure, not a fully isolated source-level defect with reproducible stack trace.

---

## 12. Documentation

This file is updated with current run evidence and strict outcome.
Registry addendum is updated in [CANONICAL_REGISTRY.md](../../governance/CANONICAL_REGISTRY.md) to reflect the revised verdict and evidence.

`.cursor/STATE.md` not edited in this task.

---

## 13. Commit intent

Proof-only commit message: `docs(runtime): update generated audio WinUI operator proof`

No push unless explicitly instructed.

---

## 14. Verdict (Phase 5 - strict)

**FAIL**

Reason: required success conditions were not proven. There is no complete evidence for synthesis + library + timeline + persistence + playback in this run, and automated launch stability capture failed.

---

## 15. Non-claims

- Not GAP-008 / not Slice 46 / not `MainWindow*ShellBridge`.
- Not RHVoice work.
- Not `ENGINE_PARITY_MATRIX.md` edits.
- Not claiming runtime FULL PASS for generated-audio workflow.
