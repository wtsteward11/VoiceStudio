# Phase 4 — Root cause (GOV-VOICESTUDIO-BACKEND-READINESS-TRUTH-01)

## Taxonomy result (Phase 1)

- **Cold start harness:** `PROOF_BACKEND_COLD_START_2026-04-11.json` — healthy within budget (`within_budget: true`, ~27s cold start).
- **No A–F failure reproduced** in the diagnostic harness on the closure machine.

## Applied mitigations (honest scope)

1. **Category E (Python env / path drift)** — *preventive / detection*, not a reproduced failure:
   - `scripts/ci/check_runtime_prerequisites.py` now runs a subprocess import smoke: `import backend.api.main` with `PYTHONPATH` set to the repo root and **120s** timeout.
   - Exit **2 (BLOCKED)** with `blocked_reason` when this import fails, so broken venv / missing deps surface before Grade-R real pytest.

2. **No timed “fixes”** for startup — no `Task.Delay` workarounds, no health semantics changes for the C# probe beyond additive artifacts and `engines_ready` on `GET /health`.

3. **UI timeout path** — if operators still see **"Backend started but did not become healthy within timeout"**, use new `startup_decision.json` fields (`spawn_elapsed_ms`, `health_attempts`, `healthy_elapsed_ms`, `last_stderr_lines`, `python_path_resolved`) plus `[STARTUP-TIMING]` logs to classify A–F on the failing machine.

## Deferred (requires failing capture)

Category-specific fixes for **B/C/D/F** remain **out of scope** until a run reproduces them with evidence (SQLite lock trace, port owner PID, firewall, etc.).
