# Generated-audio WinUI workflow proof (2026-04-29)

**Repo SHA:** `3f0155f6edeba4a896e9309bbeabebf980faec13` (`HEAD` == `origin/main` at proof time).

**Baseline:** Post-push API durability on `main` ([GENERATED_AUDIO_WORKFLOW_DURABILITY_PROOF_2026-04-28.md](GENERATED_AUDIO_WORKFLOW_DURABILITY_PROOF_2026-04-28.md)): `/api/timeline` excluded from response cache; SQLite session timeline + revision CAS.

---

## Build and automated gate

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **Succeeded** — 0 errors, 706 warnings (pre-existing test/nullability warnings). |
| `python scripts/run_verification.py` (`.venv` 3.11) | **Overall PASS** — `.buildlogs/verification/last_run.json`. |
| Advisories | `runtime_proof_staleness`, `slo_baseline_freshness`, `backend_smoke_freshness` (stale proof artifacts; warning-only). |

---

## Backend preflight

**Process:** `uvicorn backend.api.main:app` on `127.0.0.1:8000` via `.venv`, env **`VOICESTUDIO_TEST_MODE=stub`** (stub/test mode for predictable local run).

**Health `GET http://127.0.0.1:8000/api/health/`**

- **HTTP:** 200
- **Payload `status`:** `degraded` (GPU check unhealthy/skipped; database + engines checks reported healthy in nested `checks`).
- **`details.engines`:** `status` healthy; `initialized_engines` 0; `total_engines` 64; manifest-derived availability in safe mode.

**Readiness `GET http://127.0.0.1:8000/api/health/readiness`**

- **HTTP:** 200
- **`ready`:** `true`, **`status`:** `ready` (used as operator readiness surrogate; no single field named `engines_ready` in this response).

**`git_commit` in health JSON:** Not observed in the captured `/api/health/` excerpt (may be absent or in another sub-object).

**DB path:** Not returned in health payload for this run; SQLite session store follows project defaults (see timeline durability docs).

---

## Proof method

**WinUI human / automation:** **Agent-automated preflight + launch smoke only.** No human operator executed Voice Synthesis → Library → Timeline in this session. Per bounded plan: **no fabricated UI proof**.

**Executable path resolved:** `.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe` (repo root `.buildlogs`, not under `src\VoiceStudio.App\`).

---

## WinUI launch (smoke)

| Field | Value |
|-------|--------|
| PID | 41116 (at launch; process stopped after observation) |
| Main window title | `VoiceStudio Quantum+` |
| Stability | Process still running after **15 s**; no immediate crash observed. |

**Voice Synthesis checklist (operator):** **Not executed** — engine/profile/phrase/synthesize/add library/add timeline/reload/playback not performed in this agent run.

---

## Phrase (planned for human rerun)

`VoiceStudio generated audio WinUI durability proof.`

---

## Generated audio evidence

**None from WinUI** (checklist not run).

---

## Library evidence

**None from WinUI** (checklist not run).

---

## Timeline and durability (WinUI)

**None from WinUI** (checklist not run).

---

## API cross-check (non-substitute)

Performed **after** backend start for connectivity only; **not** post-WinUI corroboration of a generated clip.

`GET http://127.0.0.1:8000/api/timeline/state?session_id=default`

- **HTTP:** 200
- **`revision`:** 5
- **`tracks`:** `[]` (empty)
- **Interpretation:** Baseline default session; no proof-of-insertion from this run.

---

## Playback / heard

**Not tested** (no synthesis result; no playback triggered).

---

## Defects found

**None** in product code from this proof (no defect-driven code changes).

---

## Verdict

**PARTIAL** — matches plan criterion *“only launch/smoke without full checklist”*: build + `run_verification.py` green; backend health/readiness OK under stub; WinUI exe launched with visible main window title; **no** operator Voice Synthesis → Library → Timeline → reload → heard attestation.

**NOT RUN** would apply if the mission had stopped before launch smoke; launch smoke was completed, so **PARTIAL** is the stricter honest classification.

---

## Non-claims

- **Not** GAP-008; **not** Slice 46; **not** any new `MainWindow*ShellBridge`.
- **Not** RHVoice.
- **Not** [ENGINE_PARITY_MATRIX.md](ENGINE_PARITY_MATRIX.md) edits.
- **Not** a second backend hardening lane.
- **Not** **runtime FULL PASS** — no in-app synthesis, library, timeline, reload durability, or heard playback evidence.

---

## Control plane

- **`.cursor/STATE.md`:** Not edited in this commit (file is **gitignored** in this repo; local operators may add a minimal **Parallel** bullet with `git add -f` per [CANONICAL_REGISTRY.md](../../governance/CANONICAL_REGISTRY.md) policy if desired).

---

## Changelog

- **2026-04-29:** Initial record — bounded WinUI proof attempt (preflight + launch smoke; operator path deferred).
