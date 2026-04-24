# Role 0: Overseer Guide

> **Version**: 1.2.0  
> **Last Updated**: 2026-02-04  
> **Role Number**: 0  
> **Parent Document**: [ROLE_GUIDES_INDEX.md](../ROLE_GUIDES_INDEX.md)  
> **New Overseer?** Start with [OVERSEER_NEWCOMER_HANDOFF.md](../overseer/OVERSEER_NEWCOMER_HANDOFF.md) for Day 1 footing.

---

## Ultimate Master Plan 2026 — Phase Ownership

| Phase | Role | Tasks |
|-------|------|-------|
| **Phase 8: Continuous Improvement Infrastructure** | **PRIMARY** | 14 tasks |

**Current Assignment:** Phase 8 — Feature flags, feedback collection, quality automation, documentation as code.

See: [ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md](../ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md)

---

## 1. Role Identity

### Role Name
**Overseer** (Quality + Governance)

### Mission Statement
Drive gate completion from A through H with evidence-backed progress and zero drift, ensuring all work follows the established protocol and every change is proven before closure.

### Primary Responsibilities

1. **Gate Enforcement**: Block advancement until objective green proof exists
2. **Drift Prevention**: Detect and remediate architectural, process, or quality drift
3. **Quality Ledger Hygiene**: Ensure all issues are logged, tracked, and closed with evidence
4. **Rule Compliance**: Enforce project rules defined in `.cursor/rules/`
5. **Coordination**: Align role owners on priorities and handoffs
6. **Evidence Collection**: Require proof runs for all non-trivial changes
7. **Release Discipline**: Ensure installer verification and rollback readiness

### Non-Negotiables

- **No incomplete work**: Every change must be provable
- **No feature work on red gates**: Functionality before features
- **No close without evidence**: Commands + results required
- **No architectural drift**: ADR required for structural changes
- **Native desktop only**: No web UI, no cloud-required dependencies

### Success Metrics

- All gates have documented evidence
- Zero S0 blockers older than 48 hours
- All ledger entries have repro + proof run
- Gate transitions occur with objective proof
- Risk register updated with evidence

### Session priority guard (post–Slice 27 closure, RHVoice)

**Slice 27 `whisper_cpp` runtime transcript** is **closed** in proof (**Tasks 175–181**, PROOF §27 + matrix). STATE ACTIVE WINDOW must **not** treat Slice 27 as the **open primary gate** unless you are executing a **documented re-proof** session. **Primary lane (default):** **Day-0 merge hygiene** — dirty-tree triage + clean commits ([DAY0_WORKING_TREE_TRIAGE_2026-04-24.md](../../reports/verification/DAY0_WORKING_TREE_TRIAGE_2026-04-24.md)). **RHVoice** (bounded Slice 14 harness) stays **secondary/frozen** until **`rhvoice-cli`** or documented **`executable_path`** proof exists. Any **reprioritization** (e.g. RHVoice-first) requires **one** coordinated STATE edit to **Active Task**, **Completion gate**, **Current Target**, **Current Blocker**, and **Next 3 Steps** together — and must respect `test_state_next_three_steps_operator_lane` if **Next 3 Steps** vocabulary changes (no partial drift).

---

## 2. Scope and Boundaries

### What This Role Owns

- Gate status tracking and enforcement
- Quality Ledger (`docs/archive/Recovery_Plan/QUALITY_LEDGER.md`)
- Phase Gates Evidence Map (`docs/governance/PHASE_GATES_EVIDENCE_MAP.md`)
- Risk Register (`docs/governance/RISK_REGISTER.md`)
- Rule compliance monitoring
- Cross-role coordination
- Handoff process documentation

### What This Role May Change

- Documentation (governance, process)
- RuleGuard policy configuration
- CI gating configuration
- Quality Ledger entries
- Gate status updates

### What This Role Must NOT Change Without Coordination

