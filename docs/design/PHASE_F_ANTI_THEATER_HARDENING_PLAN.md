# Phase F Anti-Theater Hardening Sprint — Execution Plan

**Sprint Name:** Phase F Anti-Theater Hardening Sprint (Proofs Must Be Unfakeable)  
**Sprint Goal:** Eliminate loopholes where CI/proofs can say "PASS" while doing nothing.

---

## 1) Discovery Steps (Mandatory)

Execute these steps **before** any implementation. Do not guess; verify every path and value.

### A) Gate C Proof — Discovery

| Step | Action | File(s) | What to Find |
|------|--------|---------|--------------|
| A1 | Open and read | `src/VoiceStudio.App/MainWindow.xaml.cs` | Locate `RunGateCUiSmokeNavigationAsync` (around line 653). Find the `steps` array and `workspaceSteps` array. |
| A2 | Count steps | Same file | Count entries in `steps` array: 8 primary nav + 4 core synthesis + 3 training + 4 audio + 3 utility + 3 voice = **25**. Count `workspaceSteps`: **2**. Total = **27**. |
| A3 | Derive MIN_NAV_STEPS | Same file | Code comment says "Primary navigation buttons (8 steps)". **MIN_NAV_STEPS = 8** (minimum for "real smoke"). If stricter: use 25 (all main loop steps). Document choice. |
| A4 | Open and read | `.ci/proof_schema.json` | Check `nested_semantics.PROOF_GATE_C`. Verify keys: `ui_smoke.exit_code`, `ui_smoke.nav_steps_completed_min`, `ui_smoke.binding_failure_count`, `ui_smoke.summary_path_required`, `ui_smoke.log_path_required`, `ui_smoke.summary_sha256_hex`, `ui_smoke.log_sha256_hex`. |
| A5 | Open and read | `scripts/ci/check_state_proofs.py` | Locate `validate_nested_semantics` block for `PROOF_GATE_C`. Verify it enforces: exit_code==0, nav_steps_completed>=MIN, binding_failure_count==0, summary_path/log_path exist, sha256 match files. |
| A6 | Open and read | `scripts/ci/copy_gatec_proof.ps1` | Verify it emits: `nav_steps_completed`, `binding_failure_count`, `exit_code`, `summary_path`, `log_path`, `summary_sha256`, `log_sha256`. Paths: `.buildlogs/x64/Release/gatec-publish/ui_smoke_summary.json`, `.buildlogs/x64/Release/gatec-publish/gatec-ui-smoke.log`. |
| A7 | Locate artifacts | `.buildlogs/x64/Release/gatec-publish/` | After running gatec: `ui_smoke_summary.json`, `gatec-ui-smoke.log` must exist. `scripts/gatec-publish-launch.ps1` writes them. |

### B) Golden Path STUB — Discovery

| Step | Action | File(s) | What to Find |
|------|--------|---------|--------------|
| B1 | Open and read | `scripts/ci/write_golden_path_stub_proof.py` | Check for `--no-run-test` flag. If present, remove it. Verify it runs pytest, captures stdout/stderr, hashes them, reads `output_manifest.json`, computes artifact metrics. |
| B2 | Open and read | `tests/integration/test_golden_path_e2e.py` | Find `TestGoldenPathOutputArtifact::test_golden_path_output_artifact`. Verify it requires `VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR`, fails (no skip) if missing, writes WAV + `output_manifest.json`. |
| B3 | Open and read | `.ci/proof_schema.json` | Check `type_specific.PROOF_GOLDEN_PATH_STUB.required` and `nested_semantics.PROOF_GOLDEN_PATH_STUB`. Must include: `test_ran`, `pytest_stdout_sha256`, `pytest_stderr_sha256`, `artifact_path`, `artifact_sha256`. Add `test_ran_must_be_true` if missing. |
| B4 | Open and read | `scripts/ci/proof_fingerprint.py` | Verify `EVIDENCE_FIELDS["PROOF_GOLDEN_PATH_STUB"]` includes all new fields. |

### C) Golden Path REAL — Discovery

