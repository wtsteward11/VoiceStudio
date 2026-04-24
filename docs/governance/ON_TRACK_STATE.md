# On-Track State (Canonical Truth Anchor)

**Purpose:** Single-page reference for canonical sources. Ledger-driven status is authoritative; conflicting documents are historical snapshots until reconciled.

**Last Updated:** 2026-01-30

---

## Ledger and Gate Status

| Item | Location / Command |
|------|--------------------|
| **Quality Ledger** | [docs/archive/Recovery_Plan/QUALITY_LEDGER.md](../archive/Recovery_Plan/QUALITY_LEDGER.md) |
| **Gate status** | `python scripts/run_verification.py` |
| **Gate status (with build)** | `python scripts/run_verification.py --build` |
| **Ledger validate** | `python -m tools.overseer.cli.main ledger validate` |

---

## Compatibility (Authoritative Sources)

**Implementation files are canonical.** Documentation describes them; it does not override them.

| Layer | Canonical Source | Notes |
|-------|------------------|--------|
| **Python / ML stack** | [requirements_engines.txt](../../requirements_engines.txt) | Pinned versions for engines |
| **.NET / WinUI** | [Directory.Build.props](../../Directory.Build.props) | WinAppSDK, CommunityToolkit, NAudio |
| **.NET SDK** | [global.json](../../global.json) | SDK version |
| **Documentation** | [docs/design/COMPATIBILITY_MATRIX.md](../design/COMPATIBILITY_MATRIX.md) | Must match implementation; update when pins change |

---

## Proof Artifacts

| Root | Purpose |
|------|---------|
| **.buildlogs/** | Build logs, verification output, proof runs |
| **.buildlogs/verification/** | `last_run.json` — gate status + ledger validate result |
| **.buildlogs/proof_runs/** | Baseline workflow, wizard flow, engine proofs |
| **installer/Output/** | Installer binaries and lifecycle evidence |

---

## Document Precedence

1. **Implementation files** (requirements_engines.txt, Directory.Build.props, global.json) define actual versions.
2. **QUALITY_LEDGER.md** defines what is proven (gates, evidence).
3. **COMPATIBILITY_MATRIX.md** documents compatibility; it must be kept in sync with implementation.
4. Documents claiming versions or status that conflict with the above are **historical snapshots** unless updated. See [TRACEABILITY_MATRIX.md](TRACEABILITY_MATRIX.md) for claims → evidence mapping.

---

## Related

- [TRACEABILITY_MATRIX.md](TRACEABILITY_MATRIX.md) — Claims → Ledger IDs → Proof artifacts
- [CANONICAL_REGISTRY.md](CANONICAL_REGISTRY.md) — Full document registry
- [PROJECT_HANDOFF_GUIDE.md](PROJECT_HANDOFF_GUIDE.md) — Maintainer entry point