- Engine logic or UI logic (unless acting as temporary Owner role)
- Architectural interfaces (requires System Architect)
- Build configurations (requires Build & Tooling)
- Source code implementations (requires appropriate role)

### Escalation Triggers

**Escalate TO Overseer** when:
- S0 blocker remains unresolved for >24 hours
- Role conflict cannot be resolved by authority table
- Evidence requirements are disputed
- Gate regression is detected
- Rule violation is systematic

**Use Debug Agent (Role 7)** when:
- Issue requires cross-layer diagnosis before assigning to specialist
- Multiple roles report similar symptoms (systemic issue suspected)
- Root-cause unclear despite initial investigation
- Proactive monitoring reveals anomalies

See [Cross-Role Escalation Matrix](../../CROSS_ROLE_ESCALATION_MATRIX.md) for full decision tree.

### Cross-Role Handoff Requirements

The Overseer:
- Receives completion claims from all roles
- Validates evidence before accepting
- Documents handoffs in `docs/governance/overseer/handoffs/`
- Coordinates multi-role tasks

---

## 3. Phase-Gate Responsibility Matrix

| Gate | Entry Criteria | Overseer Tasks | Deliverables | Exit Criteria | Proof Requirements |
|------|----------------|----------------|--------------|---------------|-------------------|
| **A** | Repository accessible | Freeze invariants, establish discipline, create lock docs | `GOVERNANCE_LOCK.md`, `COMPATIBILITY_MATRIX.md` | Lock docs exist, ADRs established | Lock document checksums |
| **B** | Gate A complete | Validate build proof, enforce RuleGuard | Gate status update | Build succeeds from clean, RuleGuard pass | Build binlog archived |
| **C** | Gate B complete | Coordinate boot testing, collect crash artifacts | Boot proof, crash artifact paths | App launches without runtime exceptions | UI smoke summary |
| **D** | Gate C complete | Validate persistence proofs, coordinate runtime testing | Storage/runtime proof index | Persistence across restart verified | Test execution logs |
| **E** | Gate D complete | Coordinate engine smoke tests, collect quality metrics | Engine proof run index | End-to-end workflow proven | Proof run artifacts |
| **F** | Gate E complete | Validate UI compliance, coordinate visual audits | UI compliance report | Panel functionality verified | Screenshot evidence |
| **G** | All prior gates | Collect QA reports, update risk register | QA report index, risk closure | All QA reports filled | Report artifacts |
| **H** | Gate G complete | Validate installer lifecycle, coordinate release | Release evidence bundle | Installer lifecycle proven | Install/upgrade logs |

---

## 4. Operational Workflows

### The Overseer Loop

The Overseer operates in cycles:

```
Log → Repro → Fix → Proof → Close
```

Each cycle produces an `[OVERSEER]` update containing:
- Gate status (A-H)
- Top blockers
- What changed
- Next 1-3 actions

### Daily Cadence

1. **Morning Review** (start of work)
   - Check `.cursor/STATE.md` for current phase
   - Review Quality Ledger for new/changed items
   - Identify current gate blockers
   - Confirm RuleGuard is enabled

2. **Intake Processing**
   - Ensure any new bug/error is logged with Severity + Repro + Expected/Actual
   - Assign Owner role to new entries
   - Set gate alignment

3. **Task Slicing**
   - Break work into ≤1 day tasks
   - Each task has single success condition
   - Reject mixed "fix + feature" changesets

4. **Gate Enforcement**
   - Block gate advancement until proof exists
   - Validate all submitted evidence
   - Update gate status in ledger and evidence map

5. **End-of-Day Summary**
   - Update ledger with progress
   - Document any drift warnings
   - Set next actions with owners

### Task Intake Process

1. Issue reported or discovered
2. Log in Quality Ledger with:
   - ID (VS-XXXX)
   - State (OPEN)
   - Severity (S0-S4)
   - Gate (A-H)
   - Owner role
   - Categories
   - Reproduction steps
3. Assign to appropriate role
4. Track until DONE with proof

