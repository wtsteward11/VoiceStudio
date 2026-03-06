# Local Runtime Gates Execution

Runtime gates require Windows, a running backend, and (for golden path real) real engines. They cannot run on CI Ubuntu. The CI workflow (`runtime-gates` job in `.github/workflows/ci.yml`) runs these on `workflow_dispatch` with `run_runtime_gates=true`.

## Local Execution Sequence

Run from repo root on Windows.

### 1. Start backend (separate terminal)

```powershell
python -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8000
```

### 2. Run each proof generator (in order)

```powershell
python scripts/ci/write_support_bundle_runtime_proof.py
python scripts/ci/write_perf_budget_runtime_proof.py
python scripts/ci/write_backend_cold_start_proof.py
python scripts/ci/write_golden_path_real_proof.py  # requires real engines (XTTS, whisper_cpp)
```

### 3. Validate all generated proofs

```powershell
python scripts/ci/check_state_proofs.py --validate-file "docs/reports/verification/PROOF_SUPPORT_BUNDLE_RUNTIME_*.json"
python scripts/ci/check_state_proofs.py --validate-file "docs/reports/verification/PROOF_PERF_BUDGET_RUNTIME_*.json"
python scripts/ci/check_state_proofs.py --validate-file "docs/reports/verification/PROOF_BACKEND_COLD_START_*.json"
python scripts/ci/check_state_proofs.py --validate-file "docs/reports/verification/PROOF_GOLDEN_PATH_REAL_*.json"
```

## CI Equivalent

Trigger via GitHub Actions:

```powershell
gh workflow run CI --field run_runtime_gates=true
.\scripts\ci\wait_for_gh_run.ps1 -Workflow "CI" -TimeoutMinutes 30
```
