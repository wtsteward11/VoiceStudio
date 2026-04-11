# GOV-VOICESTUDIO-GAP051-SPEECH-TO-SPEECH-CONVERSION-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP051-SPEECH-TO-SPEECH-CONVERSION-01 |
| **GAP** | GAP-051 (Speech-to-speech conversion path — bounded) |
| **Status** | **Closed** — [VOICESTUDIO_GAP051_SPEECH_TO_SPEECH_CONVERSION_LANE_CLOSURE_2026-04-10.md](../reports/verification/VOICESTUDIO_GAP051_SPEECH_TO_SPEECH_CONVERSION_LANE_CLOSURE_2026-04-10.md) |
| **Phase** | Bounded execution row — `SpeechToSpeechService` / `SpeechToSpeechView` |
| **Role** | Engine Engineer + UI Engineer (split per authority map) |

## §1 Objective (frozen)

Deliver a **canonical batch speech-to-speech conversion path**: source audio by `audio_id`, target voice by `voice_profile_id`, orchestration in `SpeechToSpeechService`, RVC via `EngineService.get_rvc_engine().convert_voice`, output as registered audio artifact — **without** realtime microphone pipeline, WebSocket streaming, or new engine protocol.

## §2 Hard IN

- `SpeechToSpeechService.convert` owns validation, RVC invocation, artifact registration.
- Source: `source_audio_id` resolved via `AudioRegistry.get_path`.
- Target: `target_voice_profile_id` — RVC checkpoint via first `*.pth` under profile directory (else `None` / engine default with logged notice); reference audio path used only when required by future slices.
- `POST /api/voice/sts/convert` thin route; `IBackendClient.SynthesizeSpeechToSpeechAsync`; `ISpeechToSpeechService.ConvertSpeechAsync`.
- WinUI: `SpeechToSpeechView` — source audio id entry, target profile picker, convert, status, output link; `AutomationId`s registered.
- Tests: Python unit tests; `Gap051Tests`; `SpeechToSpeechViewModelSeamTests`.
- Verification: build, targeted filters, full App.Tests, creep, Quick.

## §3 Hard OUT

- Real-time / microphone / `RealTimeVoiceConverterView` scope.
- WebSocket or streaming STS.
- Fixing legacy `backend/api/routes/rvc.py` `engine_router` defect (separate lane).
- Dubbing, video voice replacement, speaker diarization.

## §4 Authority map

| Concern | Owner |
|--------|--------|
| STS orchestration + artifact | `backend.services.speech_to_speech_service.SpeechToSpeechService` |
| RVC inference | `app.core.engines.rvc_engine.RVCEngine.convert_voice` |
| HTTP surface | `backend/api/routes/voice/speech_to_speech.py` (thin) |
| Client transport | `IBackendClient` / `BackendClient` |
| UI | `SpeechToSpeechView` / `SpeechToSpeechViewModel` |

## §5 Acceptance criteria

- [x] `SpeechToSpeechService.convert` is sole orchestration authority; resolves source path and target RVC checkpoint.
- [x] `SpeechToSpeechRequest` / `SpeechToSpeechResponse` (Pydantic + C#) aligned with JSON names.
- [x] `POST /api/voice/sts/convert` + `SynthesizeSpeechToSpeechAsync` on `IBackendClient`; no duplicate orchestration in ViewModel.
- [x] UI: bounded panel with convert flow + `AutomationId`s in registry.
- [x] Tests: Python service tests + C# seam + source scans; creep gate passes.
- [x] Governance: closure report, tracker, CANONICAL_REGISTRY, STATE, openmemory.

## §6 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
python -m pytest tests/unit/backend/services/test_speech_to_speech_service.py -v
python scripts/ci/check_ibackendclient_creep.py
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap051Tests|FullyQualifiedName~SpeechToSpeechViewModelSeamTests"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
```

## §7 Risk register

| Risk | Mitigation |
|------|------------|
| No `.pth` under profile | Engine default model; log notice; document optional checkpoint layout |
| RVC blocking | `asyncio.to_thread` for `convert_voice` |

## §8 Rollback

Revert `speech_to_speech_service.py`, route, client DTOs/methods, VM/XAML, panel registration, tests, docs; no DB migration.

## §9 Proof Reference

Recorded in [VOICESTUDIO_GAP051_SPEECH_TO_SPEECH_CONVERSION_LANE_CLOSURE_2026-04-10.md](../reports/verification/VOICESTUDIO_GAP051_SPEECH_TO_SPEECH_CONVERSION_LANE_CLOSURE_2026-04-10.md) §4.

## §10 Changelog

| Date | Change |
|------|--------|
| 2026-04-10 | Lane closed; §5 checked at seal time. |
