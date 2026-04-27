# Runtime Truth Follow-up — WinUI cold launch + in-app Piper playback

## Scope

This proof extends the 2026-04-26 runtime lane toward **product-shaped** evidence: **WinUI cold launch** plus **in-app Piper** synthesis and playback. It is **not** an RHVoice proof, **not** engine parity matrix work, and **not** GAP-008 Slice 46 or any new `MainWindow*ShellBridge` implementation.

## Repo State

| Item | Value |
|------|--------|
| **HEAD** | `b0a1b7937e77a65885f498d3eeb51dc4dc923f5c` |
| **origin/main** | `b0a1b7937e77a65885f498d3eeb51dc4dc923f5c` (after `git fetch origin`) |
| **HEAD == origin/main** | Yes |
| **git status (summary)** | `## main...origin/main` with local modifications only on `AGENTS.md` and `.vscode/settings.json` |

**Dirty files (pre-existing, not modified by this pass):**

- `AGENTS.md` — lane governance paragraph + four new Required Rules bullets (workflow `.mdc` references).
- `.vscode/settings.json` — expanded `git.repositoryScanIgnoredFolders` array; added `dotrush.roslyn.projectOrSolutionFiles` → `VoiceStudio.sln`; file missing newline at EOF in diff.

Neither file was edited, staged, or reverted during this verification session.

## Baseline Verification

| Item | Value |
|------|--------|
| **Command** | `.\scripts\verify.ps1 -Quick` |
| **Exit code** | `0` |
| **VERIFICATION PASSED** | Yes |
| **Artifact folder** | `E:\VoiceStudio\artifacts\verify\20260427_144226` |
| **Report** | `E:\VoiceStudio\artifacts\verify\20260427_144226\verification_report.md` |

**Advisories (non-failing):**

- `runtime_proof_staleness` — golden path proof age beyond policy window (warning-only).
- `slo_baseline_freshness` — SLO baseline file age (advisory).
- `backend_smoke_freshness` — backend smoke proof age (advisory).

## Backend Readiness

| Item | Value |
|------|--------|
| **Backend URL** | `http://127.0.0.1:8000` |
| **Health endpoint** | `GET http://127.0.0.1:8000/api/health` |
| **HTTP status** | 200 |
| **engines_ready** | `true` |
| **version_info.git_commit** | `b0a1b793` (short form; matches workspace **HEAD** prefix) |
| **Commit match** | Aligned with workspace **HEAD** (no environment mismatch for commit identity) |

**Startup:** Backend was started with:

`E:\VoiceStudio\.venv\Scripts\python.exe -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8000`

(Operator-local session; not a CI artifact.)

## Engine Readiness

| Item | Value |
|------|--------|
| **Command** | `VOICESTUDIO_ENGINE_PROBE_FULL=1 python scripts/engine_readiness_probe.py` |
| **Primary output** | `docs/reports/verification/slice12/engine_readiness_probe.json` |
| **timestamp_utc** | `2026-04-27T19:47:22.383719+00:00` |
| **mode** | `manifest_scan_plus_full_router` |

**Piper (proof-relevant):**

- `per_engine.piper.registered`: true  
- `instantiable`: true  
- `instance_type`: `PiperEngine`  
- `preflight_assets.ok`: true  
- `preflight_assets.message`: `Piper voice ready: en_US-amy-medium`  
- Paths: `E:\VoiceStudio\models\piper\en_US-amy-medium.onnx` (+ matching `.json`)

**RHVoice:** The full-router probe enumerates all registered engines (including **rhvoice** in manifests and router tables). **RHVoice was not selected for synthesis** in this session. Any RHVoice-related log lines during `load_all_engines` are router inventory noise, not proof of RHVoice use.

**Other engines:** Full probe logs many optional engine failures or missing optional deps (e.g. vosk, whisperx, coqui import paths); these do not invalidate Piper readiness for this follow-up.

## WinUI Cold Launch

