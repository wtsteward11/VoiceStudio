# VOICESTUDIO — GAP-056 STS Durable Marking (slice 2) — Lane Closure

**Lane ID:** GOV-VOICESTUDIO-GAP056-STS-DURABLE-MARKING-02  
**Date:** 2026-04-10  
**Execution row:** [GOV_VOICESTUDIO_GAP056_STS_DURABLE_MARKING_02_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP056_STS_DURABLE_MARKING_02_EXECUTION_ROW.md)

## Summary

Bounded **Option B** implementation: durable **metadata** marking for STS outputs via provenance sidecar fields + artifact registry metadata; thin `GET /api/audio/{audio_id}/marking`; export response headers `X-VoiceStudio-IsTransformed` / `X-VoiceStudio-TransformationType`; WinUI **Durably marked** badge after successful convert. **No** sample-level `WatermarkingService.embed_watermark` in this lane.

## Proof — verification matrix

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors) |
| `python -m pytest tests/unit/backend/services/test_sts_durable_marking.py` | 6 PASS |
| `python -m pytest tests/unit/backend/services/test_speech_to_speech_service.py` | 10 PASS |
| `python scripts/ci/check_ibackendclient_creep.py` | PASS |
| `dotnet test ... --filter "Gap057Tests|SpeechToSpeechMarkingSeamTests|Gap056Tests|SpeechToSpeechDisclosureSeamTests"` | 21 PASS |
| `dotnet test src/VoiceStudio.App.Tests/...` (full) | 3323 PASS / 274 skipped |
| `python scripts/check_empty_catches.py` | PASS |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260410_085615/` |

## Key artifacts

- Python: `write_provenance_sidecar` enrichment; `record_artifact_provenance_and_usage(transformation_meta=...)`; `store_from_file` / `create_audio_artifact_from_file`; `SpeechToSpeechService` passes `is_transformed` + `transformation_type`.
- API: `StsMarkingStatus`; `GET /api/audio/{audio_id}/marking`; export header injection in `POST /api/audio/export`.
- C#: `StsMarkingModels.cs`; `GetStsMarkingAsync` / `GetMarkingAsync`; `SpeechToSpeechViewModel` marking fields; `SpeechToSpeechView_MarkingBadge`.
- Tests: `test_sts_durable_marking.py`; `Gap057Tests.cs`; `SpeechToSpeechMarkingSeamTests.cs`.

## Deferred (explicit)

- Invisible/audible **sample** watermark embedding / detection parity — not claimed; remains future lane under umbrella GAP-056 / export pipeline.

## Governance

- `docs/developer/AUTOMATION_ID_REGISTRY.md` — `SpeechToSpeechView_MarkingBadge`
- Tracker / CANONICAL_REGISTRY / `.cursor/STATE.md` / `openmemory.md` — synchronized at closure.
