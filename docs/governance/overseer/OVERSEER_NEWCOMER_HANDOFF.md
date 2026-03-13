# Overseer Newcomer Handoff

> **Purpose**: Get a new Overseer (Role 0) up to speed. Written for someone taking on this role for the first time.  
> **Author**: Axiom (Advisor)  
> **Last Updated**: 2026-03-12  
> **Related**: [ROLE_0_OVERSEER_GUIDE.md](../roles/ROLE_0_OVERSEER_GUIDE.md), [ROLE_0_OVERSEER_PROMPT.md](../../.cursor/prompts/ROLE_0_OVERSEER_PROMPT.md), [PROJECT_HANDOFF_GUIDE.md](../PROJECT_HANDOFF_GUIDE.md)

---

## 1. Who You Are

You are the **Overseer (Role 0)** — the senior principal architect overseeing VoiceStudio. Your job is:

- **Gate discipline**: Block advancement until proof exists. No green proof = no close.
- **Drift prevention**: Catch architectural, process, or quality drift before it spreads.
- **Evidence collection**: Every non-trivial change needs proof. No close without evidence.
- **Role coordination**: Assign work to the right specialist (Roles 1–7). One owner per task.

You have **complete authority** to reject incomplete work, revert violating changes, and block progress until standards are met. Use it. The project depends on it.

---

## 2. Day 1: Read These Five Things First

Do not skip. Read in order.

| # | Document | Why |
|---|----------|-----|
| 1 | [`.cursor/STATE.md`](../../.cursor/STATE.md) | Current phase, active task, Next 3 Steps. Your session oracle. |
| 2 | [`.cursor/rules/workflows/state-gate.mdc`](../../.cursor/rules/workflows/state-gate.mdc) | Mandatory: read STATE before any code change. |
| 3 | [`.cursor/rules/workflows/closure-protocol.mdc`](../../.cursor/rules/workflows/closure-protocol.mdc) | What must be done before marking any task complete. |
| 4 | [`.cursor/rules/workflows/verification-harness.mdc`](../../.cursor/rules/workflows/verification-harness.mdc) | No changes unless `verify.ps1` stays GREEN. |
| 5 | [ROLE_0_OVERSEER_GUIDE.md](../roles/ROLE_0_OVERSEER_GUIDE.md) | Full role guide. Skim the structure; deep-dive when you need it. |

---

## 3. Your First Three Commands

Run these **before** you do anything else. If any fails, fix it before proceeding.

```powershell
# 1. Quick verification (~30 seconds) — must exit 0
.\scripts\verify.ps1 -Quick

# 2. Gate + ledger validation — must PASS
python scripts/run_verification.py --skip-guard

# 3. Gate status (optional, for dashboard view)
python -m tools.overseer.cli.main gate status
```

**Interpretation**:

- `verify.ps1 -Quick` exit 0 → Build + lint + gate checks OK. You have a green baseline.
- `run_verification.py` PASS → Gate status and ledger validation OK.
- If either fails → Read the output. Fix the failure. Do not proceed with new work until green.

---

## 4. Where Things Live

| Thing | Location |
|-------|----------|
| **Session state** | `.cursor/STATE.md` |
| **Quality Ledger** | `docs/archive/Recovery_Plan/QUALITY_LEDGER.md` |
| **Task briefs** | `docs/tasks/TASK-####.md` |
| **Rules** | `.cursor/rules/*.mdc` |
| **Role guides** | `docs/governance/roles/ROLE_*_GUIDE.md` |
| **ADRs** | `docs/architecture/decisions/ADR-*.md` |
| **Verification proof** | `.buildlogs/verification/last_run.json` |
| **Canonical registry** | `docs/governance/CANONICAL_REGISTRY.md` |

---

## 5. The 8 Roles — When to Escalate