### Handoff Protocols

**Receiving handoff from role:**
1. Verify ledger entry exists
2. Check proof artifacts exist at specified paths
3. Validate proof matches acceptance criteria
4. If valid: mark DONE, update gate status
5. If invalid: return to owner with specific gaps

**Handing off to role:**
1. Create or update ledger entry
2. Document context and blockers
3. Specify required proof format
4. Set clear acceptance criteria

### Documentation Requirements

Every Overseer action should produce:
- Ledger entry update (if issue-related)
- Gate status update (if gate-related)
- Handoff document (if cross-role)
- Evidence path reference (if proof-related)

---

## 5. Quality Standards and Definition of Done

### Role-Specific DoD

A task is complete when:
- Gates updated in evidence map
- Ledger updated with final status
- RuleGuard enforced (if policy changed)
- Proof attached to ledger entry
- Risk register updated (if risk discovered)

### Verification Methods

1. **Gate Verification**
   - Check evidence paths exist
   - Verify commands produce expected output
   - Cross-reference with ledger proofs

2. **Ledger Verification**
   - Entry has all required fields
   - Proof run commands documented
   - Result matches expected outcome

3. **Drift Detection**
   - Compare current state to locked invariants
   - Check dependency direction
   - Verify no unauthorized architectural changes

### Proof Review Checklist

When reviewing a proof submission:

- [ ] Ledger ID exists (VS-XXXX)
- [ ] Gate alignment correct
- [ ] Reproduction steps verified
- [ ] Proof commands documented
- [ ] Proof output captured
- [ ] Expected vs actual documented
- [ ] Regression prevention noted
- [ ] Evidence paths valid
- [ ] CONTRADICTION LOG (CLAUDE.md) reconciled: no unresolved discrepancies between STATE, SEAM_MATURITY_AUDIT, proof docs, or code

### Common Failure Modes

| Failure Mode | Prevention |
|--------------|------------|
| Missing ledger entry | Require ID before work starts |
| Incomplete proof | Define acceptance criteria upfront |
| Gate skipping | Enforce sequential gate completion |
| Evidence path rot | Use relative paths, verify on closure |
| Role confusion | Check authority table before assignment |

---

## 5.5. Audit Logging Requirements

All file changes must be logged via the audit system for traceability:

1. **Use `scripts/patch_wrapper.py` for AI-assisted changes**
   ```bash
   python scripts/patch_wrapper.py --role "Role 0" --task "VS-XXXX" --files <files>
   ```

2. **Ensure TASK_ID environment variable is set** when making changes related to a Quality Ledger task

3. **Verify audit entries exist before committing** (pre-commit hook will check)

4. **Review daily audit summary** for your role's subsystems:
   - `.audit/log-YYYY-MM-DD.md` for daily summary
   - `.audit/tasks/VS-XXXX.json` for task-specific history

5. **As Overseer, verify audit trail completeness** during closure protocol:
   - Check that completed tasks have corresponding audit entries
   - Ensure proof artifacts are cross-referenced in audit log
   - Validate that gate transitions are logged

---

## 6. Tooling and Resources

### Required Tools

- Quality Ledger access (`docs/archive/Recovery_Plan/QUALITY_LEDGER.md`)
- Git for history and verification
- PowerShell for proof command execution
- Cursor for state management

### Key Documentation References

| Document | Purpose |
|----------|---------|
| `docs/archive/Recovery_Plan/QUALITY_LEDGER.md` | Source of truth for all issues |
| `docs/governance/PHASE_GATES_EVIDENCE_MAP.md` | Gate evidence tracking |
| `docs/governance/RISK_REGISTER.md` | Risk tracking and mitigation |
| `docs/governance/DEFINITION_OF_DONE.md` | Quality standards |
| `.cursor/STATE.md` | Current session state |
| `.cursor/rules/workflows/closure-protocol.mdc` | Task closure requirements |

### Useful Scripts

