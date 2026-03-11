# Deferred to v1.2

Items deferred from v1.1.0 ship. Non-blocking for v1.1.0 release.

## Completed (no action needed)

- **Flask-to-FastAPI refactors** — Already done. No Flask code remains in the codebase.

## Advisory / Non-blocking

- **Strict mypy burn-down** — Advisory only; not a gate. Run mypy with `--strict` and address findings incrementally.

## Deferred

- **Skip debt cleanup** — Remove or consolidate skip markers in tests; address when bandwidth allows. Realistic estimate: 2–3 days (312+ skipped tests). Target: reduce to fewer than 200 skips by v1.2 ship. Run `python -m pytest tests/ --co -q 2>&1 | rg "SKIP" > skip_report.txt` to regenerate skip report.

- **Workflow consolidation** — Reduce duplication across Build, CI, Tests, Sentinel workflows; align job structure and caching.

- ~~**CI suppression policy perfection**~~ — **DONE (2026-03-11):** Documented in `docs/developer/CI_SUPPRESSION_POLICY.md`. Allowed vs prohibited patterns, inventory of non-blocking steps, quarterly review process.

- ~~**Bandit B614 torch.load exemption**~~ — **DONE (2026-03-11):** Documented in `docs/governance/CVE_EXCEPTIONS.md` § Bandit B614. CI uses `--skip B614`; prefer `weights_only=True` or safetensors when feasible.

- ~~**OpenAPI drift check dep alignment**~~ — **DONE (2026-03-11):** Documented in `docs/developer/OPENAPI_CI_ALIGNMENT.md`. All workflows that run export/validate use requirements.txt; schema-only tests (drift gate, contract tests) do not import backend.

- ~~**Sentinel smoke backend startup**~~ — **DONE (2026-03-11):** Increased wait from 60×2s (2min) to 90×2s (3min) in sentinel_backend_smoke.yml. Documented in SENTINEL_TESTING_GUIDE.md. sentinel-smoke remains continue-on-error until proven stable; remove when CI consistently passes.
