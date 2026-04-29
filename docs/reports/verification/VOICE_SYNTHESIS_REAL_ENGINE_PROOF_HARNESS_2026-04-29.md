# Voice Synthesis Real-Engine Proof Harness — 2026-04-29

**Gate note:** Filenames containing `PROOF_HARNESS` are **excluded** from the `voice_synthesis_proof_boundary` validator (this is tooling documentation, not a synthesis proof artifact).

**Classification:** N/A (meta / tooling report; not a synthesis proof artifact)  
**Purpose:** Document the automated **producer** harness and **consumer** validator hardening for voice-synthesis proof reports.

---

## Scope

- **In scope:** `scripts/proof/run_voice_synthesis_real_engine_proof.py`, residual rules in `scripts/ci/check_voice_synthesis_proof_boundary.py`, unit tests, optional opt-in integration marker, `run_verification.py` dry-run gate, documentation.
- **Out of scope:** Human/operator proof, GAP-008 / Slice 46 / `MainWindow*ShellBridge`, RHVoice, `ENGINE_PARITY_MATRIX`, cloud services, git push.

---

## Validator vs harness

| Role | Path | Responsibility |
|------|------|------------------|
| **Consumer / gate** | `scripts/ci/check_voice_synthesis_proof_boundary.py` | Validates Markdown under `docs/reports/verification/` (changed-from, `--all`, `--json`, `--self-test-examples`). |
| **Producer** | `scripts/proof/run_voice_synthesis_real_engine_proof.py` | Writes JSON + Markdown; **always** runs `validate_report()` on produced Markdown before exit 0. |

---

## Residual validator rules (high level)

- **Metadata:** single `VOICESTUDIO_PROOF_BOUNDARY_V1` block; no duplicate keys; `classification` / `proof_type` / `engine_mode_source` must use allowed vocabularies.
- **`operator_claim` / `runtime_claim`:** when `true`, require matching evidence phrases **outside** Non-Claims sections.
- **REAL_ENGINE:** require explicit **non-JSON-error-body** audio evidence (e.g. `binary audio`, `does not start with {`, `not a JSON error body`).
- **Library / timeline:** positive and negative evidence evaluated on text **outside** Non-Claims; broad “maybe library” fallbacks removed.

---

## CLI (harness)

```powershell
# Dry-run: no backend; writes fixtures under output dir; validates Markdown
python scripts/proof/run_voice_synthesis_real_engine_proof.py --dry-run-fixtures --output-dir artifacts/proof_harness_selftest

# Stub path (VOICESTUDIO_TEST_MODE in {1,true,yes,stub})
$env:VOICESTUDIO_TEST_MODE = "1"
python scripts/proof/run_voice_synthesis_real_engine_proof.py --output-dir artifacts/proof_harness_out

# Real path (local FastAPI); optional base URL
python scripts/proof/run_voice_synthesis_real_engine_proof.py --base-url http://127.0.0.1:8000 --output-dir artifacts/proof_harness_out

# Fail if classification is not REAL_ENGINE
python scripts/proof/run_voice_synthesis_real_engine_proof.py --require-real
```

**Flags:** `--base-url`, `--engine`, `--profile-id`, `--session-id`, `--output-dir`, `--json-output`, `--markdown-output`, `--require-real`, `--dry-run-fixtures`, `--timeout-seconds`, `--help`.

---

## Classification behavior (real mode)

- **STUB_ENGINE:** `VOICESTUDIO_TEST_MODE` gate; skips HTTP synthesis; compliant Markdown; exit **1** only with `--require-real`.
- **UNKNOWN:** health/readiness/profiles/synthesis/audio/library/timeline failures; Markdown lists **Blocked:** with blocker text (UNKNOWN blocker rule satisfied).
- **REAL_ENGINE:** full chain succeeded; `routed_engine` non-stub; WAV bytes with RIFF/WAVE; library HTTP 201; timeline revision + clip placement.

---

## Verification commands (recorded)

```powershell
python -m pytest tests/unit/scripts/ci/test_voice_synthesis_proof_boundary.py -q
python -m pytest tests/unit/scripts/proof/test_voice_synthesis_real_engine_proof_harness.py -q
python scripts/ci/check_voice_synthesis_proof_boundary.py --self-test-examples
python scripts/proof/run_voice_synthesis_real_engine_proof.py --dry-run-fixtures --output-dir artifacts/proof_harness_selftest
python scripts/run_verification.py
.\scripts\verify.ps1 -Quick
```

**Recorded (2026-04-29):** `python scripts/run_verification.py` **PASS**; `.\scripts\verify.ps1 -Quick` **PASS** — `artifacts/verify/20260429_160337/verification_report.md`; `.buildlogs/verification/last_run.json`.

---

## Non-claims (this document)

- This file is **not** a substitute for a `REAL_ENGINE` proof under `docs/reports/verification/` with runtime measurements.
- **`--all`** on the validator may still fail on **historical** reports that predate stricter rules; the harness dry-run validates **newly generated** fixtures only.

---

## Opt-in integration

- Marker: `real_voice_synthesis_proof` (registered in `pytest.ini`; excluded from default `addopts`).
- Env: `VOICESTUDIO_RUN_REAL_ENGINE_PROOF=1` to run `tests/integration/test_voice_synthesis_real_engine_proof_harness.py`.
- Base URL: `VOICESTUDIO_REAL_ENGINE_PROOF_BASE` (optional; default `http://127.0.0.1:8000`).

---

## Changed / added files (engineering list)

- `scripts/ci/check_voice_synthesis_proof_boundary.py`
- `tests/unit/scripts/ci/test_voice_synthesis_proof_boundary.py`
- `scripts/proof/run_voice_synthesis_real_engine_proof.py`
- `scripts/proof/__init__.py`
- `tests/unit/scripts/proof/test_voice_synthesis_real_engine_proof_harness.py`
- `tests/integration/test_voice_synthesis_real_engine_proof_harness.py`
- `scripts/run_verification.py`
- `pytest.ini`
- `docs/developer/VOICE_SYNTHESIS_PROOF_REPORTING_STANDARD.md`
- `docs/templates/VOICE_SYNTHESIS_PROOF_REPORT_TEMPLATE.md` (if updated)
- `docs/governance/CANONICAL_REGISTRY.md`
- `.cursor/STATE.md` (LATEST PROOF INDEX row only)

---

## Related

- [VOICE_SYNTHESIS_PROOF_REPORTING_STANDARD.md](../developer/VOICE_SYNTHESIS_PROOF_REPORTING_STANDARD.md)
- [VOICE_SYNTHESIS_PROOF_REPORT_TEMPLATE.md](../templates/VOICE_SYNTHESIS_PROOF_REPORT_TEMPLATE.md)