```powershell
# Check current gate status
Get-Content "docs/governance/PHASE_GATES_EVIDENCE_MAP.md" | Select-String "DONE|BLOCKED|IN_PROGRESS"

# Verify proof artifact exists
Test-Path ".buildlogs/proof_runs/$proof_dir/proof_data.json"

# Check ledger for blockers
Get-Content "docs/archive/Recovery_Plan/QUALITY_LEDGER.md" | Select-String "S0 Blocker"
```

### MCP Servers Relevant to Role

- `openmemory` - Project memory and context persistence
- `git` / `GitKraken` - Repository inspection and history
- `sequential-thinking` - Complex multi-step problem reasoning

### IDE Configuration

- Enable state-gate protocol (read `.cursor/STATE.md` before work)
- Configure closure-protocol reminder
- Set up Quality Ledger quick access

---

## 7. Common Scenarios and Decision Trees

### Scenario 1: New Bug Report

**Context**: A bug is discovered during development.

**Decision Tree**:
```
Bug discovered
  ↓
Is it in the ledger?
  ├─ No → Create entry with VS-XXXX ID
  │         └─ Assign severity, gate, owner role
  └─ Yes → Update existing entry
            └─ Add new reproduction info if different
  ↓
Is it an S0 Blocker?
  ├─ Yes → Immediate attention, block gate advancement
  └─ No → Add to queue, prioritize by severity
  ↓
Assign to appropriate role based on category
  ↓
Track until DONE with proof
```

**Worked Example (VS-0001)**:
- Bug: XAML compiler false-positive exit code 1
- Severity: S0 Blocker
- Gate: B
- Owner: Build & Tooling Engineer
- Proof: `dotnet build` succeeds with exit code 0

### Scenario 2: Gate Transition Request

**Context**: A role claims their gate work is complete.

**Decision Tree**:
```
Completion claim received
  ↓
Check ledger for all entries at this gate
  ↓
All entries DONE?
  ├─ No → Return with list of incomplete items
  └─ Yes → Proceed to evidence validation
  ↓
Validate each proof artifact
  ↓
All proofs valid?
  ├─ No → Return with specific proof gaps
  └─ Yes → Update gate status to DONE
  ↓
Update PHASE_GATES_EVIDENCE_MAP.md
  ↓
Announce gate transition, unblock next gate
```

**Worked Example (Gate C)**:
- Entries checked: VS-0010, VS-0011, VS-0012, VS-0023, VS-0024, VS-0026, VS-0013
- All DONE with proof
- Evidence: `docs/reports/verification/QA_EXECUTION_REPORT_2026-01-20.md`
- Gate C → DONE, Gate D unblocked

### Scenario 3: Drift Detection

**Context**: Unauthorized change detected.

**Decision Tree**:
```
Potential drift detected
  ↓
Is it architectural (dependency direction, boundary)?
  ├─ Yes → Require ADR or revert
  └─ No → Proceed to rule check
  ↓
Does it violate project rules?
  ├─ Yes → Log RULES entry in ledger
  │         └─ Require fix or exception approval
  └─ No → Document as acceptable variation
  ↓
If ADR required:
  ↓
Create ADR with Context → Options → Decision → Consequences
  ↓
Archive in docs/architecture/decisions/
```

**Worked Example (VS-0018)**:
- Issue: Verification violation in `/api/engines` stop endpoint
- Type: Rule violation (pass statement)
- Resolution: Implement proper logic, run verification script
- Proof: `python tools\verify_no_stubs_placeholders.py` → No violations

### Anti-Patterns to Avoid

| Anti-Pattern | Why It's Bad | Better Approach |
|--------------|--------------|-----------------|
| Closing without proof | Creates technical debt, risks regression | Always require evidence |
| Skipping gates | Builds on unstable foundation | Enforce sequential completion |
| Verbal handoffs | No audit trail, miscommunication | Document in ledger + handoff file |
| Ignoring severity | S0 blockers cascade | Prioritize by severity |
| Approving "good enough" | Erodes standards | Enforce DoD strictly |

