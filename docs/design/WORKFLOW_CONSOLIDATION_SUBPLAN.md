# Workflow Consolidation Subplan (v1.2)

> **Source:** [DEFERRED_V1_2.md](../governance/DEFERRED_V1_2.md)  
> **Status:** Executable subplan for v1.2

---

## Scope

Reduce duplication across Build, CI, Tests, Sentinel workflows; align job structure and caching.

## Current Workflow Map

| Workflow | Triggers | Jobs | Purpose |
|----------|----------|------|---------|
| **build.yml** | push/PR main, develop | build-frontend, build-backend, etc. | Full build, XAML checks, backend build |
| **ci.yml** | push/PR main, develop, release/* | python-tests, dotnet-build, ... | Python tests, .NET build, guardrails, coverage |
| **test.yml** | push/PR, schedule (nightly) | test-backend, nightly-golden-loop, test-frontend | Backend unit/integration, golden loop, frontend tests |
| **sentinel_backend_smoke.yml** | schedule, workflow_dispatch | sentinel-smoke | Backend startup smoke |
| **sentinel_ui_smoke_nightly.yml** | schedule | UI smoke | UI smoke (nightly) |

## Duplication Identified

- **Python setup:** build.yml, ci.yml, test.yml each set up Python; cache keys may differ
- **pip install:** `pip install -e ".[dev,extras]"` vs `pip install -r requirements.txt` vs `requirements_engines.txt`
- **.NET setup:** build.yml and ci.yml both run dotnet restore/build
- **Test scope:** ci.yml runs `pytest tests/`; test.yml runs `pytest tests/unit/` + `tests/integration/` — overlap possible

## Sequence

1. **Document canonical vs legacy**
   - **Canonical:** ci.yml as primary gate (python-tests + dotnet-build)
   - **Legacy/convenience:** build.yml for build-only; test.yml for nightly + matrix (3.10/3.11)

2. **Propose consolidation steps**
   - Extract reusable composite action or workflow call for: Python setup + cache, pip install
   - Align cache keys: `${{ runner.os }}-pip-${{ env.PYTHON_VERSION }}-${{ hashFiles('**/requirements*.txt') }}`
   - Consider: merge test-backend from test.yml into ci.yml python-tests if scope aligns

3. **Blast-radius notes**
   - Changing cache keys can invalidate caches; document migration
   - test.yml matrix (3.10/3.11) may be intentional for compatibility; preserve if so
   - sentinel workflows: keep separate (different schedule, continue-on-error)

## First Slice Complete (2026-03-11)

- Created `.github/actions/setup-python-pip` composite action.
- Canonical cache key: `${{ runner.os }}-pip-${{ python-version }}-${{ hashFiles('**/requirements*.txt') }}`
- Migrated: `build.yml` build-backend, `test.yml` test-backend.
- Blast radius: 2 jobs; behavior identical (same setup + cache + install order).

## Second Slice Complete (2026-03-11)

- Extended composite with `cache-mode` input: `requirements` (default) or `editable`.
- Editable mode: cache key uses `hashFiles('pyproject.toml')` for `pip install -e ".[dev,extras]"`.
- Migrated: `ci.yml` python-tests to use composite with `cache-mode: editable`.
- Blast radius: 1 job; cache key now aligned with pyproject.toml for editable install.

## Third Slice Complete (2026-03-11)

- Migrated: `test.yml` nightly-golden-loop-real to use composite with `cache-mode: editable`.
- Replaced direct `actions/setup-python@v5` with `./.github/actions/setup-python-pip`; same install pattern as ci.yml python-tests.
- Blast radius: 1 job; pip cache now used for nightly golden loop.

## Fourth Slice Complete (2026-03-11)

- Script deduplication: removed root duplicates; canonical paths in scripts/setup/, scripts/migrate/, scripts/generate/.
- Removed: scripts/generate_sentinel_fixture.py, scripts/setup_test_audio.ps1, scripts/setup_openmemory.ps1, scripts/setup_gpu_venv.ps1, scripts/migrate_config.py, scripts/migrate_di.py, scripts/migrate_to_env_setup.py.
- Updated references: TOOLS_REGISTRY.md, canonical.py, MEMORY_INTEGRATION_GUIDE.md, skill_map.json, TASK-0040.md, script docstrings.

## Fifth Slice Complete (2026-03-11)

- Migrated sentinel_backend_smoke.yml: schema-validation and sentinel-smoke jobs now use `./.github/actions/setup-python-pip` with cache-mode: requirements.
- Replaced direct actions/setup-python + manual cache with composite; pip cache key aligned with other workflows.
- Blast radius: 2 jobs; behavior identical.

## Sixth Slice Complete (2026-03-11)

- Migrated sbom.yml python-sbom job to use `./.github/actions/setup-python-pip` with cache-mode: requirements.
- Blast radius: 1 job; pip cache now used for SBOM generation.

## Seventh Slice Complete (2026-03-11)

- Migrated release.yml: build-windows and publish-docs jobs now use `./.github/actions/setup-python-pip` with cache-mode: requirements.
- Blast radius: 2 jobs; pip cache aligned with other workflows.

## Eighth Slice Complete (2026-03-11)

- Migrated security-monitor.yml: python-security-scan and aggregate-report jobs now use `./.github/actions/setup-python-pip` with cache-mode: requirements.
- Blast radius: 2 jobs; pip cache aligned with other workflows.

## Ninth Slice Complete (2026-03-11)

- Migrated governance.yml: governance-tests and policy-validation jobs now use `./.github/actions/setup-python-pip` with cache-mode: requirements.
- Blast radius: 2 jobs; pip cache aligned with other workflows.

## Tenth Slice Complete (2026-03-11)

- Migrated plugin-submission.yml: process-submission and approve-submission jobs now use `./.github/actions/setup-python-pip` with cache-mode: requirements.
- Blast radius: 2 jobs; pip cache aligned with other workflows.

## Eleventh Slice Complete (2026-03-11)

- Migrated sentinel_ui_smoke_nightly.yml: ui-smoke and page-object-validation jobs now use `./.github/actions/setup-python-pip` with cache-mode: requirements.
- Blast radius: 2 jobs; pip cache aligned with other workflows.

## Proof Criteria

- Duplicate setup steps reduced (e.g. single source for Python/pip setup)
- Cache keys documented and consistent
- No regression in CI pass rate

## Non-Goals

- Single monolithic workflow (maintain separation for clarity)
- Removing test.yml (nightly golden loop is distinct)

## Owner

TBD (assign in task brief when work starts)
