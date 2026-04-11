# GOV-VOICESTUDIO-GAP055-VOICE-CONSENT-PROVENANCE-GATE-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP055-VOICE-CONSENT-PROVENANCE-GATE-01 |
| **GAP** | GAP-055 (STS consent / provenance gate — bounded) |
| **Status** | **Closed** — [VOICESTUDIO_GAP055_VOICE_CONSENT_PROVENANCE_GATE_LANE_CLOSURE_2026-04-10.md](../reports/verification/VOICESTUDIO_GAP055_VOICE_CONSENT_PROVENANCE_GATE_LANE_CLOSURE_2026-04-10.md) |
| **Phase** | Bounded execution row — `SpeechToSpeechService.convert` + `SpeechToSpeechView` |
| **Role** | Core Platform + UI Engineer |

## §1 Objective (frozen)

Introduce an **enforceable consent gate** for speech-to-speech (STS) voice identity transformation, aligned with cloning/synthesis patterns: explicit `consent_acknowledged`, optional validated `consent_id`, and **best-effort consent audit** after successful conversion. Route remains thin; enforcement lives in `SpeechToSpeechService.convert`.

## §2 Hard IN

- `SpeechToSpeechRequest` (Python + C#): `consent_acknowledged`, optional `consent_id`.
- `SpeechToSpeechService.convert`: reject `consent_acknowledged=False` with `ServiceError(400, CONSENT_REQUIRED)`; validate `consent_id` when provided via `SecurityService.consent`; consent gate **before** source resolution.
- After successful artifact registration: best-effort **consent audit** (structured log; must not block response). File provenance continues via existing `create_audio_artifact_from_file` / store pipeline.
- WinUI: `ConsentAcknowledged` + `CanConvert` guard; CheckBox with `SpeechToSpeechView_ConsentCheckBox`.
- Tests: Python consent tests; `Gap055Tests`; `SpeechToSpeechConsentSeamTests`.

## §3 Hard OUT

- New consent storage engine, RBAC redesign, watermarking changes, first-run wizard changes.
- Reopening cloning or synthesis consent design beyond alignment notes.

## §4 Authority map

| Concern | Owner |
|--------|--------|
| STS orchestration + consent gate | `backend.services.speech_to_speech_service.SpeechToSpeechService` |
| Consent records | `SecurityService.consent` / `ConsentManager` |
| HTTP surface | `backend/api/routes/voice/speech_to_speech.py` (thin; unchanged logic) |
| Client | `SpeechToSpeechRequest` DTO + `SpeechToSpeechViewModel` |
| UI | `SpeechToSpeechView` |

## §5 Acceptance criteria

- [x] `SpeechToSpeechRequest.consent_acknowledged` required `True`; service raises `ServiceError(400, CONSENT_REQUIRED)` when `False`
- [x] Optional `consent_id` validated as GRANTED + unexpired when provided; `ServiceError(403)` on failure
- [x] Consent audit recorded best-effort after successful conversion; failure logged not raised (artifact provenance unchanged via store)
- [x] Route stays thin — consent enforcement stays in service
- [x] `SpeechToSpeechViewModel.CanConvert` gated on `ConsentAcknowledged`; request carries the field
- [x] UI CheckBox with `AutomationId: SpeechToSpeechView_ConsentCheckBox`; default unchecked
- [x] Python tests: consent absent → 400, invalid consent_id → 403
- [x] C# tests: `Gap055Tests` source scans + `SpeechToSpeechConsentSeamTests` pass
- [x] Full `VoiceStudio.App.Tests` pass; creep gate pass; Quick pass
- [x] Governance surfaces synchronized at close time

## §6 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
python -m pytest tests/unit/backend/services/test_speech_to_speech_service.py -v
python scripts/ci/check_ibackendclient_creep.py
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap055Tests|FullyQualifiedName~SpeechToSpeechConsentSeamTests"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
```

## §7 Risk register

| Risk | Mitigation |
|------|------------|
| Breaking existing API clients | Default `consent_acknowledged=False` matches explicit UI gate; clients must send `true` |
| Duplicate provenance | Rely on store pipeline; GAP-055 adds consent audit log only |

## §8 Rollback

Revert model fields, service gate, VM/XAML, tests, docs; no DB migration.

## §9 Proof Reference

Recorded in [VOICESTUDIO_GAP055_VOICE_CONSENT_PROVENANCE_GATE_LANE_CLOSURE_2026-04-10.md](../reports/verification/VOICESTUDIO_GAP055_VOICE_CONSENT_PROVENANCE_GATE_LANE_CLOSURE_2026-04-10.md) §4.

## §10 Changelog

| Date | Change |
|------|--------|
| 2026-04-10 | Execution row frozen (implementation). |
| 2026-04-10 | Lane closed; §5 checked at seal time. |