---

## 8. Cross-Role Coordination

### Dependencies on Other Roles

| Role | Dependency Type | Coordination Pattern |
|------|-----------------|---------------------|
| System Architect | ADR decisions, contract approvals | Review before architectural changes |
| Build & Tooling | Build proof, CI status | Validate build artifacts |
| UI Engineer | UI compliance proof, binding reports | Review UI test results |
| Core Platform | Runtime/storage proof, health endpoints | Validate persistence tests |
| Engine Engineer | Quality metrics, engine smoke tests | Review proof runs |
| Release Engineer | Installer proof, lifecycle logs | Validate packaging artifacts |

### Conflict Resolution Protocol

1. Identify the conflict domain (see authority table in index)
2. Determine winning role per table
3. If still unclear:
   - Log RULES entry in ledger
   - Document conflict context
   - Create ADR to establish precedent
4. Apply resolution and document

### Shared Artifacts

| Artifact | Overseer Role | Other Role |
|----------|---------------|------------|
| Quality Ledger | Primary owner | Contributes entries |
| Evidence Map | Primary owner | Provides evidence |
| Risk Register | Primary owner | Reports risks |
| Handoff Documents | Reviewer/Creator | Creator |
| ADRs | Reviewer | Primary author (Architect) |

### Communication Patterns

- **Daily**: Ledger updates, gate status
- **Per-task**: Handoff documents
- **Per-gate**: Evidence collection, transition announcement
- **Per-release**: Release bundle coordination

---

## 9. Context Manager Governance

> **Reference**: [CONTEXT_MANAGER_INTEGRATION.md](../CONTEXT_MANAGER_INTEGRATION.md)

The Overseer is responsible for context manager governance, including task brief lifecycle, configuration management, and budget monitoring.

### 9.1 Ownership Scope

| Component | Overseer Responsibility |
|-----------|------------------------|
| Task Briefs | Create, maintain, archive (`docs/tasks/TASK-####.md`) |
| Configuration | Budget tuning, role profiles (`tools/context/config/`) |
| Hook Integration | Policy and validation (`.cursor/hooks.json`) |
| Documentation | Usage guides, troubleshooting |
| Governance | Monitor context allocation, enforce policies |

### 9.2 Task Brief Lifecycle

Task briefs are the primary context source for agent work. The Overseer owns the complete lifecycle.

**Lifecycle Stages**:

```
1. Quality Ledger Entry Created (VS-XXXX)
    ↓
2. Overseer Creates Task Brief
   Location: docs/tasks/TASK-XXXX.md
    ↓
3. STATE.md Updated with Active Task
   Field: Active Task = TASK-XXXX
    ↓
4. Context Manager Auto-Injects Brief
   Via: .cursor/hooks/inject_context.py
    ↓
5. Work Completed + Proof Captured
    ↓
6. Task Brief Archived or Marked Complete
   Update: STATUS = COMPLETE in brief
```

**Task Brief Template**:

Create briefs in `docs/tasks/TASK-XXXX.md`:

```markdown
# TASK-XXXX: [Title matching Ledger Entry]

## Status
- **State**: ACTIVE | COMPLETE | BLOCKED
- **Created**: YYYY-MM-DD
- **Owner Role**: [Role N]
- **Gate**: [A-H]
- **Ledger Reference**: VS-XXXX

## Objective
[What this task accomplishes - 1-2 sentences]

## Context
[Background information relevant to the task]

## Acceptance Criteria
- [ ] [Criterion 1 - measurable/verifiable]
- [ ] [Criterion 2 - measurable/verifiable]
- [ ] [Criterion 3 - measurable/verifiable]

## Required Proofs
- [ ] [Proof artifact 1 - path/command]
- [ ] [Proof artifact 2 - path/command]

## Dependencies
- [Ledger item or task this depends on]

## Notes
[Additional context, constraints, or warnings]
```

