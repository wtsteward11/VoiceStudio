# Traceability Matrix

**Purpose:** Map claims and capabilities to canonical sources and proof artifacts. Ledger-driven; no "complete" without evidence.

**Last Updated:** 2026-01-30  
**Related:** [ON_TRACK_STATE.md](ON_TRACK_STATE.md), [docs/archive/Recovery_Plan/QUALITY_LEDGER.md](../archive/Recovery_Plan/QUALITY_LEDGER.md)

---

## Claims → Ledger IDs → Proof Artifacts

| Claim / Capability | Canonical Source | Proof Artifact | Ledger ID | Owner |
| ------------------ | ---------------- | -------------- | --------- | ----- |
| Build succeeds | QUALITY_LEDGER | `.buildlogs/build_*.binlog` | VS-0001, VS-0005, VS-0035 | Build/Tooling |
| Installer lifecycle proven | QUALITY_LEDGER | `installer/Output/`, lifecycle logs (e.g. `C:\logs\voicestudio_lifecycle_*.log`) | VS-0003 | Release |
| Engine integration (XTTS, baseline, So-VITS) | QUALITY_LEDGER | `.buildlogs/proof_runs/` (baseline_workflow_*, sovits_svc_*, gpu_validation_*) | VS-0002, VS-0009, VS-0016, VS-0017, VS-0027, VS-0030, VS-0031, VS-0034 | Engine |
| App boot stability | QUALITY_LEDGER | Gate C publish + UI smoke: `ui_smoke_summary.json`, `.buildlogs/x64/Release/gatec-publish/` | VS-0012, VS-0023, VS-0026 | UI / Release |
| Storage / job persistence | QUALITY_LEDGER | ProjectStore tests, job_state_store tests, artifact registry tests | VS-0004, VS-0006, VS-0014, VS-0015, VS-0020, VS-0021, VS-0029, VS-0033 | Core Platform |
| Preflight / backend readiness | QUALITY_LEDGER | Preflight proof, `/api/health/preflight` | VS-0019, VS-0022, VS-0029 | Core Platform |
| Pins are standard | Implementation files | `requirements_engines.txt`, `Directory.Build.props`, `global.json` | N/A | System Architect |
| Gate status | run_verification.py | `.buildlogs/verification/last_run.json` | N/A | Overseer |
| UI controls / Fluent compliance | QUALITY_LEDGER | UI smoke, panel tests | VS-0028 | UI Engineer |

---

## Gate → Ledger Entry Mapping

| Gate | Purpose | Primary Ledger IDs |
| ---- | ------- | ------------------ |
| B | Clean compile + RuleGuard | VS-0001, VS-0005, VS-0008, VS-0018, VS-0035 |
| C | App boot stability | VS-0010, VS-0011, VS-0012, VS-0013, VS-0023, VS-0024, VS-0026 |
| D | Storage + job runtime | VS-0004, VS-0006, VS-0014, VS-0015, VS-0019, VS-0020, VS-0021, VS-0022, VS-0029, VS-0033 |
| E | Engine integration | VS-0002, VS-0007, VS-0009, VS-0016, VS-0017, VS-0027, VS-0030, VS-0031, VS-0034 |
| F | UI stability | VS-0028 |
| H | Packaging + installer | VS-0003 |

---

## How to Use

1. **Verify a claim:** Find the row; confirm the proof artifact exists and matches the ledger entry.
2. **Add a new capability:** Add a row; link to ledger ID(s) and proof path; get peer approval.
3. **Resolve drift:** If a document claims "complete" but no proof artifact is listed here, either add the proof or mark the claim pending.

---

## Related

- [ON_TRACK_STATE.md](ON_TRACK_STATE.md) — Canonical sources and precedence
- [CANONICAL_REGISTRY.md](CANONICAL_REGISTRY.md) — Document registry
- [docs/archive/Recovery_Plan/QUALITY_LEDGER.md](../archive/Recovery_Plan/QUALITY_LEDGER.md) — Full ledger
