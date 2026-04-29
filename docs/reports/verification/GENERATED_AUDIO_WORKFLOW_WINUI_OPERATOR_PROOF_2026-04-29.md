# Generated-audio WinUI **operator** proof (2026-04-29)

**Mission:** Human-operator proof of synthesis → library → timeline → durability → playback, with API/log corroboration only (no API substitute for UI). This document is **separate** from [GENERATED_AUDIO_WORKFLOW_WINUI_PROOF_2026-04-29.md](GENERATED_AUDIO_WORKFLOW_WINUI_PROOF_2026-04-29.md) (launch-smoke / agent preflight).

**Repo SHA:** `0f8b90bec56c4979a463e88c7c637ee37af00d14` (`HEAD` == `origin/main` at proof time).

**Backend mode:** `uvicorn backend.api.main:app` on `127.0.0.1:8000` via `.venv`; **`VOICESTUDIO_TEST_MODE` unset** (not stub/test mode for this run). Engine surface: manifests / safe mode; **`initialized_engines`: 0** in health `details.engines`.

---

## 1. Preflight — build and gates

| Step | Result |
|------|--------|
| `git fetch origin` + `git status -sb` | `HEAD` == `origin/main` at `0f8b90be`; staged index empty; working tree had only expected local noise (`M` `AGENTS.md`, `M` `.vscode/settings.json`, `??` `backend/data/voicestudio.db`, `??` `docs/reports/audit/*.md`). |
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **Succeeded** — 0 errors (nullable warnings in app projects only; pre-existing pattern). |
| `e:\VoiceStudio\.venv\Scripts\python.exe scripts/run_verification.py` | **Overall PASS** — `.buildlogs/verification/last_run.json` (timestamp `2026-04-29T05:43:02.997704+00:00`). Advisories: `runtime_proof_staleness`, `slo_baseline_freshness`, `backend_smoke_freshness` (warning-only). |

---

## 2. Backend health and readiness

**Health** `GET http://127.0.0.1:8000/api/health/` — **HTTP 200**

- Top-level **`status`:** `degraded` (nested **`gpu`** check: torch-based GPU detection not enabled; message references `VOICESTUDIO_HEALTH_ENABLE_TORCH=1`).
- **`checks.database`:** `healthy`
- **`checks.engines`:** `healthy`
- **`details.engines`:** `available_engines` 64, **`initialized_engines`:** 0, `total_engines` 64, message includes **“Engine availability derived from manifests (safe mode)”**.

**Readiness** `GET http://127.0.0.1:8000/api/health/readiness` — **HTTP 200**

- **`ready`:** `true`, **`status`:** `ready`

**`git_commit` in health JSON:** Not cited; not confirmed present in captured payload (do not invent).

---

## 3. WinUI launch (Phase 2)

| Field | Value |
|-------|--------|
| Executable | `e:\VoiceStudio\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe` |
| PID | **32344** |
| Main window title | **VoiceStudio Quantum+** |
| Stability | Process still running after **~20 s** and again after **~45 s** total; no crash observed in that window. |
| Post-observation | Process terminated to clean the session (not a product defect). |

---

## 4. Operator workflow (Phase 3) — **not executed**

**Reason:** No human operator was available in this agent session to perform the mandatory UI checklist.

**Planned checklist anchors** (from [AUTOMATION_ID_REGISTRY.md](../../developer/AUTOMATION_ID_REGISTRY.md)): `VoiceSynthesisView_EngineComboBox`, `VoiceSynthesisView_ProfileComboBox`, `VoiceSynthesisView_TextInput`, `VoiceSynthesisView_SynthesizeButton`, `VoiceSynthesisView_AddGeneratedAudioToLibraryButton`, `VoiceSynthesisView_AddGeneratedAudioToTimelineButton`, `VoiceSynthesisView_GeneratedAudioTimelineStatus`, `TimelineView_Root`, `VoiceSynthesisView_PlayButton` / `VoiceSynthesisView_StopButton`, consent/retry `InfoBar` AutomationIds as applicable.

**Fixed phrase (for human rerun):** `VoiceStudio generated audio WinUI durability proof.`

**Engine rule:** **Piper** if available in UI; otherwise document actual engine and why Piper was not used — **not performed** in this run.

**Evidence table:** Intentionally empty — no fabricated steps, timestamps, or synthesis results.

---

## 5. Generated audio evidence

**N/A** — synthesis UI not run.

---

## 6. Library evidence

**N/A** — Add to Library not run.

---

## 7. Timeline evidence

**N/A** — Add to Timeline not run.

---

## 8. Durability evidence

**N/A** — reload/restart persistence not tested (no clip created in UI).

---

## 9. Playback evidence

**Tri-state:** **`not tested`** (no operator; no audio path exercised in UI).

---

## 10. API and log cross-check (Phase 4)

**`session_id`:** **Not discoverable** — no UI session, logs, or devtools capture tied to a real operator workflow; **no** `GET /api/timeline/state?session_id=...` call was made with an invented id.

**Honest scope:** Health and readiness endpoints only (above).

---

## 11. Defects found

None observed in preflight, build, verification script, health/readiness, or WinUI launch smoke.

---

## 12. Documentation

This file satisfies Phase 7 operator proof record. Registry addendum: [CANONICAL_REGISTRY.md](../../governance/CANONICAL_REGISTRY.md).

**`.cursor/STATE.md`:** Not updated — entire `.cursor/` tree is gitignored; optional single-line parallel-lane note deferred to avoid unscoped control-plane churn.

---

## 13. Commit intent

Single commit message (proof only): `docs(runtime): record generated audio WinUI operator proof`

**No `git push`** unless explicitly instructed.

---

## 14. Verdict (Phase 5 — strict)

**`NOT RUN`** — Per mission table: no operator / proof aborted before meaningful UI checklist (honest). Launch and preflight succeeded; **not** a **FULL PASS**, **PARTIAL**, or workflow **FAIL** (no failing UI step — workflow simply not attempted).

---

## 15. Non-claims

- **Not** GAP-008 / **not** Slice 46 / **not** new `MainWindow*ShellBridge` work.
- **Not** RHVoice proof / **not** edits to `ENGINE_PARITY_MATRIX.md`.
- **Not** claiming runtime **FULL PASS** for generated-audio workflow.
- **Not** conflating this doc with the same-day launch-smoke report ([GENERATED_AUDIO_WORKFLOW_WINUI_PROOF_2026-04-29.md](GENERATED_AUDIO_WORKFLOW_WINUI_PROOF_2026-04-29.md)).
