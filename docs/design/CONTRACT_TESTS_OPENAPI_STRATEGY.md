# Contract Tests — OpenAPI Strategy (Static vs Live)

**Status:** Adopted (2026-03-19)  
**Scope:** `tests/contract/` — how OpenAPI and the FastAPI app are sourced for assertions.

## Audit summary

| Surface | Mechanism | Needs live `app` / `app.openapi()`? |
|---------|-----------|-------------------------------------|
| `tests/contract/conftest.py` — `openapi_schema` (session) | Tries **live** `_register_all_routes()` + `app.openapi()`; falls back to `docs/api/openapi.json` | **Yes** for first choice; static fallback if live throws |
| `tests/contract/test_openapi_contract.py` — local `openapi_schema` fixture | **Shadows** session fixture; loads **only** `docs/api/openapi.json` | **No** — entire module is static-file based |
| `tests/contract/test_gateway_contracts.py` | Uses session `openapi_schema` + `api_paths` | **Effectively live-first** when session fixture succeeds |
| `tests/contract/test_health_contracts.py` | `contract_client` → `TestClient(app)` | **Yes** — HTTP against in-process app |
| `tests/contract/test_route_enumeration.py` | Imports `app`, calls `app.openapi()` inside helpers | **Yes** — route enumeration against live schema |
| `tests/contract/test_openapi_schema_drift.py` | Hash / drift vs committed export | **Tied to export workflow** (`scripts/export_openapi_schema.py`), not necessarily live per test |

## Decision: **Split responsibilities (Option B)**

1. **Static OpenAPI file** remains the **default source of truth** for **documentation parity** and **JSON-structure** checks. This is already explicit in `test_openapi_contract.py` (module-local fixture).

2. **Live OpenAPI + `TestClient`** remain **required** for:
   - Gateway / path existence checks that must reflect **actually registered** routes.
   - HTTP response smoke (`contract_client`).
   - Route enumeration that walks **current** `paths` and response models.

3. **Do not** flip the session `openapi_schema` fixture to static-only without a dedicated **“live route registration”** gate: gateway and route tests would lose fidelity to lazy registration and runtime-only routes.

4. **Optional future lane (not implemented here):** a `pytest` marker or job split:
   - **Fast:** static OpenAPI + shared schemas + manifest-only tests.
   - **Slow / integration:** full session bootstrap + `TestClient` + route enumeration.

## Consequences

- **Positive:** Clear separation of concerns; static checks stay fast and deterministic; live checks catch registration drift.
- **Negative:** Session fixture still pays **live bootstrap** cost once per session; mitigated by bounded stage timeout and subprocess logging in `verify.ps1`.

## Related

- `tests/contract/conftest.py` — session fixtures
- `docs/reports/contract_tests_hang_diagnosis_20260319.md` — harness buffering vs true hang