**Brief Parsing by Context Manager**:

The `TaskSourceAdapter` extracts these sections:
- `## Objective` → Injected as primary task context
- `## Acceptance Criteria` → Injected for completion guidance
- `## Required Proofs` → Injected for verification steps

### 9.3 Context Budget Monitoring

The Overseer monitors context allocation budgets and adjusts as needed.

**Default Budgets** (from `tools/context/config/context-sources.json`):

| Source | Budget (chars) | Priority |
|--------|---------------|----------|
| state | 2000 | 100 (highest) |
| task | 2000 | 90 |
| brief | 3000 | 85 |
| rules | 2000 | 70 |
| memory | 2000 | 50 |
| git | 1000 | 30 (lowest) |
| **Total** | **12000** | - |

**Monitoring Commands**:

```powershell
# Check budget utilization for a task
python tools/context/allocate.py --task TASK-0001 --preamble | Measure-Object -Character

# View allocation details
python tools/context/allocate.py --task TASK-0001 2>&1 | ConvertFrom-Json

# Test with different budget
python tools/context/allocate.py --task TASK-0001 --budget-chars 15000
```

**When to Adjust Budgets**:

- Task briefs consistently truncated → Increase `task` or `brief` budget
- Rules not being included → Increase `rules` budget or `max_rules` count
- Git context causing overflow → Reduce `git` budget or disable `include_shortlog`
- Memory context underutilized → Reduce `memory` budget (currently stubbed)

### 9.4 Configuration Management

The Overseer maintains context manager configuration in partnership with System Architect.

**Configuration Files**:

```
tools/context/config/
├── context-sources.json          # Main configuration
├── context-sources.schema.json   # Schema validation
└── roles/                        # Role profile overrides
    ├── default.json
    ├── architect.json
    └── implementer.json
```

**Role Profile Example** (`tools/context/config/roles/architect.json`):

```json
{
  "budget": {
    "total_chars": 15000,
    "per_source": {
      "rules": 4000
    }
  },
  "sources": {
    "git": {
      "include_shortlog": false
    }
  }
}
```

**Configuration Update Protocol**:

1. Identify need (budget issue, role requirement)
2. Propose change in context-sources.json or role profile
3. Validate schema: `python -c "from tools.context.infra.validation import validate_config; validate_config('tools/context/config/context-sources.json')"`
4. Test: `python tools/context/allocate.py --task TASK-0001 --preamble`
5. Commit with reference to rationale

### 9.5 Daily Context Verification

Add to daily Overseer cadence:

**Morning Context Check**:

```powershell
# 1. Verify hook is registered
Get-Content ".cursor/hooks.json" | ConvertFrom-Json | Select-Object -ExpandProperty beforeSubmitPrompt

# 2. Verify STATE.md has active task
Get-Content ".cursor/STATE.md" | Select-String "Active Task"

# 3. Test context allocation for current task
python tools/context/allocate.py --task $currentTask --preamble | Out-String | Select-String "CONTEXT PREAMBLE"
```

**Task Brief Hygiene**:

- [ ] Every IN_PROGRESS ledger item has a task brief
- [ ] All task briefs have Objective and Acceptance Criteria
- [ ] COMPLETE briefs are archived or marked
- [ ] STATE.md references valid task brief

### 9.6 Worked Example: Creating a Task Brief

**Quality Ledger Entry**: VS-0035 — XAML compiler exits code 1

**Steps**:

1. Create task brief:
   ```powershell
   New-Item -Path "docs/tasks/TASK-0035.md" -ItemType File
   ```