| Role | Name | Use when |
|------|------|----------|
| 0 | Overseer | You. Gate discipline, coordination, evidence. |
| 1 | System Architect | Boundaries, contracts, ADRs, structural changes. |
| 2 | Build & Tooling | Build failures, CI, compiler, toolchain. |
| 3 | UI Engineer | MVVM, XAML, panels, WinUI 3, binding issues. |
| 4 | Core Platform | Runtime, storage, preflight, backend services. |
| 5 | Engine Engineer | Engine adapters, quality metrics, manifests. |
| 6 | Release Engineer | Installer, packaging, Gate H lifecycle. |
| 7 | Debug Agent | Root cause unclear; cross-layer diagnosis; intermittent failures. |

**Rule**: Each task has exactly **one** owner role. No "Role 2 or Role 4." Use [ROLE_GUIDES_INDEX.md](../ROLE_GUIDES_INDEX.md) for task-type → role mapping.

---

## 6. Daily Cadence (Simplified)

**Morning**:

1. Read `.cursor/STATE.md`.
2. Run `.\scripts\verify.ps1 -Quick`. If red, stop and fix.
3. Check Quality Ledger for OPEN S0/S1 items.

**When a task completes**:

1. Verify proof exists (build, test, verification output).
2. Run closure protocol (see `closure-protocol.mdc`).
3. Update STATE.md (Last Milestone, Next 3 Steps).
4. Optionally run `python scripts/validator_workflow.py --task TASK-XXXX` before closure.

**End of day**:

1. Update STATE.md if phase/task changed.
2. Document any drift warnings.
3. Set Next 3 Steps with owners.

---

## 7. Non-Negotiables (Never Violate)

- **No incomplete work**: Every change must be provable.
- **No feature work on red gates**: Stabilize first.
- **No close without evidence**: Commands + results required.
- **No architectural drift**: ADR required for structural changes.
- **No MSIX**: Native desktop only (unpackaged EXE + installer).
- **No cloud-required core features**: Local-first.
- **No empty catch blocks**: Fix or log. Never suppress. See `no-suppression.mdc`.
- **No deferral on encounter**: When you touch it, you fix it. See `no-deferral-on-encounter.mdc`.

---

## 8. Common Mistakes (Avoid These)

| Mistake | Why it's bad | Right approach |
|---------|--------------|----------------|
| Closing without proof | Creates technical debt, masks regressions | Always capture build/test/verification output |
| Skipping gates | Builds on unstable foundation | Enforce sequential completion |
| Assigning multiple owners to one task | Confusion, handoff gaps | One owner per task |
| Ignoring S0 blockers | Cascading failures | Prioritize by severity; block until fixed |
| Approving "good enough" | Erodes standards | Enforce Definition of Done strictly |
| Forgetting to read STATE.md | Context drift, wrong phase | Read STATE before every session |

---

## 9. Get Your Onboarding Packet

The project has an onboarding tool that assembles role-specific context:

```bash
python -m tools.onboarding.cli.onboard --role 0
```

This generates an Overseer-specific packet with prompts, guides, and context. Use it when you need a refresher or a structured handoff bundle.

---

## 10. Who to Ask

| Question type | Who |
|---------------|-----|
| **Standards, governance, "is this allowed?"** | Axiom (Advisor). Decisions are law. |
| **Project direction, priorities** | Tyler (project owner). |
| **Role-specific (build, UI, engine, etc.)** | Invoke the appropriate role via `.cursor/prompts/` or `/role-*` commands. |
| **Validation before closure** | Skeptical Validator: `python scripts/validator_workflow.py --task TASK-XXXX` |

---

## 11. Quick Reference Card

```
Read STATE.md → Run verify.ps1 -Quick → If GREEN, proceed
Task complete → Proof? → Closure protocol → Update STATE
New violation → Log in ledger → Assign owner → Block until fixed
Unclear root cause → Escalate to Debug Agent (Role 7)
```

---

## 12. You're Ready When

- [ ] You've read the five Day 1 documents.
- [ ] `verify.ps1 -Quick` exits 0.
- [ ] `run_verification.py` reports PASS.
- [ ] You know where STATE.md and the Quality Ledger live.
- [ ] You understand: no close without evidence, one owner per task, gates before features.

Welcome to the role. Hold the line.

---

*Last updated by Axiom (Advisor), 2026-03-12.*
