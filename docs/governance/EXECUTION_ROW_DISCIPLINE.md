# Execution row discipline (bounded lanes)

**Status:** Canonical governance (2026-04-07).  
**Note:** Cursor rule files under `.cursor/rules/` are **user-owned**; this document is the repo’s **canonical** execution-row contract until or unless the same text is adopted into rules with explicit user approval.

## 1. Row type (required at freeze)

Every execution row MUST declare one of:

| Type | Meaning |
|------|---------|
| **runtime-affecting** | Production code, tests, schemas, or installer behavior may change. |
| **proof-hardening** | **No production behavior change** — documentation, tracker/registry/STATE, or test-only locks for already-shipped behavior. |

**Proof-hardening** rows MUST state explicitly: *“No production code paths changed in this lane.”* (Test-only changes are allowed if they add assertions for existing behavior.)

## 2. Failure-path parity (closure gate)

A lane MUST NOT be marked **Closed** unless:

- The **happy path** is specified in the execution row or closure report, and
- The **failure / degraded path** is specified with equal rigor (what the operator sees, what is rolled back, what events fire or do not fire, what consumers do).

## 3. Allowlist commits

- Stage **only** paths listed on the row’s allowlist (plus unavoidable typo fixes in the same files).
- **STATE.md** only when closure-grade sync is complete in the same commit.
- One bounded lane SHOULD correspond to **one behavior narrative** so **revert** removes one story, not several.

## 4. Honest limits as backlog

Closure report **Honest limits** / **Hard OUT** bullets are **first-class backlog** inputs for the next lane queue — prefer them over undisciplined feature brainstorming.

## 5. Pre-existing code

If runtime code existed before the row was frozen, the row MUST say:

- what **pre-existed**,
- what was **newly validated** or **changed** in this lane.

## 6. Runtime proof requirement (required at freeze)

Every execution row MUST include a **Runtime proof requirement** subsection that checks exactly one of:

| Checkbox | When to use |
|----------|-------------|
| **Fresh Grade R proof required** | The lane changes synthesis, training, startup, export, or health **product** paths (not only tests/docs). |
| **Inherited Grade R proof required** | The lane changes product code but not the paths above; the closure must cite the most recent Grade R proof artifact within the policy window (default **72 hours** unless the row states otherwise). |
| **No Grade R proof** | Proof-hardening rows, or governance/CI-only rows that do not change production behavior. |

Proof grades **S / I / R** are defined in [TEST_CLASSIFICATION.md](TEST_CLASSIFICATION.md).

### Closure gate (runtime-affecting lanes with Fresh or Inherited Grade R)

The closure report MUST include a **Runtime Proof** section containing:

- Commands executed (exact `verify.ps1` flags, pytest paths, or scripted smokes),
- Artifact paths (`artifacts/verify/...`, `docs/reports/verification/PROOF_*.json`, `%LOCALAPPDATA%\VoiceStudio\crashes\*.json`, etc.),
- Pass/fail and timestamp,
- For **inherited** proof: citation of the prior artifact and confirmation it is within the policy window.

Lanes with **No Grade R proof** must state that explicitly and must not claim full-stack operability from seam tests alone.

### Callable Grade R bundle (optional)

For engine-backed synthesis + training export honesty without running the full UI stack, use:

`.\scripts\verify.ps1 -RuntimeProof`

(`-Quick` is forbidden with `-RuntimeProof`; see `scripts/verify.ps1` header.)

### Enforcement levels (GAP-015 slice 2)

| Context | Grade-R artifact enforcement | Command |
|--------|------------------------------|---------|
| Quick verify | None (no staleness gate) | `.\scripts\verify.ps1 -Quick` |
| Full verify (default) | **Advisory** — `runtime_proof_staleness` runs in `run_verification.py` and is visible in Stage 9 output; does **not** fail the harness | `.\scripts\verify.ps1` |
| Full verify + hard gate | **Enforce** — missing or stale `PROOF_GOLDEN_PATH_REAL_*.json` (per 72h policy) **fails** Stage 9 | `.\scripts\verify.ps1 -EnforceRuntimeProof` |
| Rolling verifier (default) | Advisory — `runtime_proof_staleness` **and** `slo_baseline_freshness` (GAP-015 slice 3; newest `slo_baselines.json` under `artifacts/verify/*/` or `docs/reports/verification/slo_baselines*.json`, 72h window); both rows `passed: True` unless `--enforce-runtime-proof` (staleness only) | `python scripts/run_verification.py` |
| Rolling verifier + enforce | Hard fail on missing/stale proof | `python scripts/run_verification.py --enforce-runtime-proof` |
| Release / closure (human process) | Runtime-affecting lanes that declare **Fresh** or **Inherited** Grade R must attach proof within the policy window; reviewers run enforce mode before approving | Same as enforce rows above |
| Standalone runtime proof | `runtime_proof.json` **schema v2** — `PASS` / `FAIL` / `BLOCKED` (exit **0** / **1** / **2**); prerequisite probe: `scripts/ci/check_runtime_prerequisites.py`; sibling **`slo_baselines.json` schema v1** from `scripts/ci/write_slo_baseline_proof.py` (advisory baselines; no threshold enforcement) | `.\scripts\verify.ps1 -RuntimeProof` |

