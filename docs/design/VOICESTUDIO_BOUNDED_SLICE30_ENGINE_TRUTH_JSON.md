# Bounded Slice 30 — Machine-readable engine truth (generated JSON)

**Status:** Closed  
**Date:** 2026-04-23  

## Goal

- **v1:** Generate [`docs/reports/verification/generated/engine_truth.json`](../../docs/reports/verification/generated/engine_truth.json) from manifest scan (inventory).
- **v2 (Task 33):** Generate [`docs/reports/verification/generated/engine_truth_v2.json`](../../docs/reports/verification/generated/engine_truth_v2.json) via **`python scripts/generate_engine_truth.py --schema v2`**, joining [`tools/overseer/data/engine_truth_overrides.json`](../../tools/overseer/data/engine_truth_overrides.json) for readiness/runtime/matrix fields.

## Verification

```powershell
python scripts/generate_engine_truth.py
python scripts/generate_engine_truth.py --schema v2
```
