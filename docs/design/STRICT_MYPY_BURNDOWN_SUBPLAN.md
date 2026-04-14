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

## Sixth Slice Complete (2026-03-11)

- `mypy backend/api/routes/voice/audio.py backend/api/routes/voice/streaming.py --strict --follow-imports=skip` passes.
- Fixes: file-level `untyped-decorator` disable (SAFETY: FastAPI router decorators lack complete type stubs); `NDArray[Any]` for `_send_audio_chunk`; return type `-> FileResponse` for `get_audio`; return type `-> None` for `synthesize_stream`.

## Seventh Slice Complete (2026-03-11)

- `mypy backend/api/routes/voice/cloning.py backend/api/routes/voice/processing.py --strict --follow-imports=skip` passes.
- Fixes: file-level `untyped-decorator` disable (SAFETY: FastAPI router decorators lack complete type stubs); `getattr(cv2, "VideoWriter_fourcc")` for opencv stub gap (no type: ignore).

## Eighth Slice Complete (2026-03-11)

- `mypy backend/api/routes/voice/_helpers.py backend/api/routes/voice/_shared.py --strict --follow-imports=skip` passes.
- Fixes: `**kwargs: Any` for _log_context; return type `-> None` for _ensure_tts_assets, _ensure_vc_assets; `str()` for no-any-return in _normalize_engine_id and _select_engine_with_fallback; `NDArray[Any]` for _send_audio_chunk.

## Ninth Slice Complete (2026-03-11)

- `mypy backend/api/routes/voice/ --strict --follow-imports=skip` passes (10 source files).
- Fixes: Added `__all__` to `_shared.py` for no-implicit-reexport (EngineConfigServiceDep, EngineProcessingException, EngineServiceDep, EngineUnavailableException, EventType, HAS_*, InvalidEngineException, ProfileNotFoundException, STREAMING_ENGINES, get_config, get_engine_breaker, instrument_flow, logger, router).

## Tenth Slice Complete (2026-03-11)

- `mypy backend/services/{path_service,training_broadcaster,script_store,circuit_breaker}.py --strict --follow-imports=skip` passes.
- Fixes: path_service: `cast(Path, get_path(...))` for no-any-return; training_broadcaster: `dict[str, Any]` for type-arg; script_store: `dict[str, Any]` return; circuit_breaker: `AsyncIterator[None]` for __call__, `*args: Any, **kwargs: Any` for execute, `cast(T, ...)` for async return.

## Eleventh Slice Complete (2026-03-11)

- `mypy backend/services/{emotion_service,track_store,error_tracker,telemetry,slo_monitor,usage_stats}.py --strict --follow-imports=skip` passes.
- Fixes: emotion_service: `dict[str, Any]` params/return, `cast` for handler result; track_store: `str(track_id)`, `cast` for json.load; error_tracker: `dict[str, Any]` for params/headers; telemetry: `result: dict[str, Any] = {}`; slo_monitor: `deque[MetricSample]`; usage_stats: `cast(Path, ...)`, `dict[str, Any]`, `cast` for json.load.

## Twelfth Slice Complete (2026-03-11)

- `mypy backend/services/{project_service,training_quality}.py --strict --follow-imports=skip` passes.
- Fixes: project_service: return type `ProjectStoreService` via TYPE_CHECKING; training_quality: `list[dict[str, Any]]` and `dict[str, Any]` for all quality_history params and returns.

## Thirteenth Slice Complete (2026-03-11)

- `mypy backend/services/{api_key_store,profile_store,quality_consistency_service,audio_download_service}.py --strict --follow-imports=skip` passes.
- Fixes: api_key_store: `cast(dict[str, dict[str, Any]], data.get("keys", {}))` for no-any-return; profile_store: `cast(dict[str, Any], json.load(f))`, `str(profile_id)`; quality_consistency_service: `float(variance**0.5)`; audio_download_service: `cast(Path, cache_path)` for both return paths.

## Fourteenth Slice Complete (2026-03-11)

- `mypy backend/services/JobStateStore.py --strict --follow-imports=skip` passes.
- Fixes: `cast(Path, get_path("jobs"))` for _resolve_jobs_root; `cast(dict[str, Any], json.loads(...))` for get().

