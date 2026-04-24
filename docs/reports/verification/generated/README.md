# Generated verification artifacts

Do not hand-edit files in this directory; regenerate after manifest or override changes.

## Git tracking policy (Tasks 226–227)

**Tracked in git (canonical for clones and CI):** this `README.md`, `engine_truth.json`, and `engine_truth_v2.json`. Regenerate with `python scripts/generate_engine_truth.py --schema all` after manifest or `engine_truth_overrides.json` changes; commit the resulting JSON in the **same** change-set as override/matrix updates when those rows move.

**Local-only (optional):** `stt_hardening_regress_summary.json` — produced by `.\scripts\stt_hardening_regress.ps1`. [`test_stt_hardening_regress_summary_schema.py`](../../../../tests/unit/scripts/test_stt_hardening_regress_summary_schema.py) **skips** when the file is absent; do not expect a green STT-pack schema test on a bare clone until that script has been run once.

| File | Schema | Producer |
| --- | --- | --- |
| `engine_truth.json` | `voicestudio.engine_truth.v1` | `python scripts/generate_engine_truth.py` (default) |
| `engine_truth_v2.json` | `voicestudio.engine_truth.v2` | `python scripts/generate_engine_truth.py --schema v2` (or `python scripts/generate_engine_truth.py --schema all` for v1+v2 in one process) |
| `stt_hardening_regress_summary.json` | `schema_version: 1` (STT pack run metadata) | `.\scripts\stt_hardening_regress.ps1` (after pytest + engine truth v1/v2); schema asserted by `tests/unit/scripts/test_stt_hardening_regress_summary_schema.py` |

## STT pack truth surface (Tasks 78–80)

**Canonical execution record:** After a green `.\scripts\stt_hardening_regress.ps1` run, treat this file as the **only** authority for how many pytest items passed (`passed_count`), which test modules ran (`pytest_args`), and generator exit codes. **Do not** type ad-hoc counts (“42 passed”, “45 passed”, etc.) into [`.cursor/STATE.md`](../../../../.cursor/STATE.md), [PROOF §27](../PROOF_SLICE27_WHISPER_CPP_TRANSCRIPT.md), or [ENGINE_PARITY_MATRIX.md](../ENGINE_PARITY_MATRIX.md) — reviewers and automation read **`passed_count`** / **`timestamp_utc`** here.

**Contract:** [`tests/unit/scripts/stt_pack_required_targets.py`](../../../../tests/unit/scripts/stt_pack_required_targets.py) lists the pytest paths the script must run; [`tests/unit/scripts/test_stt_hardening_regress_summary_schema.py`](../../../../tests/unit/scripts/test_stt_hardening_regress_summary_schema.py) asserts the committed JSON’s `pytest_args` matches that set and that `passed_count` agrees with the last `N passed` line captured in `pytest_stdout_tail`.

## Verify bar vs ad-hoc `verify.ps1` (Tasks 96)

**Canonical bar:** [`.cursor/STATE.md`](../../../../.cursor/STATE.md) **Latest verify artifact** must match `defaults.latest_verify_artifact` in [`tools/overseer/data/engine_truth_overrides.json`](../../../../tools/overseer/data/engine_truth_overrides.json) and each curated row in **`engine_truth_v2.json`** — enforced by [`tests/unit/scripts/test_engine_truth_verify_artifact_alignment.py`](../../../../tests/unit/scripts/test_engine_truth_verify_artifact_alignment.py).

A local **`.\scripts\verify.ps1 -Quick`** (or any verify run) **does not** change repo truth until the **same change-set** updates **STATE** + **overrides** + regenerated **`engine_truth_v2.json`** to the **same** `artifacts/verify/<stamp>/verification_report.md`. Do not narrate a “new Quick PASS” in governance prose unless that three-way path moved.

## Verify bar bump discipline (Tasks 96 + 104)

Bump **`defaults.latest_verify_artifact`** (and the matching **STATE** + **v2** rows) **only** when the change-set **introduces or updates canonical evidence** (PROOF, matrix, generated truth, or STATE verification claims) **and** anchors that batch to a **real** `verify.ps1` run you intend as the new bar.

Do **not** bump the verify bar for **internal-only** changes (for example: small test refactors, skip-helper extractions, or doc typo fixes) unless that same PR also carries the **new** verification artifact path and truth-surface updates tied to that run.

## Tasks 131–132 (Slice 27 batch — verify bar + RHVoice)

**Task 131** — Same rule as **Tasks 96 / 104 / 112 / 121**: doc-only / test-only / capture-tool-only PRs do **not** advance **`latest_verify_artifact`** or **STATE** “Latest verify artifact” without a **canonical runtime proof batch** (e.g. non-skip **`real_whisper_cpp`**) plus the matching **`verify.ps1`** anchor in the **same** change-set.

**Task 132** — **RHVoice freeze:** no churn under **`engines/audio/rhvoice/`** without **`rhvoice-cli`** on PATH or a valid manifest **`executable_path`** proof; do **not** bundle RHVoice edits with Slice 27 closure PRs.

## Task 140 (Slice 27 post-132) — verify bar unchanged on operator-only sessions

**Tasks 134–136** (fresh Uvicorn + **`real_whisper_cpp`** skip with committed `slice27/` artifacts) **do not** bump **`defaults.latest_verify_artifact`** or **STATE** “Latest verify artifact” **unless** the same change-set also lands **Task 138**-class proof (non-skip transcript PASS + §8 mechanical flip batch) **and** anchors **`verify.ps1`** to the new `artifacts/verify/<stamp>/` bar — same discipline as **Tasks 96 / 104 / 121 / 131**.

## v1 vs v2

- **v1** is a **manifest projection** only (`engine_id`, `subtype`, paths, `entry_point`, etc.).
- **v2** adds **curated operational fields** merged from [`tools/overseer/data/engine_truth_overrides.json`](../../../../tools/overseer/data/engine_truth_overrides.json): `readiness_status`, `runtime_proof_status`, `first_blocker`, `latest_proof_doc`, `latest_verify_artifact`, `authority_module`, `manifest_consistency_ok`, `matrix_status`, `notes`, `engine_kind`.

v2 is **not** a live HTTP poll. **`generated_utc`** stamps when rows were built; update **`engine_truth_overrides.json`** when matrix or PROOF closure changes, then regenerate.

### v2 curation policy (Task 48)

v2 rows are **curated**: matrix-backed and proof-backed engines get explicit statuses; other engines may remain **`unknown`** or receive **honest `pending` / `preflight_not_boolean`** overrides tied to [ENGINE_PARITY_MATRIX.md](../ENGINE_PARITY_MATRIX.md) or an explicit “no bounded proof” note — not invented PASS lines. See [PROOF §30](../PROOF_SLICE30_ENGINE_TRUTH_JSON.md) **v2 curation policy**.

## Targeted pack

`.\scripts\stt_hardening_regress.ps1` runs pytest slices for STT/preflight, then **`generate_engine_truth.py`** with **no args** (writes **v1**), then **`--schema v2`** (writes **v2**). That is two commands, not a single `--schema all` call.
