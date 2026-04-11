# GOV-VOICESTUDIO-GAP056-TRANSFORMED-AUDIO-DISCLOSURE-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP056-TRANSFORMED-AUDIO-DISCLOSURE-01 |
| **GAP** | GAP-056 (bounded STS transformed-output disclosure slice) |
| **Status** | **Closed** 2026-04-10 — [closure](../reports/verification/VOICESTUDIO_GAP056_TRANSFORMED_AUDIO_DISCLOSURE_LANE_CLOSURE_2026-04-10.md) |
| **Phase** | `SpeechToSpeechResponse` + `SpeechToSpeechView` disclosure |
| **Role** | Core Platform + UI Engineer |

## §1 Objective (frozen)

Make STS-derived output **explicitly identifiable and disclosable**: extend `SpeechToSpeechResponse` with transformed-output metadata and user-facing disclosure text; pass **source linkage** into artifact registration; surface disclosure in the STS panel. **Watermark embedding in audio samples is out of scope** (deferred).

## §2 Hard IN

- `SpeechToSpeechResponse` (Python + C#): `is_transformed`, `transformation_type`, `source_audio_id`, `disclosure_text`.
- `SpeechToSpeechService.convert`: populate all four; pass `source=request.source_audio_id` to `create_audio_artifact_from_file`.
- `SpeechToSpeechViewModel`: `OutputIsTransformed`, `OutputDisclosureText`, `HasOutputDisclosure`; clear on new conversion start.
- `SpeechToSpeechView`: `SpeechToSpeechView_DisclosureText` TextBlock.
- Tests: Python disclosure assertions; `Gap056Tests`; `SpeechToSpeechDisclosureSeamTests`.

## §3 Hard OUT

- **Audio watermark embedding** in the STS pipeline (`WatermarkingService` / numpy load-save loop).
- Export-pipeline-wide watermarking beyond STS.
- RBAC, storage-engine rewrite, cloning/synthesis retrofit.
- New consent infrastructure (GAP-055 owns consent gate).

## §4 Authority map

| Concern | Owner |
|--------|--------|
| Response shape + service population | `SpeechToSpeechService.convert` |
| Artifact source linkage | `create_audio_artifact_from_file(..., source=...)` / store |
| Client DTO | `SpeechToSpeechModels.cs` |
| UI | `SpeechToSpeechView` / `SpeechToSpeechViewModel` |

## §5 Acceptance criteria

- [x] `SpeechToSpeechResponse` carries `is_transformed`, `transformation_type`, `source_audio_id`, `disclosure_text`
- [x] Service is the single authority; populates all four fields at return
- [x] `source_audio_id` flows into artifact registry metadata via `source=` parameter
- [x] Route unchanged — thin
- [x] ViewModel exposes `OutputIsTransformed`, `OutputDisclosureText`, `HasOutputDisclosure`; cleared at start of conversion
- [x] UI `SpeechToSpeechView_DisclosureText` shows disclosure after successful conversion
- [x] Non-STS code paths unchanged
- [x] Python tests: disclosure fields on success response
- [x] C#: `Gap056Tests` + `SpeechToSpeechDisclosureSeamTests` + related STS seam filters pass
- [x] Full App.Tests pass; creep pass; Quick pass
- [x] Watermarking deferred and documented
- [x] Governance surfaces synchronized at close time

## §6 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
python -m pytest tests/unit/backend/services/test_speech_to_speech_service.py -v
python scripts/ci/check_ibackendclient_creep.py
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap056Tests|FullyQualifiedName~SpeechToSpeechDisclosureSeamTests|FullyQualifiedName~SpeechToSpeechConsentSeamTests|FullyQualifiedName~SpeechToSpeechViewModelSeamTests"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
```

## §7 Risk register

| Risk | Mitigation |
|------|------------|
| Larger JSON response | Bounded four fields; defaults stable |
| Watermark expectation | §3 Hard OUT; defer to future lane |

## §8 Rollback

Revert model fields, service return + `source=`, VM/XAML, tests, docs; no DB migration.

## §9 Proof Reference

[VOICESTUDIO_GAP056_TRANSFORMED_AUDIO_DISCLOSURE_LANE_CLOSURE_2026-04-10.md](../reports/verification/VOICESTUDIO_GAP056_TRANSFORMED_AUDIO_DISCLOSURE_LANE_CLOSURE_2026-04-10.md)

## §10 Changelog

| Date | Change |
|------|--------|
| 2026-04-10 | Execution row frozen. |
| 2026-04-10 | §5 sealed **Closed**; closure report linked. |
