# GOV-VOICESTUDIO-GAP056-STS-DURABLE-MARKING-02

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP056-STS-DURABLE-MARKING-02 |
| **GAP** | GAP-056 addendum (bounded STS durable transformed-audio marking via provenance + registry metadata) |
| **Status** | **Closed** 2026-04-10 — [closure](../reports/verification/VOICESTUDIO_GAP056_STS_DURABLE_MARKING_LANE_CLOSURE_2026-04-10.md) |
| **Phase** | Sidecar + registry + `GET /api/audio/{audio_id}/marking` + export headers + WinUI badge |
| **Role** | Core Platform + UI Engineer |

## §1 Objective (frozen)

Deliver **Option B**: attach canonical **provenance + registry metadata** for STS-transformed artifacts (`is_transformed`, `transformation_type`, source linkage), expose marking status via a thin GET endpoint, inject export response headers when metadata indicates transformation, and surface a **Durably marked** badge in the STS panel after successful convert. **Sample-level watermark embedding (INVISIBLE/AUDIBLE) is explicitly not claimed** in this lane — `WatermarkingService.embed_watermark` without `detect_watermark` is out of scope.

## §2 Hard IN

- `write_provenance_sidecar`: optional `is_transformed`, `transformation_type`, `source_reference_id`.
- `record_artifact_provenance_and_usage` / `_do_provenance`: `transformation_meta` → sidecar writer.
- `store_from_file` / `create_audio_artifact_from_file`: `is_transformed`, `transformation_type`; merged into registry `metadata`; passed to provenance.
- `SpeechToSpeechService._run_convert`: `is_transformed=True`, `transformation_type="speech_to_speech"`.
- `StsMarkingStatus` (Pydantic + C#); `GET /api/audio/{audio_id}/marking`.
- Export: `X-VoiceStudio-IsTransformed` / `X-VoiceStudio-TransformationType` when registry metadata indicates transformation.
- `IBackendClient.GetStsMarkingAsync`, `ISpeechToSpeechService.GetMarkingAsync`; `SpeechToSpeechViewModel` marking observables + non-blocking lookup; `SpeechToSpeechView_MarkingBadge`.
- Tests: Python (sidecar, registry, endpoint, export, STS call site); C# `Gap057Tests` + `SpeechToSpeechMarkingSeamTests`.

## §3 Hard OUT

- **Audio sample embedding** via `WatermarkingService.embed_watermark` (no invisible/audible watermark in this lane).
- Synthesis-route / cloning-wide retrofit beyond STS artifact path.
- RBAC / auth changes.
- Changes to `app/core/security/watermarking.py` raising `RuntimeError` by design.

## §4 Authority map

| Concern | Owner |
|--------|--------|
| Sidecar + provenance | `security_service.write_provenance_sidecar`, `artifact_provenance` |
| Registry metadata | `AudioArtifactStore.store_from_file`, `create_audio_artifact_from_file` |
| STS pipeline | `SpeechToSpeechService._run_convert` |
| HTTP surface | `audio.py` (marking + export headers) |
| Client + UI | `BackendClient`, `SpeechToSpeechService`, `SpeechToSpeechViewModel`, `SpeechToSpeechView` |

## §5 Acceptance criteria

- [x] `write_provenance_sidecar` writes `is_transformed` / `transformation_type` / `source_reference_id` when requested
- [x] STS outputs register with `is_transformed` + `transformation_type` in artifact metadata and provenance path
- [x] `GET /api/audio/{audio_id}/marking` returns `StsMarkingStatus` consistent with registry metadata
- [x] `POST /api/audio/export` adds transformation headers when artifact metadata indicates transformed audio
- [x] C# client + STS seam call `GetMarkingAsync` / `GetStsMarkingAsync`; UI shows badge when marking confirms transformed
- [x] Python + C# tests per verification matrix; creep + Quick pass
- [x] Watermark **sample** embedding deferred and documented (§3 / §7)
- [x] Governance surfaces updated at close time

## §6 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
python -m pytest tests/unit/backend/services/test_sts_durable_marking.py -v
python -m pytest tests/unit/backend/services/test_speech_to_speech_service.py -v
python scripts/ci/check_ibackendclient_creep.py
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 `
  --filter "FullyQualifiedName~Gap057Tests|FullyQualifiedName~SpeechToSpeechMarkingSeamTests|FullyQualifiedName~Gap056Tests|FullyQualifiedName~SpeechToSpeechDisclosureSeamTests"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
```

## §7 Risk register

| Risk | Mitigation |
|------|------------|
| Confusion with sample watermarking | §1/§3 explicit; no `embed_watermark` in STS path |
| Registry miss on export `source` | Best-effort headers; only when `get(audio_id)` succeeds |

## §8 Rollback

Revert sidecar/registry/API/client/VM/XAML/tests/docs; no DB migration (metadata in existing JSON column).

## §9 Proof Reference

[VOICESTUDIO_GAP056_STS_DURABLE_MARKING_LANE_CLOSURE_2026-04-10.md](../reports/verification/VOICESTUDIO_GAP056_STS_DURABLE_MARKING_LANE_CLOSURE_2026-04-10.md)

## §10 Changelog

| Date | Change |
|------|--------|
| 2026-04-10 | Execution row frozen (implementation start). |
| 2026-04-10 | §5 sealed **Closed**; closure report linked. |