| Step | Action | File(s) | What to Find |
|------|--------|---------|--------------|
| C1 | Open and read | `scripts/ci/write_golden_path_real_proof.py` | Locate line ~167: `output_dir = Path(tempfile.gettempdir()) / "voicestudio_golden_path"`. Locate `wav_files[-1]` — this picks "last" (stale). |
| C2 | Open and read | `tests/e2e/test_golden_path.py` | Find where it writes output WAV. Currently: `export_path = os.path.join(export_dir, "golden_path_export.wav")` with `export_dir = tempfile.gettempdir()/voicestudio_golden_path`. No `output_manifest.json`. |
| C3 | Verify | `tests/e2e/test_golden_path.py` | Test uses `VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR`? No. Must add: require env var, write to that dir, write `output_manifest.json`. |

### D) STATE Canonical — Discovery

| Step | Action | File(s) | What to Find |
|------|--------|---------|--------------|
| D1 | Open and read | `scripts/ci/check_state_proofs.py` | Line 32: `STATE_PATH = ROOT / ".cursor" / "STATE.md"`. Validator reads `.cursor/STATE.md`. |
| D2 | Open and read | `STATE.md` (repo root) | Current size, content. Contains full state (1400+ lines). Must become ≤2KB pointer. |
| D3 | Grep | `STATE.md` or `STATE` | `rg -l "STATE\.md" --type-add 'md:*.md'` to find docs that reference STATE. Update any that imply repo-root is canonical. |
| D4 | Open and read | `.cursor/STATE.md` | This is the canonical file. Repo-root `STATE.md` must point to it. |

### E) God-Object Growth — Discovery

| Step | Action | File(s) | What to Find |
|------|--------|---------|--------------|
| E1 | Get sizes | `src/VoiceStudio.App/Services/BackendClient.cs` | `(Get-Item ...).Length` → **186512** bytes. |
| E2 | Get sizes | `src/VoiceStudio.App/MainWindow.xaml.cs` | `(Get-Item ...).Length` → **133861** bytes. |
| E3 | Compute budgets | — | Budget = current + 5120 (5KB). BackendClient: 191632. MainWindow: 138981. |

### F) Schema/Fingerprint Drift — Discovery

| Step | Action | File(s) | What to Find |
|------|--------|---------|--------------|
| F1 | Open and read | `.ci/proof_schema.json` | `evidence_fields` section. List keys for PROOF_GATE_C, PROOF_GOLDEN_PATH_STUB, PROOF_GOLDEN_PATH_REAL. |
| F2 | Open and read | `scripts/ci/proof_fingerprint.py` | `EVIDENCE_FIELDS` dict. Compare to schema. They must match exactly. |

---

## 2) Exact Implementation Steps

### A) Gate C Proof — Implementation

**Current state (as of plan creation):** Schema and validator already enforce `nav_steps_completed_min: 8`, `summary_path`, `log_path`, `summary_sha256`, `log_sha256`. `copy_gatec_proof.ps1` already emits them. **Gap:** Existing `PROOF_GATE_C_2026-03-02.json` has `nav_steps_completed: 0` and lacks new fields. Regeneration required.

**Steps:**

1. **Verify MIN_NAV_STEPS:** Open `src/VoiceStudio.App/MainWindow.xaml.cs`, count steps in `RunGateCUiSmokeNavigationAsync`. Confirm 8 primary nav. If code changed, update `ui_smoke.nav_steps_completed_min` in `.ci/proof_schema.json` to match.
2. **Regenerate Gate C proof (Windows only):**
   ```powershell
   .\scripts\gatec-publish-launch.ps1 -Configuration Release -RuntimeIdentifier win-x64 -UiSmoke -UiSmokeTimeoutSeconds 120
   .\scripts\ci\copy_gatec_proof.ps1
   ```
3. **Validate:**
   ```powershell
   python scripts/ci/check_state_proofs.py --validate-file "docs/reports/verification/PROOF_GATE_C_*.json" --no-git-match
   ```
4. **Negative test (do NOT commit):** Edit proof JSON, set `nav_steps_completed: 0`. Run validator. **Must FAIL** with message containing "nav_steps_completed must be >= 8".

**Proof JSON fields (ui_smoke):**

