# Strict Mypy Burn-Down Subplan (v1.2)

> **Source:** [DEFERRED_V1_2.md](../governance/DEFERRED_V1_2.md)  
> **Status:** Advisory; not a gate. Executable subplan for incremental improvement.

---

## Scope

Run mypy with `--strict` and address findings incrementally. Target modules: `backend/api/routes/`, `backend/services/`.

## Sequence

1. **Baseline**
   - Run `mypy backend/api/routes/ --strict` and `mypy backend/services/ --strict`
   - Capture error count and categories (e.g. missing annotations, incompatible types)

2. **Prioritize**
   - **Routes first:** Smaller surface, often simpler types
   - **Services second:** May depend on route types

3. **Per-folder strictness**
   - Max tolerated ignores per folder: 0 (goal); document any `# type: ignore` with SAFETY comment per no-suppression rule
   - Enforce per-folder strictness over time: add `mypy.ini` or `pyproject.toml` section per package

4. **Incremental execution**
   - Fix one module at a time; run mypy after each change
   - Prefer proper annotations over `# type: ignore`

## Baseline (2026-03-11)

- `mypy backend/api/routes/health.py --strict`: ~12 errors (untyped decorators, IEngineService attr, get_performance_middleware, etc.)
- First slice target: `backend/api/routes/health.py` — fix annotations; document any remaining ignores with SAFETY

## First Slice Complete (2026-03-11)

- `mypy backend/api/routes/health.py --strict --follow-imports=skip` passes.
- Fixes: file-level `untyped-decorator` disable (SAFETY: FastAPI/Starlette stubs); `get_performance_middleware` return type; `cast()` for no-any-return; `bool()` for _is_healthy.

## Second Slice Complete (2026-03-11)

- `mypy backend/api/routes/v2/health.py --strict --follow-imports=skip` passes.
- Fixes: file-level `untyped-decorator` disable (SAFETY: FastAPI router decorators lack complete type stubs).

## Third Slice Complete (2026-03-11)

- `mypy backend/api/routes/voice/testing.py --strict --follow-imports=skip` passes.
- Fixes: file-level `untyped-decorator` disable (SAFETY: FastAPI router lacks complete type stubs).

## Fourth Slice Complete (2026-03-11)

- `mypy backend/api/routes/voice/analysis.py --strict --follow-imports=skip` passes.
- Fixes: file-level `untyped-decorator` disable (SAFETY: FastAPI router lacks complete type stubs); `float(cv)` for pitch_stability assignment (numpy numeric to float); return type `dict[str, Any]` for `test_pronunciation`.

## Fifth Slice Complete (2026-03-11)

- `mypy backend/api/routes/voice/synthesis.py --strict --follow-imports=skip` passes.
- Fixes: file-level `untyped-decorator` disable (SAFETY: FastAPI router decorators lack complete type stubs); return type `-> VoiceSynthesizeResponse` for `synthesize_with_style` and `synthesize_cross_lingual`.

## Proof Criteria

- `mypy backend/api/routes/ --strict` passes (or documented exceptions)
- `mypy backend/services/ --strict` passes (or documented exceptions)
- No new `# type: ignore` without SAFETY justification

## Non-Goals

- Full codebase strict mypy (only routes + services for v1.2)
- Blocking CI on mypy (advisory only per DEFERRED_V1_2)

## Owner

TBD (assign in task brief when work starts)
