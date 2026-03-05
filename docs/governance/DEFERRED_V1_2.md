# Deferred to v1.2

Items deferred from v1.1.0 ship. Non-blocking for v1.1.0 release.

## Completed (no action needed)

- **Flask-to-FastAPI refactors** — Already done. No Flask code remains in the codebase.

## Advisory / Non-blocking

- **Strict mypy burn-down** — Advisory only; not a gate. Run mypy with `--strict` and address findings incrementally.

## Deferred

- **Skip debt cleanup** — Remove or consolidate skip markers in tests; address when bandwidth allows.

- **Workflow consolidation** — Reduce duplication across Build, CI, Tests, Sentinel workflows; align job structure and caching.

- **CI suppression policy perfection** — Tighten rules around `|| echo "::warning::..."` and other non-blocking patterns; document when suppression is acceptable.

- **Bandit B614 torch.load exemption** — Document and formalize exemption for `torch.load` in ML/engine code; ensure `weights_only` or equivalent when feasible.

- **OpenAPI drift check dep alignment** — Ensure Sentinel (or any workflow running OpenAPI drift) uses the same dependency set as backend runtime; avoid minimal deps that cause route import failures.
