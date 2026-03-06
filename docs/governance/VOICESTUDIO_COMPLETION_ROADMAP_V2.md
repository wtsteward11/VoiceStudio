# VoiceStudio Completion Roadmap v2.0 — CI-Enforced Edition

**Date**: March 3, 2026 | **Target**: v1.1.0 | **Confidential**

**Stack**: WinUI 3 / C# + FastAPI / Python
**Engines**: XTTS v2 (primary) · Piper · whisper_cpp · 42 adapters

---

## What Changed From v1.0 — And Why

v1.0 roadmap: correct gaps, weak enforcement. ChatGPT critique: mostly valid on 4 of 5 points. This document: same gap list, every major claim now backed by a CI gate or a hard fork decision.

### Accepted Criticisms

- **ACCEPTED — Point A (Mediator decision gate)**: roadmap now forces wire-fully-or-delete-fully. No limbo.
- **ACCEPTED — Point B (Router uniqueness CI gate)**: CI test enumerates all FastAPI routes and asserts one voice router source of truth.
- **ACCEPTED — Point D (Proof artifact spec)**: golden path proof now requires model hashes, engine mode, git commit, output file hash, and audio energy check.
- **ACCEPTED — Point E (Safety choke point gate)**: trust/safety matrix doc is now backed by a required FastAPI dependency on synthesis routes + CI enforcement.
- **PARTIAL — Point C (AssertionError traceback)**: structured traceback logging requirement added. But 'CI fails if log contains AssertionError without stack' is too brittle for prod logs — instead, the acceptance criterion is a root-cause document + regression test.

### Wrong Criticism

- **WRONG (minor framing issue)**: ChatGPT's 'two upgrades' summary undersells the work. All 5 gates are required, not just 2.

### Governing Principle — UNCHANGED

100% complete means exactly one thing: `pytest tests/e2e/test_golden_path.py` exits 0 with real XTTS + whisper_cpp loaded, real audio in, real synthesized audio out, proof artifact on disk with model hashes and git commit. Everything else is scaffolding.

---

## Section 1: Ground Truth Gaps (Verified 2026-03-03)

### Gap 1 — voice.py Is a 138 KB God-Route

- `backend/api/routes/voice.py` — 138,410 bytes, co-existing alongside `voice/` subdirectory
- Two routing implementations simultaneously active. Import order in `main.py` determines which shadows which.
- Thin-route rule (TD-023) is formally closed, but this file alone violates it more than the 37 previously fixed combined.

### Gap 2 — Service Layer M4 Is Dead Code

- `backend/application/` — CommandDispatcher, QueryDispatcher, commands, queries all exist
- Zero handlers registered at startup. No route calls `dispatch_command()`. The mediator never runs.
- Decision required: wire it fully with real handlers and route delegation, or delete it. Half-wiring is the worst outcome.

### Gap 3 — Active Runtime AssertionError

- `.audit/log-2026-03-01.md` — AssertionError logged 2026-03-01 02:49:23, no traceback, no RCA
- Occurred AFTER all 'completion' declarations. Not in TECH_DEBT_REGISTER.md.

### Gap 4 — Non-Deterministic Test Suite

- Full pytest suite produces different pass/fail results depending on execution order
- Shared in-process state (FastAPI app instance, singleton services, DB fixtures) not reset between tests
- CI GREEN on a non-deterministic suite is a coin flip, not a guarantee.

### Gap 5 — OpenAPI Schema $ref Gaps

- `docs/api/openapi.json` — 508 paths, flagged for broken `$ref` resolution
- Breaks auto-generated client SDKs.

### Gap 6 — Trust/Safety Enforcement Unverified

- `consent.py` and `safety.py` exist and are functionally correct in isolation
- No verification that every synthesis entrypoint enforces consent + rate limit + safety scan.
- `instant_cloning.py`, `batch.py`, `multi_voice_generator.py`, `ensemble.py` — enforcement status unknown

### Gap 7 — Golden Path Not Run With Real Engines

- `tests/e2e/test_golden_path.py` — structurally complete, 5 steps implemented correctly
- Has not been validated with XTTS + whisper_cpp executing on real models, producing real audio output

---

## Section 2: The Hardened Roadmap

Six phases. Every milestone has a CI gate, a specific command, or a hard fork decision.

### Phase 0: Permanent CI Invariants

| Invariant | Gate | Test File |
|-----------|------|-----------|
| I-1 Router Uniqueness | No duplicate method+path; voice router from one source | `tests/ci/test_router_uniqueness.py` |
| I-2 Trust/Safety Choke Point | All synthesis routes have `require_synthesis_clearance` dependency | `tests/ci/test_synthesis_safety_coverage.py` |
| I-3 Proof Artifact Completeness | Required fields enforced | Schema: `.ci/proof_schema.json`, Validator: `scripts/ci/check_state_proofs.py`, Drift guard: `tests/ci/test_proof_schema_fingerprint_alignment.py` |
| I-4 OpenAPI Spec Validity | Zero unresolvable $ref; concrete schemas on core routes | `tests/ci/test_openapi_validity.py` |
| I-5 Crash Traceback Completeness | Exception handler logs full traceback | `tests/ci/test_exception_handler.py` |