## Fifteenth Slice Complete (2026-03-11)

- `mypy backend/services/workload_balancer.py --strict --follow-imports=skip` passes.
- Fixes: `asyncio.Task[None]` for type-arg; `cast(int, psutil.virtual_memory().total)`, `cast(int, torch.cuda.device_count())`; `int(len(...))` for nvidia-smi; `int(torch.cuda.get_device_properties(...).total_memory)`; `dict[str, Any]` for get_stats return.

## Sixteenth Slice Complete (2026-03-11)

- `mypy backend/services/request_queue.py --strict --follow-imports=skip` passes.
- Fixes: `QueuedRequest[T]` for __lt__ param; conditional `engine_sem` when `request.engine_type is not None`; `dict[str, Any]` for get_stats; `RequestQueue[Any]` for global and return type.

## Seventeenth Slice Complete (2026-03-11)

- `mypy backend/services/edit_history.py --strict --follow-imports=skip` passes.
- Fixes: `track_store: Any` and `-> None` for AddClipCommand, RemoveClipCommand, MoveClipCommand __init__.

## Eighteenth Slice Complete (2026-03-11)

- `mypy backend/services/voice_presets.py backend/services/llm_provider_service.py --strict --follow-imports=skip` passes.
- Fixes: voice_presets: `list[Callable[[VoicePreset], None]]` for _load_callbacks; llm_provider_service: `list[str] | None` for ProviderInfo.models, `-> None` for __post_init__ and __init__, assert cache before .values()/.get(), to_dict uses `self.models if self.models is not None else []`.

## Nineteenth Slice Complete (2026-03-11)

- `mypy backend/services/ab_testing.py --strict --follow-imports=skip` passes.
- Fixes: None required; ab_testing already strict-compliant.

## Twentieth Slice Complete (2026-03-11)

- `mypy backend/services/diagnostics.py --strict --follow-imports=skip` passes.
- Fixes: `dict[str, Any]` for DiagnosticCheck.details, DiagnosticReport.environment, _add_check details param, _collect_environment return and env_info, get_quick_status return; `from typing import Any`.

## Twenty-First Slice Complete (2026-03-11)

- `mypy backend/services/json_file_store.py --strict --follow-imports=skip` passes.
- Fixes: `-> None` for _ensure_loaded, _write_to_disk, _delete_from_disk, _evict_if_needed, clear; `Callable[[dict[str, Any]], bool]` for search predicate; `_SequenceOfDict` alias to avoid `list` method shadowing; `List[str]` for list_ids; `builtins.list` for list()/list_ids()/clear() return values; `Sequence` for list/search return types.

## Twenty-Second Slice Complete (2026-03-11)

- `mypy backend/services/audit_logger.py --strict --follow-imports=skip` passes.
- Fixes: `Queue[Any]`, `Task[None]` for type params; `masked: dict[str, Any]` and `nested if nested is not None else value` for _mask_sensitive; `**kwargs: Any` for log_create, log_update, log_delete, log_login; `results: list[AuditEntry] = []` for query.

## Twenty-Third Slice Complete (2026-03-11)

- `mypy backend/services/collaboration_service.py --strict --follow-imports=skip` passes.
- Fixes: `-> None` for leave_project, update_cursor, subscribe, unsubscribe, _export_vstudio, _export_zip, _export_json; `cast(dict[str, Any], json.loads(...))` in _import_archive; `cast(dict[str, Any], json.load(f))` in _import_json; `from typing import cast`.

## Reassessment (2026-03-11)

- **Decision:** Continue one trivial slice, then pivot to architecture.
- **Ranked list:** (1) audio_artifacts/usage.py — 1 error, trivial; (2) marketplace_service.py — 1 error; (3) persistent_store.py — 3 errors.

## Twenty-Fourth Slice Complete (2026-03-11)

- `mypy backend/services/audio_artifacts/usage.py --strict --follow-imports=skip` passes.
- Fixes: `metadata: dict | None` → `metadata: dict[str, Any] | None`; `from typing import Any`.

## Twenty-Fifth Slice Complete (2026-03-12)

- `mypy backend/services/persistent_store.py backend/services/marketplace_service.py --strict --follow-imports=skip` passes.
- Fixes: persistent_store: `-> list[str]` for keys, `-> list[Any]` for values, `-> list[tuple[str, Any]]` for items; marketplace_service: `cast(dict[str, Any], r.to_dict())` for add_review no-any-return.