| Item | Value |
|------|--------|
| **Launch method** | `Start-Process` of built debug executable: `E:\VoiceStudio\src\VoiceStudio.App\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe` |
| **Result** | **Pass (process level)** — `VoiceStudio.App` process present after launch (e.g. PID **26996**, start **2026-04-27 ~14:50 local**). |
| **Screenshot** | Not captured (no repo-automated screenshot step in this session). |
| **UI state** | Not read by automation; operator visual confirmation of shell/startup UI was **not** recorded in this pass. |

## In-App Piper Synthesis

| Item | Value |
|------|--------|
| **Recommended phrase** | `VoiceStudio runtime truth follow-up using Piper.` |
| **Engine** | Piper only (no RHVoice). |
| **Voice / profile** | Not selected in UI during this session — **no in-app synthesis run** was executed through WinUI by the verifier. |
| **Output path** | None discovered from in-app run (no synthesis performed in-app here). |
| **Result** | **Partial / not executed** — proof gap: requires **human operator** (or WinAppDriver / documented UI automation) to complete the in-app path. |

**Note:** `POST /api/voice/synthesize` remains gated by consent policy in normal mode. **Follow-up B** (below) records **consent-correct HTTP** synthesis + a downloaded WAV for **supplementary** file/engine proof only; it does **not** satisfy the plan’s **in-app** UI proof requirement by itself.

## In-App Playback

| Item | Value |
|------|--------|
| **Playback heard in-app** | Not tested — no in-app generated clip from this session. |
| **Limitation** | Agent session did not drive WinUI controls for synthesize/play; audible confirmation would require operator at the machine. |
| **Proof class** | **Partial runtime proof** — backend + Piper preflight + WinUI process launch are evidenced; full product path (UI phrase → file → play) is **not** closed here. |

## Artifact Validation (initial session)

No application-emitted WAV from this session’s in-app flow was produced or located.

**Optional reference (models, not synthesis output):** Piper ONNX assets on disk (`en_US-amy-medium.onnx` and `.json`) are cited from `engine_readiness_probe.json` preflight; no RIFF/WAV header or RMS check was applied to a new synthesis file (none captured).

## Follow-up B (operator + consent-correct API, 2026-04-27)

This section records **Follow-up B** execution after the initial pass above. **FULL PASS** (per plan) still requires **in-app** phrase entry, file discovery from UI, and **in-app playback** with honest audio confirmation; those steps were **not** automated here. What **was** proven is **policy-correct Piper synthesis** through the public API after an explicit **consent grant**, plus **durable file evidence**, plus a **second WinUI cold launch** with a live main window title.

### B0 — Backend health (re-check)

| Item | Value |
|------|--------|
| **GET** | `http://127.0.0.1:8000/api/health` |
| **engines_ready** | `true` |
| **version_info.git_commit** | `b0a1b793` |

### B1 — Consent then Piper synthesis (HTTP; not a substitute for in-app UI)

| Step | Detail |
|------|--------|
| **Profile ID** | `22ebe087-5589-4d35-ab5a-c57049407813` (existing dev profile `csharp-slice10-piper-playback-audition` from `GET /api/profiles`). |
| **403 (pre-consent)** | `POST /api/voice/synthesize` with `engine: piper` and the exact phrase returned **`No active consent for voice`** — expected under `require_synthesis_clearance`. |
| **Consent request** | `POST /api/consent/request` with `voice_id` = profile UUID, `grantor_id` = `runtime-proof-b`, `grantor_name` = `Runtime Proof B`, `consent_type` = `voice_cloning` → **`consent_id`**: `consent_7acf2adb`, status `pending`. |
| **Consent grant** | `POST /api/consent/grant/consent_7acf2adb` → status **`granted`**. |
| **Synthesis** | `POST /api/voice/synthesize` with `engine: piper`, same `profile_id`, text **`VoiceStudio runtime truth follow-up using Piper.`** → **200**, `routed_engine`: **`piper`**, `audio_id`: **`synth_22ebe087-5589-4d35-ab5a-c57049407813_a82d30f8`**, `audio_url`: `/api/voice/audio/synth_22ebe087-5589-4d35-ab5a-c57049407813_a82d30f8`. |

