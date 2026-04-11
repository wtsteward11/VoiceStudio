# Execution Row: GAP-060 — Model Version Provenance on Outputs

**Lane ID:** GOV-VOICESTUDIO-GAP060-MODEL-PROVENANCE-01  
**Gap:** GAP-060 (Security — model provenance; depends on GAP-059 trust evidence)  
**Status:** CLOSED  
**Date frozen:** 2026-04-11  
**Date closed:** 2026-04-11  
**Owner Role:** Core Platform (Role 4)  
**Validator:** Overseer (Role 0) + Skeptical Validator  
**Predecessor:** GAP-059 closed (Trust audit trail)

---

## Context

GAP-059 provides joinable trust events (`artifact_id`, `correlation_id`). Outputs still lack **structured engine/model/version** identifiers in the artifact registry. This lane adds a single canonical **model provenance** payload on STS-transformed artifacts so trust evidence and production metadata align.

---

## Hard IN (Scope)

1. `backend/services/model_provenance_service.py` — `ModelProvenanceRecord`, `ModelProvenanceService.build` / `attach`, `get_model_provenance_service()`.
2. `backend/services/audio_artifacts/registry_db.py` — `AudioRegistryDB.update_metadata` (merge into `metadata_json`).
3. `backend/services/speech_to_speech_service.py` — after successful `create_audio_artifact_from_file`, call provenance `build` + `attach` for STS outputs only.
4. Tests: `tests/unit/backend/services/test_model_provenance_service.py` (8+).
5. Proof matrix: build, targeted pytest + trust-stack regression cohort, full App.Tests, creep, empty-catch, `verify.ps1 -Quick`, `run_verification.py`.
6. Governance: closure report, tracker, CANONICAL_REGISTRY, STATE, `openmemory.md`.

## Hard OUT

- No changes to `artifact_provenance.py` / file sidecar writer.
- No provenance query API, SIEM, or audit analytics UI.
- No RBAC expansion; no `IBackendClient` creep.
- No historical metadata backfill for pre-lane artifacts.
- No broad synthesis-path wiring beyond the frozen row (STS only).

---

## Authority Model

- **Single authority:** `ModelProvenanceService` for structured `model_provenance` under `AudioArtifact.metadata`.
- **Storage:** `audio_artifacts.metadata_json` key `model_provenance` (JSON object).
- **Best-effort:** attach failures → `logger.warning`; **never** fail STS conversion.
- **Correlation:** `artifact_id` and `correlation_id` must match GAP-059 `TrustAuditEvent` fields for the same successful conversion.

---

## “Model” definition (this lane)

| Field | Source |
|-------|--------|
| `engine_id` | Canonical engine id (STS path: `"rvc"`) |
| `engine_version` | Engine manifest `version` |
| `model_name` | Engine manifest `name` |
| `model_family` | Engine manifest `venv_family` |
| `manifest_hash` | **Out of scope** for this lane (optional future) |

---

## Acceptance Contract

- [x] STS success path writes `artifact.metadata["model_provenance"]` with `engine_id`, `engine_version`, `artifact_id`, `correlation_id` (when provided), `is_transformed`, `transformation_type`, `recorded_at`.
- [x] `model_provenance.artifact_id` equals trust audit `artifact_id` for the same request.
- [x] `model_provenance.correlation_id` equals trust audit `correlation_id` when both present.
- [x] Manifest unavailable: `engine_id` still set; version/name/family degrade gracefully (no crash).
- [x] Provenance attach failure does not fail conversion response.
- [x] No parallel provenance authorities for this payload shape.
- [x] Targeted + regression + full tests pass; creep + empty-catch + Quick pass.
- [x] Governance surfaces closed.

---

## Proof Matrix (fill on close)

| Check | Result |
|-------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| `pytest tests/unit/backend/services/test_model_provenance_service.py` | 9 PASS |
| Trust-stack regression pytest cohort | 23 PASS |
| `pytest tests/unit/backend/services/test_speech_to_speech_service.py` | 10 PASS |
| Full `VoiceStudio.App.Tests` | 3338 PASS / 274 skipped |
| `check_ibackendclient_creep.py` | PASS |
| `check_empty_catches.py` | PASS |
| `verify.ps1 -Quick` | PASS `artifacts/verify/20260410_215817/` |
| `run_verification.py` | PASS (after commit; **completion_guard** requires committed markers) |

---

## Rollback

Remove `model_provenance_service.py`; remove `update_metadata` and revert STS wiring; delete new tests and governance addenda.
