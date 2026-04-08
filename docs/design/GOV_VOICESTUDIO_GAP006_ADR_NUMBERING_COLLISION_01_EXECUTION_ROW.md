# GOV-VOICESTUDIO-GAP006-ADR-NUMBERING-COLLISION-01

**Status:** Closed
**GAP:** GAP-006 - ADR numbering collision (MCP / integration ADRs)
**Phase:** 0 (Ops)
**Role:** System Architect
**Created:** 2026-04-08

---

## Problem Statement

`docs/architecture/decisions/` contains an ADR namespace collision and related governance drift:

- Two files share ADR-045 numbering:
  - `ADR-045-orchestrator-architecture.md` (accepted; canonical ADR-045)
  - `ADR-045-mcp-integration-strategy.md` (stale duplicate of ADR-049 content)
- `ADR-049-mcp-integration-strategy.md` already exists and is the canonical MCP strategy record.
- `docs/architecture/decisions/README.md` index omits ADR-044, ADR-045, ADR-049, and ADR-050.
- `backend/mcp_bridge/README.md` cites MCP strategy as ADR-045 instead of ADR-049.
- `docs/design/GOLDEN_PATH_GOVERNANCE_STATUS.md` links to non-existent `ADR-050-golden-path-ci-deferred-risk.md`.

This creates reference ambiguity across governance docs, tracker citations, and architecture handoff surfaces.

## Bounded Slice

Repair ADR identifier integrity and inbound references only.

### Allowlist

| Action | Target |
|--------|--------|
| Create | `docs/design/GOV_VOICESTUDIO_GAP006_ADR_NUMBERING_COLLISION_01_EXECUTION_ROW.md` |
| Delete | `docs/architecture/decisions/ADR-045-mcp-integration-strategy.md` |
| Edit | `docs/architecture/decisions/README.md` (add missing ADR rows 044/045/049/050) |
| Edit | `backend/mcp_bridge/README.md` (ADR-045 -> ADR-049) |
| Edit | `docs/design/GOLDEN_PATH_GOVERNANCE_STATUS.md` (remove ghost ADR-050 link; clarify actual state) |
| Edit | Governance closure surfaces: tracker, canonical registry, STATE, closure report |

### Hard OUT

- No runtime code changes.
- No UI/XAML changes.
- No backend behavior changes.
- No semantic rewrite of ADR decisions.
- No new ADR numbering scheme changes beyond resolving this collision.

## Acceptance Contract

- [x] `ADR-045-mcp-integration-strategy.md` deleted.
- [x] `ADR-045-orchestrator-architecture.md` remains ADR-045 unchanged.
- [x] `ADR-049-mcp-integration-strategy.md` remains canonical MCP strategy ADR.
- [x] ADR README index includes entries for ADR-044, ADR-045, ADR-049, ADR-050.
- [x] `backend/mcp_bridge/README.md` cites MCP strategy as ADR-049.
- [x] `docs/design/GOLDEN_PATH_GOVERNANCE_STATUS.md` no longer references non-existent `ADR-050-golden-path-ci-deferred-risk.md`.
- [x] `pytest tests/ci` passes.
- [x] `.\scripts\verify.ps1 -Quick` GREEN.
- [x] `python scripts/run_verification.py` completion_guard PASS.
- [x] No new skip/ignore introduced.

## Rollback

Revert the GAP-006 commit to restore prior ADR filename/index/reference state.

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Hidden inbound links to deleted `ADR-045-mcp-integration-strategy.md` remain | Medium | Medium | Run repository-wide ADR reference grep and correct remaining hits in-lane |
| Historical docs depend on ghost ADR-050 filename | Low | Low | Keep historical meaning in prose while removing broken file link |
| Documentation-only lane under-verified | Low | Medium | Run governance proof set (`pytest tests/ci`, Quick verify, rolling verifier) and capture artifacts |

## Changelog

| Date | Entry |
|------|-------|
| 2026-04-08 | Row created; bounded governance-only lane frozen |
| 2026-04-08 | **Closed** — orphaned ADR-045 MCP file deleted; ADR README index repaired (044/045/049/050); MCP bridge ref corrected to ADR-049; ghost ADR-050 golden-path link removed; `pytest tests/ci` 217 passed; Quick `artifacts/verify/20260408_072710/`; rolling `20260408-073153` (completion_guard PASS); [closure report](../reports/verification/VOICESTUDIO_GAP006_ADR_NUMBERING_COLLISION_LANE_CLOSURE_2026-04-08.md) |
