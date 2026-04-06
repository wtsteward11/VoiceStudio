# GOV-VOICESTUDIO-GAP054-SSML-CAPABILITY-DETECT-STRIP-WARN-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP054_SSML_CAPABILITY_DETECT_STRIP_WARN_01`  
**Status:** **Closed** (2026-04-06)  
**Tracker:** [GAP-054](PROFESSIONAL_GAP_TRACKER.md)  
**Lane type:** runtime-affecting (synthesis request path)

## Problem statement

SSML handling is split between the SSML preview route (text extraction / NLP) and the main synthesis path, with **no single authority** that decides per engine whether SSML is preserved, stripped with warning, or rejected. Manifest `contract.input.supports_ssml_tags` exists but is **not enforced** on the canonical synthesis path. Clients cannot rely on structured diagnostics when markup is altered.

## Frozen architecture decisions

1. **Authority:** `backend/services/ssml_capability_resolver.py` is the **only** policy module for synthesis-path SSML (detect → resolve capability → apply action → diagnostics).
2. **Capability classes (normalized):** `supports_ssml`, `plain_text_only`, `supports_subset`, `unknown` — derived from manifest `contract.input` (`supports_ssml_tags`, optional `ssml_capability` = `subset`).
3. **Actions:** `preserved`, `stripped_warned`, `rejected` (rejected surfaces as HTTP **422** via `ServiceError`, not success body).
4. **Unknown engines:** When SSML markup is detected and manifest does not declare SSML support → **strip + warn** (conservative).
5. **Malformed SSML:** When SSML-like markup is detected but XML parse fails after `<speak>` normalization → **reject** (422).
6. **Plain text:** No SSML hint → **no policy branch**; `ssml_handling` omitted on `VoiceSynthesizeResponse` (optional field).
7. **Boundary:** No new SSML-specific methods on `IBackendClient`; diagnostics ride on existing synthesis response JSON.

## Acceptance contract (all required)

- [x] `SynthesisService.synthesize` applies policy **before** NLP preprocess; preserved SSML skips `preprocess_for_tts` on that input.
- [x] `POST /api/voice/synthesize` applies the **same** policy (shared resolver) before NLP preprocess.
- [x] Success responses include `ssml_handling` when SSML was detected or transformed (machine-readable `capability_class`, `action`, `warnings`).
- [x] `plain_text_only` / `unknown` + SSML → `stripped_warned` + warnings; `supports_ssml` → `preserved` + optional `ssml` kwargs for engines that support it.
- [x] `supports_subset` normalizes unsupported tags to text with warnings.
- [x] SSML preview delegates text payload to canonical synthesis with **raw** `content` (resolver runs in service); preview response exposes aligned diagnostics fields.
- [x] Backend unit tests + SSML route tests + C# deserialization / `IBackendClient` no-creep check.
- [x] Closure matrix + proof — [closure](../reports/verification/VOICESTUDIO_GAP054_SSML_CAPABILITY_DETECT_STRIP_WARN_LANE_CLOSURE_2026-04-06.md).

## Allowlist

`backend/services/ssml_capability_resolver.py` (new), `backend/services/synthesis_service.py`, `backend/api/routes/voice/synthesis.py`, `backend/api/models_additional.py`, `backend/api/routes/ssml.py`, `src/VoiceStudio.App/Core/Models/VoiceSynthesisRequest.cs` (SSML diagnostics on response type), `VoiceSynthesisService.cs` (pass-through only if needed), tests, tracker, registry, STATE, closure report, this row.

## Hard OUT

SSML editor UX redesign; manifest schema overhaul; broad synthesis refactor; new `IBackendClient` SSML APIs; startup changes; wizard work; benchmark UI.

## Rollback

Revert scoped commit(s). Omission of `ssml_handling` in JSON restores prior client ignore-path.

## Changelog

- **2026-04-06:** Row frozen; implementation and closure.