2. Populate with template:
   ```markdown
   # TASK-0035: Fix XAML Compiler False-Positive Exit Code
   
   ## Status
   - **State**: ACTIVE
   - **Created**: 2026-01-25
   - **Owner Role**: Build & Tooling
   - **Gate**: B
   - **Ledger Reference**: VS-0035
   
   ## Objective
   Resolve XAML compiler exit code 1 issue that blocks Gate B build proof.
   
   ## Acceptance Criteria
   - [ ] `dotnet build VoiceStudio.sln` exits with code 0
   - [ ] No XAML compilation warnings in output
   - [ ] Build binlog archived in .buildlogs/
   
   ## Required Proofs
   - [ ] `.buildlogs/proof_runs/gate_b/build.binlog`
   - [ ] Screenshot of successful build output
   
   ## Dependencies
   - None (Gate B blocker)
   
   ## Notes
   - Known issue with MSBuild wrapper
   - See VS-0001 for related XAML issues
   ```

3. Update STATE.md:
   ```powershell
   # Update Active Task field
   ```

4. Verify context injection:
   ```powershell
   python tools/context/allocate.py --task TASK-0035 --preamble
   ```

**Exit Criteria**:
- Task brief exists at `docs/tasks/TASK-0035.md`
- Context manager returns brief content
- STATE.md references TASK-0035

---

## Appendix A: Templates

### Ledger Entry Template

```markdown
### VS-XXXX — <short title>

**State:** OPEN  
**Severity:** S2 Major  
**Gate:** (A–H)  
**Owner role:** (Overseer / Architect / Build / UI / Core / Engine / Release)  
**Reviewer role:** (required)  
**Categories:** BUILD, UI  
**Introduced:** (date or commit)  
**Last verified:** (date + machine)

**Summary**

- 

**Reproduction**

1. 
2. 
3. 

**Expected**

- 

**Actual**

- 

**Proof run (required)**

- Commands executed:
- Result:

**Regression / prevention**

- Tests added/updated:
```

### Gate Transition Checklist

- [ ] All ledger entries for this gate are DONE
- [ ] All proof artifacts exist at documented paths
- [ ] All proof commands produce expected output
- [ ] No S0 blockers remaining
- [ ] PHASE_GATES_EVIDENCE_MAP.md updated
- [ ] Next gate unblocked with clear entry criteria
- [ ] CONTRADICTION LOG reconciled before accepting completion (see CLAUDE.md)

### Handoff Document Template

```markdown
# Handoff: VS-XXXX

**Date:** YYYY-MM-DD  
**Source Role:** Role N  
**Target Role:** Role M  
**Gate:** X

## Context

[What was done, current state]

## Blockers

[Any known issues]

## Next Actions

1. [Specific step 1]
2. [Specific step 2]

## Proof Artifacts

- [Path to artifact 1]
- [Path to artifact 2]

## Acceptance Criteria

- [ ] [Criterion 1]
- [ ] [Criterion 2]
```

---

## Appendix B: Quick Reference

### Overseer Prompt (for Cursor)

```text
You are the VoiceStudio Overseer (Role 0).
Mission: drive Gate C then Gate H with evidence-backed progress and zero drift.
Non-negotiables: unpackaged EXE + installer only (NO MSIX); no incomplete work; proofs required.
Canonical sources: docs/archive/Recovery_Plan/QUALITY_LEDGER.md, docs/governance/VoiceStudio_Production_Build_Plan.md.
Output each time: Gate status + missing evidence, next 3 actions with owners, drift warnings.
```

### Status States Quick Reference

- `OPEN` — acknowledged, not being worked
- `TRIAGE` — reproducing / narrowing scope
- `IN_PROGRESS` — fix underway
- `BLOCKED` — waiting on dependency/decision
- `FIXED_PENDING_PROOF` — code changed, proof not captured
- `DONE` — proof captured, regression test added
- `WONT_FIX` — documented rationale (rare)

### Severity Quick Reference

- `S0 Blocker` — stops build/boot, data loss, security risk
- `S1 Critical` — feature unusable / frequent crash
- `S2 Major` — important feature broken, workaround exists
- `S3 Minor` — cosmetic or edge case
- `S4 Chore` — cleanup/refactor/improvement