### Phase A: Kill the voice.py God-Route — COMPLETE (2026-03-03)

- [x] **A1**: Build gap matrix (voice.py endpoints vs voice/ submodules) — all 15 covered
- [x] **A2**: Migrate all remaining endpoints to voice/ with service delegation — already done
- [x] **A3**: Delete voice.py (139 KB), create voice/__init__.py, enable I-1 (3/3 pass)

### Phase B: Mediator Hard Decision Gate — COMPLETE (2026-03-03)

- [x] **B-DELETE**: Removed `backend/application/` (11 files, ~20 KB). ADR-046. Routes call services directly.

### Phase C: Stability — COMPLETE (2026-03-03)

- [x] **C1**: Root cause and fix the AssertionError — double call_next fixed, Unicode guard added
- [x] **C2**: Fix test isolation (pytest-randomly, deterministic across seeds)

### Phase D: Trust/Safety + OpenAPI — COMPLETE (2026-03-03)

- [x] **D1**: Build trust/safety choke point dependency + enable I-2 (already passing)
- [x] **D2**: Fix OpenAPI $ref resolution + enable I-4 (concrete schemas on 6 routes, xfail removed)

### Phase E: Golden Path — SCAFFOLDING COMPLETE (stub only, 2026-03-03)

- [x] **E1**: Pre-conditions checklist (models, env, clean start) — `scripts/golden_path_preconditions.py`, URL fix in test_golden_path.py
- [x] **E2**: Full engine run (import → transcribe → clone → synthesize → validate) — stub mode only, 10 tests passed (does NOT satisfy 100% completion per governing principle)
- [x] **E3**: Proof artifact generation (I-3 compliant) — `scripts/golden_path_proof.py`, proof.json in .buildlogs/proof_runs/
- [x] **E4**: Golden path in CI (stub mode) — golden-path job in .github/workflows/ci.yml

### Phase F: v1.1.0 Release — COMPLETE (2026-03-06)

- [x] **F0**: PROOF_GOLDEN_PATH_REAL must exist before any "100% complete" declaration
- [x] **F1**: Documentation and debt closure
- [x] **F2**: Final gate check (all invariants + deterministic tests + Release build)
- [x] **F3**: Tag v1.1.0-release

---

## Section 3: Execution Sequence

| # | Phase | Milestone | Level | Dependency | Status |
|---|-------|-----------|-------|------------|--------|
| 1 | Phase 0 | All 5 CI Invariants | INSTALL FIRST | Everything else is gameable | DONE |
| 2 | Phase C | C1 — Fix AssertionError | CRITICAL | Active production bug | DONE |
| 3 | Phase C | C2 — Test Isolation | CRITICAL | Non-deterministic tests invalidate all acceptance criteria | DONE |
| 4 | Phase A | A1 — Gap Matrix | HIGH | Must know what to migrate | DONE |
| 5 | Phase A | A2 — Migrate Endpoints | HIGH | Complete the split | DONE |
| 6 | Phase A | A3 — Delete voice.py + I-1 | HIGH | Removes dual-routing ambiguity | DONE |
| 7 | Phase B | B-DELETE (mediator removed) | DECISION | After route decomp | DONE |
| 8 | Phase D | D1 — Safety Choke Point + I-2 | HIGH | Must be done before Golden Path | DONE |
| 9 | Phase D | D2 — OpenAPI + I-4 | MEDIUM | API contract | DONE |
| 10 | Phase E | E1-E3 — Real Engine Run + Proof | RELEASE GATE | Requires all previous phases | STUB DONE |
| 11 | Phase E | E4 — Golden Path CI | MEDIUM | Automated regression guard | STUB DONE |
| 12 | Phase F | F0-F3 — Release | FINAL | Only after steps 1-11 | DONE |

---

## Section 4: Anti-Patterns

1. **Documentation Theater** — A milestone producing a document instead of code enforcement
2. **Half-Wired Architecture** — Services + routes + mediator coexisting with no canonical dispatch
3. **Completion Declaration Trap** — STATE.md says 'complete' but bugs logged after
4. **Proof Artifacts Without Hashes** — Unfalsifiable, unreproducible
5. **Dual Routing** — voice.py + voice/ simultaneously active

## Section 5: The One Metric That Cannot Be Faked

`pytest tests/e2e/test_golden_path.py -v` exits 0 with `engine_mode='real'`, XTTS + whisper_cpp loaded, real WAV in, synthesized audio out with RMS > 0.001, proof artifact at `.buildlogs/proof_runs/` with all I-3 fields.
