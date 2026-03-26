# Golden Path Governance Status (GP-01)

> **Purpose:** Explicit governance closure for golden-path real-engine validation gap.  
> **Related:** [ADR-050](../architecture/decisions/ADR-050-golden-path-ci-deferred-risk.md), [VoiceStudio Completion Plan](VOICESTUDIO_COMPLETION_ROADMAP_V2.md)

---

## Status: Documented and Owned

**GP-01 requirement:** Either scheduled real-engine CI validation exists, or an ADR/tracked decision makes the gap explicit and owned.

**Resolution:** [ADR-050: Real-Engine Golden-Path CI Validation Deferred](../architecture/decisions/ADR-050-golden-path-ci-deferred-risk.md) documents the gap, dependencies, and target resolution trigger. Risk is explicitly accepted; no scheduled job.

---

## Current State

| Item | Status |
|------|--------|
| Stub proof | Runs on every PR via `write_golden_path_stub_proof.py` |
| Real proof | Manual trigger only: `gh workflow run CI --field run_runtime_gates=true` |
| One-time proof | `docs/reports/verification/PROOF_GOLDEN_PATH_REAL_2026-03-06.json` |
| Scheduled real-engine CI | **Deferred** per ADR-050 |

---

## Revisit Trigger (from ADR-050)

1. Self-hosted Windows runner with cached models available, or
2. CI budget allows scheduled Windows runs, or
3. Golden path refactored to run in container with pre-baked models.

---

## Changelog

- 2026-03-14: GP-01 governance closure. ADR-050 referenced; gap explicit and owned.
