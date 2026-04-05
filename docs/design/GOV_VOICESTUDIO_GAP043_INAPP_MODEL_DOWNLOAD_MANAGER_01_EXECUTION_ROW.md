# GOV-VOICESTUDIO-GAP043-INAPP-MODEL-DOWNLOAD-MANAGER-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP043-INAPP-MODEL-DOWNLOAD-MANAGER-01 |
| **GAP** | GAP-043 (in-app model download manager) |
| **Status** | **Closed** (2026-04-04) — canonical `download` job, verify-before-register, client + Model Manager UI |
| **Phase** | Professional Roadmap v3 — Phase 4 |
| **Role** | Core Platform + System Architect + UI Engineer |
| **Dependency** | Canonical jobs (`JobRepository`, `/api/jobs`), `ModelStorage` registration, optional `ModelRegistryService` |
| **Authority memo** | [GOV_VOICESTUDIO_GAP043_DOWNLOAD_AUTHORITY_DECISIONS.md](GOV_VOICESTUDIO_GAP043_DOWNLOAD_AUTHORITY_DECISIONS.md) |

## §1 Objective (frozen)

Deliver an **in-app model download manager** where:

- Every download is tracked as **one canonical job** with `job_type=download`.
- Progress and status are observable via **`/api/jobs`** (same UX surface as other jobs).
- Artifacts are **staged**, **verified** (checksum when provided), and only then **registered** in model storage + lifecycle registry.
- **No registry activation** and **no job completion** until verification succeeds.

## §2 Hard IN

- `JobType.DOWNLOAD` (`download`) in `backend/data/repositories/job_repository.py`.
- `backend/services/model_download_service.py` — orchestration: create/update job, stream download, checksum gate, register, complete/fail/cancel.
- `POST /api/models/download` — start download (returns `job_id`); **single-flight** per `(engine_id, model_name, version)` for active jobs.
- `GET /api/models/download/active-key` (optional) or enforcement inside start endpoint only — **idempotent reject** duplicate concurrent flight.
- Integration with **`POST /api/jobs/{id}/cancel`**, **`retry`**, **`resume`** for `download` jobs (see authority memo).
- C# `IModelManagerClient` / `ModelManagerClient` + `ModelManagerViewModel` commands: start, surface job id / progress hook, cancel/retry/resume via jobs API or thin wrappers.
- Deterministic tests: route/service tests + C# transport/seam tests.

## §3 Hard OUT

- Paid CDN-only pipelines; credentials in URL; arbitrary protocol handlers (`file:`, `ftp:`) as download sources.
- Marking jobs **completed** before checksum verification (when `expected_sha256` supplied) or before successful `register_model`.
- Silent partial activation: **no** `ModelRegistryService.register_artifact` / `ModelStorage.register_model` on failed or unverified bytes.
- True multi-part HTTP resume as a **guarantee** (best-effort only; see authority memo).

## §4 Authority map

| Concern | Owner |
|---------|--------|
| Job lifecycle + progress truth | `JobRepository` + `/api/jobs` |
| Download bytes + staging + checksum | `model_download_service` |
| Model file registration | `ModelStorage` (`register_model`) |
| Lifecycle catalog artifact row | `ModelRegistryService.register_artifact` (when service available) |
| UI commands | `ModelManagerViewModel` → `IModelManagerClient` + `IJobProgressApiClient` (or jobs client) |

## §5 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
python -m pytest tests/unit/backend/api/routes/test_models.py tests/unit/backend/api/routes/test_models_download_manager.py -q
python -m pytest tests/ci/ -q --randomly-seed=12345
.\scripts\verify.ps1 -Quick
python scripts/run_verification.py
```

## §6 Risk register

| Risk | Mitigation |
|------|------------|
| Duplicate concurrent downloads | Single-flight key + 409 on duplicate active job |
| Large files / memory | Stream to disk; bounded read chunk |
| SSRF via URL | Allowlist `http`/`https` only; block localhost/private IP optional follow-up |
| Retry does not restart worker | `retry` / `resume` paths call download scheduler for `job_type=download` |

## §7 Rollback order

1. Model Manager VM / client methods  
2. `models.py` download routes  
3. `jobs.py` download hooks  
4. `model_download_service.py`  
5. `JobType` enum extension  
6. Governance docs / closure report  

## §8 Related

- [GOV_VOICESTUDIO_GAP040_NONDESTRUCTIVE_EDIT_MODEL_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP040_NONDESTRUCTIVE_EDIT_MODEL_01_EXECUTION_ROW.md)  
- [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md)  