- `nav_steps_completed` (int, >= 8)
- `binding_failure_count` (int, == 0)
- `exit_code` (int, == 0)
- `summary_path` (str, repo-relative)
- `log_path` (str, repo-relative)
- `summary_sha256` (str, 64 hex)
- `log_sha256` (str, 64 hex)

**Hashes:** `Get-FileHash -Algorithm SHA256` on `ui_smoke_summary.json` and `gatec-ui-smoke.log`. Store hex (lowercase). **Size:** Paths + hashes only. No full log content. Proof stays < 50KB.

---

### B) Golden Path STUB — Implementation

**Current state:** `--no-run-test` removed. Test `test_golden_path_output_artifact` exists, requires `VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR`, writes WAV + `output_manifest.json`. Writer creates output dir, runs pytest, reads manifest, computes metrics.

**Steps:**

1. **Schema:** In `.ci/proof_schema.json`, ensure `type_specific.PROOF_GOLDEN_PATH_STUB.required` includes: `test_ran`, `pytest_stdout_sha256`, `pytest_stderr_sha256`, `artifact_path`, `artifact_sha256`. Add to `evidence_fields.PROOF_GOLDEN_PATH_STUB`.
2. **Schema nested_semantics:** Add `test_ran_must_be_true: true`. Add `artifact_path_exists: true`, `artifact_sha256_matches_file: true`.
3. **Validator:** In `scripts/ci/check_state_proofs.py`, extend `PROOF_GOLDEN_PATH_STUB` block: require `test_ran is True`; require `pytest_stdout_sha256` and `pytest_stderr_sha256` are 64-hex; require `artifact_path` exists on disk; require `artifact_sha256` matches file at `artifact_path`.
4. **Fingerprint:** In `scripts/ci/proof_fingerprint.py`, ensure `EVIDENCE_FIELDS["PROOF_GOLDEN_PATH_STUB"]` includes all new fields. Must match schema.
5. **Regenerate:**
   ```powershell
   python scripts/ci/write_golden_path_stub_proof.py
   ```
6. **Validate:**
   ```powershell
   python scripts/ci/check_state_proofs.py --validate-file "docs/reports/verification/PROOF_GOLDEN_PATH_STUB_*.json" --no-git-match
   ```
7. **Negative test (do NOT commit):** Edit proof, set `test_ran: false`. Validator **must FAIL**. Corrupt `artifact_sha256`. Validator **must FAIL**.

**Hashes:** SHA-256 of pytest stdout bytes, stderr bytes, and artifact file. Store hex only. **Size:** Hashes + paths. No full stdout/stderr. Proof < 50KB.

---

### C) Golden Path REAL — Implementation

**Current state:** `write_golden_path_real_proof.py` uses `tempfile.gettempdir()/voicestudio_golden_path`, globs `*.wav`, takes `wav_files[-1]`. No manifest.

**Steps:**

1. **Modify `tests/e2e/test_golden_path.py`:**
   - At start of test class or first test, check `os.environ.get("VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR")`. If missing or not a dir: `pytest.fail("VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR required")`. No skip.
   - In `test_step5_validate_output`, when writing exported WAV: write to `os.environ["VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR"]`, not `tempfile.gettempdir()/voicestudio_golden_path`.
   - After writing WAV, write `output_manifest.json` in same dir: `{"output_wav": "golden_path_export.wav"}` (or actual filename).
2. **Modify `scripts/ci/write_golden_path_real_proof.py`:**
   - Create output dir: `.buildlogs/proof_runs/golden_path_real_{UTCtimestamp}_{gitsha8}/`
   - Set `os.environ["VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR"] = str(output_dir)` before running pytest.
   - Run pytest with `capture_output=True`. Hash stdout, stderr.
   - After pytest: read `output_dir / "output_manifest.json"`. Get `output_wav` filename. Resolve path: `output_dir / output_wav`.
   - Compute WAV metrics and SHA-256 from that exact file. Do NOT glob temp.
   - Add to proof: `artifact_path`, `artifact_sha256`, `artifact_bytes`, `pytest_stdout_sha256`, `pytest_stderr_sha256`.
