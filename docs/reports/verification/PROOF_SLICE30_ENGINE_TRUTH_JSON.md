# PROOF — Slice 30 — engine truth JSON (manifest inventory + v2)

**Status:** **PASS** (v1 generator + **Task 33** v2 joined projection).

**Date:** 2026-04-23 (v1); v2 same session as Tasks 31–37.

## v1 — Manifest inventory (`engine_truth.json`)

| Item | Detail |
| --- | --- |
| Script | [`scripts/generate_engine_truth.py`](../../../scripts/generate_engine_truth.py) (default / `--schema v1`) |
| Output | [`generated/engine_truth.json`](generated/engine_truth.json) |
| Schema | `voicestudio.engine_truth.v1` |
| Content | Per manifest: `engine_id`, `name`, `type`, `subtype`, `support_tier`, `implementation_status`, paths, `entry_point` |

**Verification:**

```powershell
python scripts/generate_engine_truth.py
python scripts/generate_engine_truth.py --schema v2
```

**STT regression pack** ([`scripts/stt_hardening_regress.ps1`](../../../scripts/stt_hardening_regress.ps1)): runs the same two lines in order (default **v1**, then **`--schema v2`**). Optional one-shot: `python scripts/generate_engine_truth.py --schema all`.

## v2 — Operational projection (Task 33)

| Item | Detail |
| --- | --- |
| Output | [`generated/engine_truth_v2.json`](generated/engine_truth_v2.json) |
| Schema | `voicestudio.engine_truth.v2` |
| Join | Manifest scan + [`tools/overseer/data/engine_truth_overrides.json`](../../../tools/overseer/data/engine_truth_overrides.json) — **curated** readiness/runtime/matrix strings |

v2 is **not** a live HTTP poll; fields may be **stale** until regen after matrix/PROOF edits — see [`generated/README.md`](generated/README.md).

**RHVoice (Tasks 36–37 / 43 / 49):** **`rhvoice`** in overrides reflects **`runtime_proof_status: pending`** and an explicit **`first_blocker`** until a real host passes bounded RHVoice proof — **no new RHVoice implementation** under the matrix freeze; governance only in this slice.

## v2 curation policy (Task 48)

**Decision:** v2 is a **curated operational projection**, not an auto-filled “every engine must be non-unknown” inventory.

- **Matrix-first:** Engines with a **bounded matrix row** or an explicit **PROOF slice** get concrete `runtime_proof_status`, `matrix_status`, `latest_proof_doc`, and (when registered) `authority_module` values in [`tools/overseer/data/engine_truth_overrides.json`](../../../tools/overseer/data/engine_truth_overrides.json).
- **Optional / unscoped engines:** Engines present in manifests but **without** a matrix proof row may still receive overrides with honest **`pending`**, **`preflight_not_boolean`**, and a **`first_blocker`** that states the gap (e.g. no bounded proof) — **never** a synthetic **PASS**.
- **STS / voice conversion (Option A):** Curated v2 rows may use **`engine_kind: sts`** for manifest **`voice_conversion`** with **`pending`** semantics only until a future bounded slice defines STS proof shape — see [ENGINE_PARITY_MATRIX.md](ENGINE_PARITY_MATRIX.md) **STS / voice conversion** governance (no matrix PASS inflation).
- **Manifest `subtype` vs `engine_kind` (Task 61):** Curated **`engine_kind`** aligns to manifest **`subtype`** where mapped (e.g. **`vc`** So-VITS manifests → **`vc`** only); **`manifest_consistency_ok`** is enforced in [`scripts/generate_engine_truth.py`](../../../scripts/generate_engine_truth.py) — narrative in matrix **`engine_kind` vs manifest `subtype`** subsection.
- **Verify bar:** `defaults.latest_verify_artifact` must match the **same** path as [`.cursor/STATE.md`](../../../.cursor/STATE.md) **Latest verify artifact** and every v2 row’s `latest_verify_artifact`; [`tests/unit/scripts/test_engine_truth_verify_artifact_alignment.py`](../../../tests/unit/scripts/test_engine_truth_verify_artifact_alignment.py) enforces this (Tasks 44–45).

## Out of scope (Slice 30 original)

- CI gate “fail if generated file stale” — optional follow-up.

## Artifacts

- [`docs/reports/verification/generated/README.md`](generated/README.md)
- Bounded brief: [`docs/design/VOICESTUDIO_BOUNDED_SLICE30_ENGINE_TRUTH_JSON.md`](../../../docs/design/VOICESTUDIO_BOUNDED_SLICE30_ENGINE_TRUTH_JSON.md)
