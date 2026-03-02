# Proof Index — Next 3 Steps (2026-03-02)

**Purpose:** Single source of truth for proof artifacts referenced by STATE.md "Next 3 Steps". Every DONE claim must have a proof file in this repo.

**Schema:** All proofs must conform to [PROOF_SCHEMA.md](PROOF_SCHEMA.md). CI fails on schema violation.

---

## 1. Provenance Policy and Usage Accounting (DONE 2026-03-01)

**What changed:** provenance_policy.py, record_artifact_provenance_and_usage policy-aware, usage in registration pipeline, 15 tests pass, 0 route handlers call record_synthesis_minutes/write_provenance_sidecar directly.

**How verified:**
```bash
python -m pytest tests/unit/test_audio_artifact_provenance.py tests/unit/test_audio_artifact_use_cases.py -q --tb=short
```

**Proof files:**
- `docs/reports/verification/PROOF_PROVENANCE_2026-03-02.json`

**Produce proof:**
```bash
python scripts/ci/write_provenance_proof.py
```

---

## 2. Gate C Publish + UI Smoke (DONE 2026-03-02)

**Command:**
```powershell
.\scripts\gatec-publish-launch.ps1 -Configuration Release -RuntimeIdentifier win-x64 -UiSmoke -UiSmokeTimeoutSeconds 120
```

**How verified:** Exit 0; ui_smoke_summary.json shows exit_code 0, binding_failure_count 0.

**Proof files:**
- `docs/reports/verification/PROOF_GATE_C_2026-03-02.json`

**Produce proof:** Run gatec above, then:
```powershell
.\scripts\ci\copy_gatec_proof.ps1
```

---

## 3. Installer Lifecycle Gate H (DONE 2026-03-02)

**Command:** Run as Administrator:
```powershell
.\installer\test-installer-lifecycle.ps1 -LogDir ".buildlogs/installer-lifecycle"
```

**7 steps:** Install V1, Launch V1, Upgrade to V2, Launch V2, Rollback, Launch V1, Uninstall.

**Proof files:**
- `docs/reports/verification/PROOF_INSTALLER_2026-03-02.json`

**Produce proof:** Run lifecycle test above, then:
```powershell
.\scripts\ci\copy_installer_proof.ps1 -LogDir ".buildlogs/installer-lifecycle"
```

---

## 4. Repo Payload Detox (M8, DONE 2026-03-02)

**What changed:** Large files moved out of repo; check_repo_payloads enforces policy.

**Proof files:**
- `docs/reports/verification/PROOF_PAYLOAD_DETOX_2026-03-02.json`

**Produce proof:**
```bash
python scripts/ci/write_payload_detox_proof.py
```

---

## CI Enforcement

`scripts/ci/check_state_proofs.py` runs in build-backend. It parses STATE.md, extracts Proof: paths, validates schema and semantics (exit_code, timestamp, git_commit), and exits 1 if any referenced file is missing or invalid.