3. **Schema:** Add same required fields and nested_semantics as stub (artifact_path, artifact_sha256, pytest hashes).
4. **Fingerprint:** Add new fields to `EVIDENCE_FIELDS["PROOF_GOLDEN_PATH_REAL"]`.
5. **Regenerate (machine with models):**
   ```powershell
   python scripts/ci/write_golden_path_real_proof.py
   ```
6. **Validate:**
   ```powershell
   python scripts/ci/check_state_proofs.py --validate-file "docs/reports/verification/PROOF_GOLDEN_PATH_REAL_*.json" --no-git-match
   ```
7. **Negative test (do NOT commit):** Corrupt `artifact_sha256`. Validator **must FAIL**.

---

### D) STATE Canonical — Implementation

**Steps:**

1. **Create `tests/ci/test_state_canonical_location.py`:**
   - Test 1: `repo_root_state = ROOT / "STATE.md"`. If exists: `assert repo_root_state.stat().st_size <= 2048`, fail with "repo-root STATE.md must be ≤2KB pointer".
   - Test 2: Assert repo-root STATE.md does NOT contain line matching `Proof:` or `Proof Index` (i.e. no proof entries). Fail with "Proof entries belong in .cursor/STATE.md".
   - Test 3: `cursor_state = ROOT / ".cursor" / "STATE.md"`. `assert cursor_state.exists()`, fail with ".cursor/STATE.md must exist (canonical state)".
   - Test 4: Repo-root STATE.md content must contain string "canonical" and ".cursor/STATE.md" (pointer).
2. **Replace repo-root `STATE.md`:** Overwrite with pointer (≤2KB):
   ```markdown
   # VoiceStudio State — Pointer

   **Canonical state file:** [.cursor/STATE.md](.cursor/STATE.md)

   This repo-root file is a pointer only. All session state, proof index, and active task live in `.cursor/STATE.md`. Scripts (e.g. `check_state_proofs.py`) read `.cursor/STATE.md`.
   ```
3. **Grep and update docs:** `rg "STATE\.md" -g "*.md"` — update any doc that says "repo-root STATE.md" is what validators use. Replace with ".cursor/STATE.md".

---

### E) God-Object Growth — Implementation

**Steps:**

1. **Create `tests/ci/test_file_size_budgets.py`:**
   - Define: `BACKEND_CLIENT_BUDGET = 191632`  # 186512 + 5120
   - Define: `MAINWINDOW_BUDGET = 138981`     # 133861 + 5120
   - Paths: `src/VoiceStudio.App/Services/BackendClient.cs`, `src/VoiceStudio.App/MainWindow.xaml.cs`
   - For each: `size = path.stat().st_size`; `assert size <= BUDGET`, else fail with "This file is a god-object. You exceeded the budget. Split using partial classes or typed clients. Do not grow it blindly."
2. **Document budgets in test:** Add docstring with current sizes and allowance (5KB).

---

### F) Schema/Fingerprint Drift — Implementation

**Steps:**

1. **Create `tests/ci/test_proof_schema_fingerprint_alignment.py`:**
   - Load `ROOT / ".ci" / "proof_schema.json"`.
   - Import `from scripts.ci.proof_fingerprint import EVIDENCE_FIELDS`.
   - For each proof type in schema `evidence_fields`: `schema_keys = set(schema["evidence_fields"][pt])`; `fp_keys = set(EVIDENCE_FIELDS.get(pt, []))`; `assert schema_keys == fp_keys`, else fail with diff: `missing_in_fp = schema_keys - fp_keys`, `extra_in_fp = fp_keys - schema_keys`.
   - Proof types to check: PROOF_GATE_C, PROOF_GOLDEN_PATH_STUB, PROOF_GOLDEN_PATH_REAL (at minimum).

---

## 3) Commit Slicing (Max 3 Files Per Commit)