**Definitions:** **FRESH** = newest `docs/reports/verification/PROOF_GOLDEN_PATH_REAL_*.json` is ≤ **72 hours** old; **STALE** = older than 72h; **MISSING** = no such files. **BLOCKED** = cannot honestly run real-mode pytest (missing pytest, consent import failure, or no Piper manifest under `engines/`).

**GAP-015 slice 3 (closed):** Percentile **sample** baselines for three ASGI workflows live in `slo_baselines.json`; `slo_baseline_freshness` in `run_verification.py` mirrors the 72h advisory pattern and does **not** enforce thresholds. **Hard OUT** remains: no telemetry warehouse, dashboards, or CI gating on SLOs.

## 7. Closure report doctrine (runtime-affecting lanes)

Every **runtime-affecting** lane closure report MUST make the following explicit (in addition to §2 failure-path parity and §6 runtime proof when applicable):

1. **Grade S / I / R** — What was proven at each grade per [TEST_CLASSIFICATION.md](TEST_CLASSIFICATION.md) (seam vs integration vs real stack).
2. **Grade R: inherited vs fresh** — If the row declared **Inherited Grade R proof required**, cite the prior artifact path and timestamp and confirm it is within the policy window (default **72h**). If **Fresh Grade R proof required**, cite the new artifact from `.\scripts\verify.ps1 -RuntimeProof` (or the row’s stated substitute) and its outcome. If the row declared **No Grade R proof**, state that and do not imply full-stack operability from lower grades alone.
3. **SLO posture** — State whether `slo_baselines.json` / `slo_baseline_freshness` was **informational / advisory only** for this closure (default after GAP-015 slice 3) or whether a future umbrella explicitly changed gating. Silent omission is not allowed for lanes that ran the rolling verifier or `-RuntimeProof` sibling SLO emission.

**Proof-hardening** rows (§1) must state **No production code paths changed** and typically declare **No Grade R proof**; they still benefit from a one-line SLO posture note if rolling verify output is cited.

## 8. Windows CI verify-harness (GitHub Actions)

**Workflow file:** [`.github/workflows/verify-harness.yml`](../.github/workflows/verify-harness.yml) — **Verify Harness (Checkpoint + Resume)**.

| Trigger | What runs |
|--------|-----------|
| `workflow_dispatch` with `run_full_chain: true` (default) | Job **Verify Quick Gate** (`verify.ps1 -Quick`), then **Verify Checkpoint + Resume Chain** (`-StopAfterStage "C# Unit Tests - Other"` → lineage echo → `-ResumeFrom "Python Unit Tests"` → `python scripts/run_verification.py`). |
| `workflow_dispatch` with `run_full_chain: false` | **Verify Quick Gate** only. |
| `schedule` (weekly Monday 06:00 UTC) | Same as full chain dispatch. |
| `push` to `main` touching harness paths (`scripts/verify.ps1`, `*-failure-smoke.ps1`, `scripts/ci/**`, this workflow, `show-checkpoint-lineage.ps1`) | **Verify Quick Gate** only (checkpoint/resume is not run on every push; cost control). |

**Runner:** `windows-latest` (WinUI build requires Windows).

**Interpreting results**

- **Checkpoint + resume** proves junction/`checkpoint.json`/`latest_pointer.json` lineage and stage inheritance; that is the primary CI signal.
- **UI / Failure-Path Smoke** stages may fail or behave differently on headless hosted runners (no display, WinUI runtime gaps). Treat those as **environment caveats** unless the failure reproduces locally with the same `verify.ps1` flags.
- **Artifacts:** `verify-quick-artifacts` and `verify-checkpoint-resume-artifacts` (default retention **7 days**). Download with GitHub UI or: `gh run download <run-id> --dir artifacts/ci-harness-run/`.

**Operator tooling:** `.\scripts\show-checkpoint-lineage.ps1` — prints junction target vs checkpoint vs `latest_pointer.json` before a manual `-ResumeFrom`.

**Branch protection:** Optional status check **`verify-quick`** for PRs that touch harness paths; do **not** make the full checkpoint+resume job a required check on every PR (runtime cost). Configure in repo **Settings → Branches** if desired.

## 9. References

- Gap tracker: [PROFESSIONAL_GAP_TRACKER.md](../design/PROFESSIONAL_GAP_TRACKER.md)
- Proof grades: [TEST_CLASSIFICATION.md](TEST_CLASSIFICATION.md) (Grade S / I / R)
- Panel / architecture guardrails: [GUARDRAILS.md](../design/GUARDRAILS.md) (includes transcript mutation outcome taxonomy)
- Document governance: [DOCUMENT_GOVERNANCE.md](DOCUMENT_GOVERNANCE.md)
- CI verify-harness: §8 above; workflow [`.github/workflows/verify-harness.yml`](../.github/workflows/verify-harness.yml)
