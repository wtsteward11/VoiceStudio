# Voice Synthesis Generated-Audio Workflow Smoke

**Date:** 2026-04-28  
**Repo SHA:** `8d63a01a56cd798058c3402836e2e2654ee57c48`  
**Branch:** `main` (in sync with `origin/main`)  
**Workflow:** Generated-audio insertion into project timeline  
**Overall result:** **NOT RUN** (manual operator steps not executed)

---

## Repo Guard (Phase 1)

| Check | Result |
|---|---|
| `HEAD` | `8d63a01a56cd798058c3402836e2e2654ee57c48` |
| `origin/main` | `8d63a01a56cd798058c3402836e2e2654ee57c48` |
| HEAD == origin/main | **Yes** |
| Staged changes | **None** |
| Dirty files | `AGENTS.md`, `.vscode/settings.json` (unstaged, intentional) |

---

## Build and Backend Preflight (Phase 2)

### dotnet build

```
Configuration: Debug  Platform: x64
Errors:    0
Warnings:  5  (pre-existing nullable CS8619/CS8604 — not introduced by this work)
Elapsed:   ~58 s
Result:    PASS
```

### run_verification.py

```
Overall: PASS  (exit 0)
Advisory: SLO baselines stale (425 h > 72 h policy, advisory only)
Advisory: backend smoke stale (356 h > 72 h policy, advisory only)
Artifact: .buildlogs/verification/last_run.json
```

### Backend health — GET http://127.0.0.1:8000/api/health

```json
{
  "status": "ok",
  "version": "1.1.0",
  "version_string": "v1.1.0 (b0a1b793)",
  "engines_ready": true,
  "git_commit": "b0a1b793",
  "python_version": "3.11.9"
}
```

- HTTP status: **200 OK**
- `engines_ready`: **true**
- Backend `git_commit`: `b0a1b793` (backend version; differs from frontend SHA — expected, backends are deployed independently)

### Application launch

```
EXE: src\VoiceStudio.App\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe
PID: 20812
Status: RUNNING (still alive 3 s after launch)
```

---

## Manual Workflow Smoke (Phase 3)

| Step | Action | Status |
|---|---|---|
| 1 | Open Voice Synthesis | NOT RUN |
| 2 | Select Piper | NOT RUN |
| 3 | Select compatible profile | NOT RUN |
| 4 | Enter phrase: `VoiceStudio generated audio project timeline smoke.` | NOT RUN |
| 5 | Synthesize | NOT RUN |
| 6 | Confirm generated result appears | NOT RUN |
| 7 | Click Add to Library | NOT RUN |
| 8 | Confirm saved status | NOT RUN |
| 9 | Click Add to Timeline | NOT RUN |
| 10 | Open or view Timeline | NOT RUN |
| 11 | Confirm generated clip appears without manual app restart | NOT RUN |
| 12 | Confirm clip placement is not obviously overlapping | NOT RUN |
| 13 | Record whether playback from generated result still works | NOT RUN |

**Reason:** All 13 steps require a human operator interacting with the live UI. The automated agent cannot execute UI gestures or read screen state. The application was running (PID 20812) and ready for operator use; no steps were executed by automation.

---

## Classification

**Verdict: NOT RUN**

The preflight (build + backend + launch) passed cleanly. The manual smoke could not be completed because no operator was present to drive the UI. No fake partial proof is claimed.

---

## Defects Found

**None** — smoke was not executed; no defects observed or claimed.

---

## Explicit Non-Claims

- This report does **not** claim runtime FULL PASS.
- This report is **not** GAP-008 work.
- This report does **not** address RHVoice.
- This report does **not** modify `ENGINE_PARITY_MATRIX.md`.
- `AGENTS.md` and `.vscode/settings.json` were **not** staged or modified.
