# PROOF — Slice 29 — Preflight registry (control-plane map)

**Status:** **PASS** — registry is the **discovery** source for `engine_id` → **`ensure_*`** without duplicating long if/elif chains in every tool.

**Date:** 2026-04-23  

## What changed

| File | Role |
| --- | --- |
| [`backend/services/preflight_registry.py`](../../backend/services/preflight_registry.py) | **`get_engine_preflight_callables`**, **`run_registered_preflight`** |
| [`tests/unit/backend/services/test_preflight_registry.py`](../../tests/unit/backend/services/test_preflight_registry.py) | Unit tests — keys include **`vosk`**, **`parakeet`**, **`whisper_cpp`** |

## Scope

| In scope | Out of scope |
| --- | --- |
| Central map for tooling / probes | Rewriting all of `health.py` in one slice (incremental OK) |

## Verification

```powershell
python -m pytest tests/unit/backend/services/test_preflight_registry.py -q
```

**Expected:** exit code **0**.

## Honesty

Health routes may still wrap engines for HTTP-specific error shaping; registry is **authoritative for which ids have a public ensure API**, not for all HTTP semantics.
