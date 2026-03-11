# OpenAPI CI Dependency Alignment

> **Version**: 1.0  
> **Last Updated**: 2026-03-11  
> **Classification**: Developer Guide

---

## Purpose

Scripts that export or validate the OpenAPI schema import `backend.api.main`, which loads all routes and their dependencies (engines, services, etc.). If a CI job uses minimal dependencies, route imports can fail (e.g. missing torch, engine adapters). This document ensures all OpenAPI-related CI steps use the same dependency set as the backend runtime.

---

## Scripts That Import Backend

| Script | Imports | Purpose |
|--------|---------|---------|
| `scripts/export_openapi_schema.py` | `backend.api.main` | Export OpenAPI schema to JSON |
| `scripts/validate_openapi_contract.py` | `backend.api.main` | Validate spec vs routes |
| `scripts/generate_openapi.py` | `backend.api.main` | Generate schema (legacy) |
| `scripts/regenerate_openapi.py` | `backend.api.main` | Regenerate schema |

**Rule:** Any job that runs these scripts **must** install `requirements.txt` (full backend deps) before execution.

---

## Tests That Do NOT Import Backend

| Test | Behavior | Deps Required |
|------|----------|---------------|
| `tests/ci/test_contract_drift_gate.py` | Reads `docs/api/openapi.json` + hash file only | None (stdlib + pytest) |
| `tests/contract/test_openapi_contract.py` | Validates schema structure from JSON file | pytest, json |
| `tests/contract/test_openapi_schema_drift.py` | Compares schema to baseline | pytest, json |

These tests do not import `backend.api.main`; they operate on the committed schema file. Minimal deps are acceptable.

---

## Workflow Alignment (Current State)

| Workflow | Job | OpenAPI Step | Deps Used | Aligned? |
|----------|-----|--------------|-----------|----------|
| build.yml | build-backend | validate_openapi_contract.py, export_openapi_schema.py | requirements.txt | ✓ |
| ci.yml | python-tests | test_contract_drift_gate.py | pip install -e ".[dev,extras]" | ✓ (no backend import) |
| test.yml | contract-tests | pytest tests/contract/ | requirements.txt + pytest | ✓ |
| sentinel_backend_smoke.yml | schema-validation | Sentinel schema tests only | pytest, jsonschema, httpx | ✓ (no OpenAPI export) |
| sentinel_backend_smoke.yml | sentinel-smoke | Backend startup + API calls | requirements.txt | ✓ |

---

## Verification

Before adding a new CI step that runs OpenAPI export or validation:

1. Confirm the job installs `requirements.txt` (or equivalent full backend deps).
2. Do not use minimal deps (e.g. `pip install pytest httpx` only) for steps that import `backend.api.main`.
3. If route import failures occur, add missing deps to requirements.txt or use a backend-compatible venv.

---

## Related

- [CI_SUPPRESSION_POLICY.md](CI_SUPPRESSION_POLICY.md) — Non-blocking CI patterns
- [DEFERRED_V1_2.md](../governance/DEFERRED_V1_2.md) — OpenAPI drift dep alignment (documented)