| # | Commit Title | Files (max 3) | Acceptance Criteria |
|---|--------------|---------------|---------------------|
| 1 | fix(ci): Gate C proof semantics — nav_steps, artifacts, hashes | `.ci/proof_schema.json`, `scripts/ci/check_state_proofs.py`, `scripts/ci/copy_gatec_proof.ps1` | Schema enforces nav>=8, paths, hashes. Copy script emits them. Validator enforces. |
| 2 | fix(ci): Stub proof non-fakeable — test_ran, artifact hashes | `scripts/ci/write_golden_path_stub_proof.py`, `tests/integration/test_golden_path_e2e.py`, `scripts/ci/proof_fingerprint.py` | No --no-run-test. Proof has test_ran, pytest hashes, artifact path/hash. |
| 3 | fix(ci): Schema + validator stub/real fields, fingerprint alignment | `.ci/proof_schema.json`, `scripts/ci/check_state_proofs.py`, `tests/ci/test_proof_schema_fingerprint_alignment.py` | Schema requires new fields. Validator enforces. Alignment test passes. |
| 4 | fix(ci): Real proof deterministic — manifest, no temp scan | `scripts/ci/write_golden_path_real_proof.py`, `tests/e2e/test_golden_path.py`, `scripts/ci/proof_fingerprint.py` | Real test uses VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR, writes manifest. Writer reads manifest. |
| 5 | fix(ci): STATE canonical — pointer, CI test | `STATE.md`, `tests/ci/test_state_canonical_location.py`, (one doc from grep) | Repo-root STATE.md ≤2KB pointer. .cursor/STATE.md canonical. CI test enforces. |
| 6 | fix(ci): God-object file size budgets | `tests/ci/test_file_size_budgets.py` | Test fails if BackendClient or MainWindow exceeds budget. |

---

## 4) Validation Commands

**After each major step:**

```powershell
python -m pytest tests/ci/ -q --randomly-seed=12345
```

**After Gate C work:**

```powershell
python scripts/ci/check_state_proofs.py --validate-file "docs/reports/verification/PROOF_GATE_C_*.json" --no-git-match
```

**After Stub work:**

```powershell
python scripts/ci/write_golden_path_stub_proof.py
python scripts/ci/check_state_proofs.py --validate-file "docs/reports/verification/PROOF_GOLDEN_PATH_STUB_*.json" --no-git-match
```

**After Real work (machine with models):**

```powershell
python scripts/ci/write_golden_path_real_proof.py
python scripts/ci/check_state_proofs.py --validate-file "docs/reports/verification/PROOF_GOLDEN_PATH_REAL_*.json" --no-git-match
```

**Gate C regeneration (Windows):**

```powershell
.\scripts\gatec-publish-launch.ps1 -Configuration Release -RuntimeIdentifier win-x64 -UiSmoke -UiSmokeTimeoutSeconds 120
.\scripts\ci\copy_gatec_proof.ps1
```

---

## 5) Negative Tests (Do NOT Commit)

| Test | Action | Expected |
|------|--------|----------|
| Gate C nav=0 | Edit `PROOF_GATE_C_*.json`, set `ui_smoke.nav_steps_completed: 0`. Run validator. | FAIL: "nav_steps_completed must be >= 8" |
| Stub test_ran=false | Edit `PROOF_GOLDEN_PATH_STUB_*.json`, set `test_ran: false`. Run validator. | FAIL |
| Stub corrupt sha | Edit `artifact_sha256` to wrong 64-hex. Run validator. | FAIL: hash mismatch |
| Real corrupt sha | Edit `artifact_sha256` in real proof. Run validator. | FAIL |

---

## 6) Definition of Done

- [ ] All CI tests pass: `python -m pytest tests/ci/ -q --randomly-seed=12345`
- [ ] Gate C proof validation fails when `nav_steps_completed=0`
- [ ] Gate C proof validation fails when summary_path/log_path missing or hash mismatch
- [ ] Stub proof cannot be created without running test (no --no-run-test; test_ran enforced)
- [ ] Stub proof includes pytest stdout/stderr hashes and artifact path/hash; validator enforces
- [ ] Real proof uses output_manifest.json; no temp dir scan
- [ ] Real proof includes artifact path/hash; validator enforces
- [ ] Repo-root STATE.md is ≤2KB pointer to .cursor/STATE.md
- [ ] CI test fails if repo-root STATE.md contains Proof entries or exceeds 2KB
- [ ] CI test fails if .cursor/STATE.md missing
- [ ] File size budget test exists and passes for BackendClient and MainWindow
- [ ] Schema/fingerprint alignment test exists and passes for Gate C, Stub, Real

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-04 | Initial plan created |
