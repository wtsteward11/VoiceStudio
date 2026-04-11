# GOV-VOICESTUDIO-GAP049-LONG-FORM-VOICE-CONSISTENCY-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP049-LONG-FORM-VOICE-CONSISTENCY-01 |
| **GAP** | GAP-049 (Long-form voice synthesis consistency) |
| **Status** | **Closed** — [VOICESTUDIO_GAP049_LONG_FORM_VOICE_CONSISTENCY_LANE_CLOSURE_2026-04-10.md](../reports/verification/VOICESTUDIO_GAP049_LONG_FORM_VOICE_CONSISTENCY_LANE_CLOSURE_2026-04-10.md) |
| **Phase** | Bounded execution row — `SynthesisService` / `VoiceSynthesisView` |
| **Role** | Engine Engineer + UI Engineer (split per authority map) |

## §1 Objective (frozen)

Deliver a **canonical long-form synthesis path**: sentence-boundary chunking, identical synthesis settings per chunk, sequential per-chunk calls to `SynthesisService.synthesize`, ordered `numpy` concatenation into one derived artifact, and **honest partial-failure** reporting — without new engine contracts, narration subsystems, or ViewModel-local orchestration.

## §2 Hard IN

- `SynthesisService.synthesize_long_form` owns chunking + orchestration + assembly.
- `TextPreprocessor.sentence_segmentation` (via `backend.nlp.text_processing`) for chunk boundaries; overflow sentences split by words only when a single sentence exceeds `chunk_size_chars`.
- `VoiceSynthesizeRequest` per chunk carries the **same** engine/profile/language/emotion/consent and prosody fields (`speed`, `pitch`, `stability`, `clarity`, `temperature`, `enhance_quality`) for every chunk.
- Sequential chunk synthesis only; `np.concatenate` then `create_audio_artifact_from_wav_array` for the merged output.
- New thin route `POST /api/voice/synthesize/long-form` delegating to the service.
- WinUI: `IVoiceSynthesisService.SynthesizeLongFormAsync` + `BackendClient` POST; `VoiceSynthesisView` **Long-form mode** checkbox + progress text; `AutomationId`s registered.
- Tests: Python unit tests for chunking + long-form service; `Gap049Tests` source scans; `VoiceSynthesisViewModelSeamTests` long-form cases.
- Verification: build, targeted filters, full App.Tests, `check_ibackendclient_creep.py`, `verify.ps1 -Quick`.

## §3 Hard OUT

- Streaming narration platform, audiobook/scene frameworks, cross-session chunk history.
- New engine protocol/router edits, parallel chunk workers, live waveform editor.
- Route-local or panel-local synthesis orchestration forks (orchestration stays in `SynthesisService`).
- Per-chunk WebSocket progress (single POST response; bounded slice).

## §4 Authority map

| Concern | Owner |
|--------|--------|
| Chunking + merge + partial-failure contract | `backend.services.synthesis_service.SynthesisService` |
| Per-chunk synthesis | `SynthesisService.synthesize` (existing) |
| Artifact write | `create_audio_artifact_from_wav_array` |
| HTTP surface | `backend/api/routes/voice/synthesis.py` (thin) |
| Client transport | `IBackendClient` / `BackendClient` |
| UI affordance + state | `VoiceSynthesisView` / `VoiceSynthesisViewModel` |
| Emotion preset post-apply (GAP-050) | `VoiceSynthesisService` on **final** merged `audio_id` (same as single-shot) |

## §5 Acceptance criteria

- [x] `SynthesisService.synthesize_long_form` exists; chunking uses sentence segmentation (service layer), not raw word-split as primary policy.
- [x] Settings envelope identical across chunk `VoiceSynthesizeRequest` instances (verified in tests).
- [x] `LongFormSynthesisResponse` exposes `partial_failure`, `failed_chunks`, `chunks_total`, `chunks_succeeded`, merged `audio_id` / `audio_url` / `duration` / `quality_score`.
- [x] `ServiceError(500)` when zero chunks succeed.
- [x] Thin route + client method; no duplicate orchestration in ViewModel.
- [x] UI: `UseLongForm`, `IsLongFormRunning`, `LongFormProgressText`; partial-failure warning toast.
- [x] Governance: tracker, CANONICAL_REGISTRY, STATE, openmemory, closure report.

## §6 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
python -m pytest tests/unit/backend/services/test_synthesis_service_long_form.py -v
python scripts/ci/check_ibackendclient_creep.py
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap049Tests|FullyQualifiedName~VoiceSynthesisViewModelSeamTests"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
```

## §7 Risk register

| Risk | Mitigation |
|------|------------|
| Sample-rate mismatch between chunks | Resample to first chunk SR via `resample_audio` (same as text-speech merge) |
| Huge single sentence | Word-split overflow only for that sentence |

## §8 Rollback

Revert `synthesis_service.py` long-form helpers, route registration, client DTOs/methods, VM/XAML, tests, and docs; no DB migration.

## §9 Proof Reference (inherited — no re-run required)

Proof recorded in [VOICESTUDIO_GAP049_LONG_FORM_VOICE_CONSISTENCY_LANE_CLOSURE_2026-04-10.md](../reports/verification/VOICESTUDIO_GAP049_LONG_FORM_VOICE_CONSISTENCY_LANE_CLOSURE_2026-04-10.md):

| Artifact | Value |
|----------|--------|
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → exit **0** |
| Python | `pytest tests/unit/backend/services/test_synthesis_service_long_form.py` → **7** PASS |
| Targeted C# | Filter `Gap049Tests|VoiceSynthesisViewModelSeamTests` → **11** PASS |
| Full App.Tests | **3284** PASS / **274** skipped |
| Creep gate | `check_ibackendclient_creep.py` → exit **0** |
| Quick verify | `artifacts/verify/20260409_222636/` — PASS |

**Known flake (preserved):** First full-suite run timed out on `TranscribeViewModelInlineEditTests.RangeApply_AfterFillerCleanup_PublishesSingleCoherenceEvent`; re-run passed — non-deterministic, not attributable to GAP-049.

## §10 Changelog

| Date | Change |
|------|--------|
| 2026-04-10 | §5 acceptance criteria checked; §9 proof reference added (governance seal). |