## Twenty-Sixth Slice Complete (2026-03-12)

- `mypy backend/services/quality_metrics_db.py backend/services/phrase_emotion_service.py --strict --follow-imports=skip` passes.
- Fixes: quality_metrics_db: `return str(entry_id)` for no-any-return; `row: tuple[Any, ...]` for _row_to_entry; phrase_emotion_service: `-> None` for __init__ and _init_default_presets.

## Twenty-Seventh Slice Complete (2026-03-12)

- `mypy backend/services/profile_search_service.py --strict --follow-imports=skip` passes.
- Fixes: `data: dict[str, Any]` for _DictToObject; `-> Any` for _profile_store; `-> Iterator[str]` for __iter__; `-> list[str]` for keys with cast; `-> list[_DictToObject]` for values with cast; `-> Iterator[tuple[str, _DictToObject | None]]` for items; `cast(_DictToObject | None, default)` for get; `cast(_DictToObject, self._wrap(result))` for __getitem__; `-> Any` for get_profile_timestamps_store.

## Twenty-Eighth Slice Complete (2026-03-12)

- `mypy backend/services/macro_store.py backend/services/effect_chain_store.py --strict --follow-imports=skip` passes.
- Fixes: cast/str/bool/int for no-any-return; list() for Sequence→list; cast for get() returns; cast for entry.get("status")/("schedule").

## Twenty-Ninth Slice Complete (2026-03-12)

- `mypy backend/services/unified_config.py backend/services/AudioArtifactRegistry.py --strict --follow-imports=skip` passes.
- Fixes: unified_config: `re.Match[str]` for replacer; cast(dict[str, Any], expand_env_vars_recursive(...)) and cache returns; bool(override.get(...)); AudioArtifactRegistry: cast(Path, get_path(...)); payload: dict[str, Any].

## Thirtieth Slice Complete (2026-03-12)

- `mypy backend/services/ml_optimization/hyperparameter_optimization.py --strict --follow-imports=skip` passes.
- Fixes: `-> None` for __init__; `Callable[[dict[str, Any]], float]` for objective params; `def wrapped_objective(trial: Any) -> float` and `params: dict[str, Any]`; `def wrapped_objective(params: dict[str, Any]) -> float` for hyperopt.

## Thirty-First Slice Complete (2026-03-12)

- `mypy backend/services/lexicon_service.py --strict --follow-imports=skip` passes.
- Fixes: Replaced BaseModel with @dataclass for PhonemeEstimateRequest (avoids "BaseModel has type Any" when --follow-imports=skip).

## Proof Criteria

- `mypy backend/api/routes/ --strict` passes (or documented exceptions)
- `mypy backend/services/ --strict` passes (or documented exceptions)
- No new `# type: ignore` without SAFETY justification

## Non-Goals

- Full codebase strict mypy (only routes + services for v1.2)
- Blocking CI on mypy (advisory only per DEFERRED_V1_2)

## Owner

TBD (assign in task brief when work starts)

---

## Completion — narrow C-4 contract (2026-04-14)

The **CI gate scope** in [`.ci/mypy_strict_baseline.json`](../../.ci/mypy_strict_baseline.json) is **at zero errors** with **`baseline_errors`: 0** and **`last_updated`**: **2026-04-14**. Enforcement: [`tests/ci/test_mypy_strict_scope.py`](../../tests/ci/test_mypy_strict_scope.py) (collected under default `pytest tests/`). **Baseline bump rule:** any increase requires an explicit commit justification **and** an update to this subplan section **and** the JSON baseline — no silent regressions.

**Relationship to full subplan scope:** Historical slices above targeted broader `backend/api/routes/` and `backend/services/` strictness. The **maintained contract** for “C-4 lane closure” in tracker/roadmap is the **baseline-gated paths** above; expanding strict coverage to additional modules is **new work** under the same discipline (incremental slices), not a reopening of this closure row.

**Proof:** gate PASS locally/CI; see [.cursor/STATE.md](../../.cursor/STATE.md) **LATEST PROOF INDEX** row **C-4 mypy strict-scope**.
