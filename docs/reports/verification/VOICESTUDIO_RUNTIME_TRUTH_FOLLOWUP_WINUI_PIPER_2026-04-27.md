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

**Note:** `POST /api/voice/synthesize` remains gated by consent policy in normal mode; this follow-up did not substitute HTTP synthesis for the required in-app UI proof.

## In-App Playback

| Item | Value |
|------|--------|
| **Playback heard in-app** | Not tested — no in-app generated clip from this session. |
| **Limitation** | Agent session did not drive WinUI controls for synthesize/play; audible confirmation would require operator at the machine. |
| **Proof class** | **Partial runtime proof** — backend + Piper preflight + WinUI process launch are evidenced; full product path (UI phrase → file → play) is **not** closed here. |

## Artifact Validation

No application-emitted WAV from this session’s in-app flow was produced or located.

**Optional reference (models, not synthesis output):** Piper ONNX assets on disk (`en_US-amy-medium.onnx` and `.json`) are cited from `engine_readiness_probe.json` preflight; no RIFF/WAV header or RMS check was applied to a new synthesis file (none captured).

## Blocker Inventory

| Blocker | Class | Notes |
|---------|--------|--------|
| In-app synthesis + playback not executed | **Proof-only** | Automation/operator gap; not a backend compile failure. |
| No screenshot / no UI text capture | **Proof-only** | Could be added in a follow-on operator checklist. |
| Optional engines failing to init in full probe | **Environment / dep surface** | Expected on partial dev installs; health still reported `engines_ready: true`. |

No **product** blocker was identified for Piper readiness or API health for this commit.

## Verdict

**PARTIAL**

Rationale: **WinUI** executable **did** start as a live process; **backend** health returned **`engines_ready: true`** with commit aligned to **HEAD**; **Piper** full-router preflight is **OK**. **In-app** synthesis with the specified phrase and **in-app playback** were **not** proven in this session → not **FULL PASS**. No **FAIL** — Piper and control-plane signals were healthy for the exercised steps.

---

## Environment snapshot (session metadata)

| Item | Value |
|------|--------|
| OS | Windows (`[System.Environment]::OSVersion.Version` → `10.0.26200.0`) |
| dotnet | `8.0.420` |
| Python (.venv) | `3.11.9` (`E:\VoiceStudio\.venv\Scripts\python.exe`) |
| Backend port | `8000` |
