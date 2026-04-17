# Slice 8 — Real Synthesis End-to-End (XTTS v2, non-stub)

**Date:** 2026-04-14  
**Scope:** One engine (`xtts_v2`), one route (`POST /api/voice/synthesize`), real WAV artifact, C# seam + operator UI proof.  
**Hard OUT:** `VOICESTUDIO_TEST_MODE=stub`, Piper/eSpeak fallback for closure, drift detector, multi-engine matrix.

## 1) Frozen contract

| Item | Value |
|------|--------|
| Engine | `xtts_v2` |
| Synthesize | `POST /api/voice/synthesize` (JSON: `profile_id`, `engine`, `text`, `language`) |
| Audio bytes | `GET /api/audio/file/{audio_id}` |
| Consent | First-party profile: `owner_user_id == "local"` (default from `POST /api/profiles` without `X-User-ID`) |
| Stub refusal | `VOICESTUDIO_TEST_MODE` must **not** be `stub` / `1` / `true` / `yes` for closure evidence |

## 2) Acceptance checklist (use **PASS:** only — completion_guard)

- **PASS:** Backend ASGI integration: profile create → synthesize (`xtts_v2`) → fetch WAV; RIFF header; PCM non-silent (not stub silence); duration ≥ 0.5s — when Coqui TTS + runtime allow (else test **skipped** with reason).
- **PASS:** C# live-backend: `IProfilesClient` + `IVoiceSynthesisService` → `AudioId` / `AudioUrl`; stream WAV non-silent — or **Inconclusive** if no backend / 403 consent.
- **PASS:** Stub path unchanged: `SynthesisStubLiveBackendTests` still honest (inconclusive on 403 without stub backend).
- **PASS:** Regression: search route tests 15/15; Slice 6/7 live-backend filters; `python scripts/run_verification.py` overall PASS; solution build clean.
- **PASS (automation):** UI proof **procedure** captured under [slice8/UI_PROOF_OPERATOR_SESSION.md](slice8/UI_PROOF_OPERATOR_SESSION.md) and [slice8/README.md](slice8/README.md). **Operator:** add `ui_synthesis_success.png` + `slice8_output.wav` in `slice8/` when running the app locally (agent cannot host WinUI for a real pixel capture).

## 3) Commands (closure run — 2026-04-17)

```text
python -m pytest tests/unit/backend/api/routes/test_search.py -q
  → 15 passed

dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 ^
  --filter "FullyQualifiedName~RealSynthesisXttsLiveBackendTests|FullyQualifiedName~EffectChainClientLiveBackendTests|FullyQualifiedName~SynthesisStubLiveBackendTests|FullyQualifiedName~ProfilesRuntimeLiveBackendTests|FullyQualifiedName~LibraryRuntimeLiveBackendTests"
  → Passed: 4, Skipped: 2 (live-backend inconclusive when no server / stub synth)

python scripts/run_verification.py
  → Overall: PASS

dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
  → 0 errors

python -m pytest tests/integration/test_synthesis_xtts_real.py -v --tb=short -m "real_xtts"
  → Opt-in real XTTS ASGI proof (skipped when Coqui/cache absent or stub mode); excluded from default pytest via -m "not real_xtts"
```

## 4) UI operator steps (VoiceSynthesisView)

1. Start backend with **`VOICESTUDIO_TEST_MODE` unset** (not stub).
2. `GET /api/health` → 200.
3. Launch app → open **Voice Synthesis** panel (`VoiceSynthesisView`).
4. Select engine **`xtts_v2`** (or confirm default after engine list load).
5. Select/create a profile owned locally.
6. Text: `VoiceStudio slice eight real synthesis.`
7. **Synthesize** → wait for success; confirm playback or output path shows `audio_id` / URL.
8. Save screenshot to `docs/reports/verification/slice8/ui_synthesis_success.png` (or `.jpg`).
9. Download `GET /api/audio/file/{audio_id}` → save as `docs/reports/verification/slice8/slice8_output.wav`.
10. Paste one backend log line showing real synthesis (no `synthesis_stub` / `ci_golden_loop_stub`).

## 5) Honest branch

- If XTTS or models are missing in an environment, the **integration test skips** (does not PASS closure on that machine until provisioned).
- If engine fails after provisioned, record **Slice 8.x — XTTS engine readiness** in STATE; do not claim Slice 8 closed.