**Note:** `VOICESTUDIO_TEST_MODE=stub` was **not** used for proof-class Piper audio (stub path does not exercise real Piper). Consent + normal synthesis path was used instead.

### B2 — WinUI cold launch (second pass)

| Item | Value |
|------|--------|
| **Executable** | `E:\VoiceStudio\src\VoiceStudio.App\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe` |
| **Method** | `Start-Process` (operator session). |
| **Evidence** | Process **`VoiceStudio.App`** PID **32608**, start **2026-04-27 ~15:43 local**, **`MainWindowTitle`**: **`VoiceStudio Quantum+`** (window present; not a headless crash). |
| **Screenshot** | Not captured (written operator note only). |

### B3 — Artifact on disk (downloaded via `audio_url`)

| Item | Value |
|------|--------|
| **Command** | `Invoke-WebRequest -Uri 'http://127.0.0.1:8000/api/voice/audio/synth_22ebe087-5589-4d35-ab5a-c57049407813_a82d30f8' -OutFile 'E:\VoiceStudio\artifacts\runtime_truth_b_piper.wav'` |
| **Path** | `E:\VoiceStudio\artifacts\runtime_truth_b_piper.wav` |
| **Size** | **169004** bytes (**> 1 KiB**) |
| **Header** | `Format-Hex` first 16 bytes: **`52 49 46 46`** … **`57 41 56 45`** (**RIFF** / **WAVE**). |
| **Non-silent (optional)** | `python -c` using `wave` + RMS on all frames → **frames** `84480`, **RMS** ~**3967** (int16 samples; clearly non-silent). |

### B4 — In-app playback

| Item | Value |
|------|--------|
| **In-app play** | **Not executed** — no WinUI automation drove synthesize/play controls; agent session cannot attest “heard in-app.” |
| **Honest limit** | Playback attestation requires a **human operator** at the machine (or WinAppDriver / documented UI automation). The WAV above can be played locally outside this report’s proof class. |

### B5 — Verification gate (before this report commit)

| Item | Value |
|------|--------|
| **`python scripts/run_verification.py`** | **PASS** (overall); JSON: `.buildlogs/verification/last_run.json` |
| **`.\scripts\verify.ps1 -Quick`** | **PASS** (exit **0**); report folder **`E:\VoiceStudio\artifacts\verify\20260427_154452`** |

## Blocker Inventory

| Blocker | Class | Notes |
|---------|--------|--------|
| **In-app** synthesis + **in-app** playback not executed | **Proof-only** | Follow-up B closed **API + file** evidence only; plan’s **A3–A5 / A7** UI path still open for a human pass. |
| No screenshot | **Proof-only** | Written note + `MainWindowTitle` only. |
| Optional engines failing to init in full probe | **Environment / dep surface** | Unchanged from initial session; health still **`engines_ready: true`**. |

No **product** blocker was identified for Piper synthesis (post-consent), API health, or WinUI process launch for the exercised steps.

## Verdict

**PARTIAL**

Rationale: **Follow-up B** adds **stronger** evidence than the initial pass alone: **granted consent** → **`POST /api/voice/synthesize`** with **`routed_engine: piper`** and the **exact phrase** → **169004**-byte **RIFF/WAVE** on disk with **non-trivial RMS**, plus a **second** WinUI launch with **`VoiceStudio Quantum+`** window title. **Still not FULL PASS** under the plan’s strict rule because **in-app** panel navigation, phrase entry, output path from UI, and **in-app playback heard** were **not** operator-attested here. **Not FAIL** — control plane, consent policy, Piper routing, and file shape are consistent with a healthy dev setup.

---

## Environment snapshot (session metadata)

| Item | Value |
|------|--------|
| OS | Windows (`[System.Environment]::OSVersion.Version` → `10.0.26200.0`) |
| dotnet | `8.0.420` |
| Python (.venv) | `3.11.9` (`E:\VoiceStudio\.venv\Scripts\python.exe`) |
| Backend port | `8000` |
