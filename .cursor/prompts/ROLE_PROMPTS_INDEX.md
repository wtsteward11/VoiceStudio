# VoiceStudio 9-Role System Prompts Index (Roles 0–8)

> **Version**: 1.2.0
> **Last Updated**: 2026-03-28
> **Owner**: Overseer
> **Status**: ACTIVE — Ultimate Master Plan 2026 (Optimized)

This document is the navigation hub for VoiceStudio's **nine** execution roles (0–8) plus the **Skeptical Validator** subagent. Each numbered role has a comprehensive prompt file for AI agents (Claude, ChatGPT, Cursor, etc.). **Role 8** is read-only intelligence (no mutations); Validator prompts are listed separately below.

---

## 🎯 ACTIVE PLAN: Ultimate Master Plan 2026 (Optimized)

**Plan Document**: [ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md](../../docs/governance/ULTIMATE_MASTER_PLAN_2026_OPTIMIZED.md)

| Phase | Primary Owner | Tasks | Status |
|-------|--------------|-------|--------|
| 1. XAML Reliability & AI Safety | UI Engineer (3) | 20 | **CURRENT** |
| 2. Context Management Automation | Core Platform (4) | 22 | Pending |
| 3. API/Contract Synchronization | Engine Engineer (5) | 17 | Pending |
| 4. Test Coverage Expansion | Build/Tooling (2) | 24 | Pending |
| 5. Observability & Diagnostics | Debug Agent (7) | 17 | Pending |
| 6. Security Hardening | Core Platform (4) | 14 | Pending |
| 7. Production Readiness | Release Engineer (6) | 17 | Pending |
| 8. Continuous Improvement | Overseer (0) | 14 | Pending |

**Current Task**: 1.1.1 — Audit {Binding} vs {x:Bind} usage (UI Engineer)

---

## 📋 QUICK NAVIGATION

| Role | Prompt File | Quick Access | Primary Gates | One-Liner |
|------|-------------|--------------|---------------|-----------|
| **0** | [ROLE_0_OVERSEER_PROMPT.md](ROLE_0_OVERSEER_PROMPT.md) | `@.cursor/prompts/ROLE_0_OVERSEER_PROMPT.md` | All (A-H) | Gate discipline, evidence, minimal drift, owners aligned |
| **1** | [ROLE_1_SYSTEM_ARCHITECT_PROMPT.md](ROLE_1_SYSTEM_ARCHITECT_PROMPT.md) | `@.cursor/prompts/ROLE_1_SYSTEM_ARCHITECT_PROMPT.md` | A, B | Guard boundaries, contracts, compatibility, ADR discipline |
| **2** | [ROLE_2_BUILD_TOOLING_PROMPT.md](ROLE_2_BUILD_TOOLING_PROMPT.md) | `@.cursor/prompts/ROLE_2_BUILD_TOOLING_PROMPT.md` | B, C | Deterministic build/publish/CI, no silent failures |
| **3** | [ROLE_3_UI_ENGINEER_PROMPT.md](ROLE_3_UI_ENGINEER_PROMPT.md) | `@.cursor/prompts/ROLE_3_UI_ENGINEER_PROMPT.md` | C, F | MVVM, VSQ tokens, no layout drift, smoke proof |
| **4** | [ROLE_4_CORE_PLATFORM_PROMPT.md](ROLE_4_CORE_PLATFORM_PROMPT.md) | `@.cursor/prompts/ROLE_4_CORE_PLATFORM_PROMPT.md` | C, D, E | Persistence, preflight, jobs, local-first stability |
| **5** | [ROLE_5_ENGINE_ENGINEER_PROMPT.md](ROLE_5_ENGINE_ENGINEER_PROMPT.md) | `@.cursor/prompts/ROLE_5_ENGINE_ENGINEER_PROMPT.md` | E | Quality + functions, adapter-first, pinned deps |
| **6** | [ROLE_6_RELEASE_ENGINEER_PROMPT.md](ROLE_6_RELEASE_ENGINEER_PROMPT.md) | `@.cursor/prompts/ROLE_6_RELEASE_ENGINEER_PROMPT.md` | C, H | Installer lifecycle proof, gate evidence, no MSIX |
| **7** | [ROLE_7_DEBUG_AGENT_PROMPT.md](ROLE_7_DEBUG_AGENT_PROMPT.md) | `@.cursor/prompts/ROLE_7_DEBUG_AGENT_PROMPT.md` | B, C, D, E | Root-cause, triage, cross-layer fixes, validation |
| **8** | [ROLE_8_PROJECT_INTELLIGENCE_ANALYST_PROMPT.md](ROLE_8_PROJECT_INTELLIGENCE_ANALYST_PROMPT.md) | `@.cursor/prompts/ROLE_8_PROJECT_INTELLIGENCE_ANALYST_PROMPT.md` | — (read-only) | Maps, traces, contradiction memos, official-doc research — **no edits** |

---

## 📚 HOW TO USE THESE PROMPTS

### For Cursor AI

1. **Start new chat** in Cursor
2. **Use @-mention** to reference the role prompt: `@.cursor/prompts/ROLE_X_..._PROMPT.md`
3. **Add universal preprompt**: `@.cursor/commands/prompt-universal.md` (if available)
4. **Describe your task** in the context of that role
5. **Agent assumes role** and operates according to prompt

**Example**:
```
@.cursor/prompts/ROLE_2_BUILD_TOOLING_PROMPT.md

I need to resolve the XAML compiler exit code 1 issue (VS-0035). 
Please diagnose and fix the build failure.
```

### For ChatGPT / Claude

1. **Copy entire prompt** from the role file
2. **Paste into system message** or initial prompt
3. **Describe your task** in a follow-up message
4. **Agent assumes role** and operates accordingly

### For Multi-Agent Orchestration

1. **Assign one AI agent per role**
2. **Load corresponding prompt** for each agent
3. **Coordinate via Overseer** (Role 0)
4. **Use Quality Ledger** for handoffs between roles

---

## 🎯 ROLE RESPONSIBILITY MATRIX

### Phase-Gate-Role Alignment

| Gate | Phase | Description | Role 0 | Role 1 | Role 2 | Role 3 | Role 4 | Role 5 | Role 6 |
|------|-------|-------------|--------|--------|--------|--------|--------|--------|--------|
| **A** | 0A | Governance + Compatibility Lock | **P** | **P** | S | - | - | - | - |
| **B** | 0B | Build Determinism | S | **P** | **P** | - | - | - | - |
| **C** | 1A | Boot + Health Stability | S | S | **P** | **P** | **P** | - | **P** |
| **D** | 1B | Runtime + Storage Durability | S | S | - | - | **P** | S | - |
| **E** | 2 | Engine Integration | S | S | - | - | S | **P** | - |
| **F** | 3 | UI Compliance | S | - | - | **P** | - | - | - |
| **G** | 4 | Comprehensive QA | **P** | S | S | S | S | S | S |
| **H** | 5 | Packaging + Installer Validation | S | - | S | - | - | - | **P** |

**Legend**:
- **P** = Primary responsibility (drives completion, creates evidence)
- **S** = Supporting role (provides input, reviews, or assists)
- **-** = Not typically involved

---

## 🔄 ROLE COORDINATION PATTERNS

### Handoff Flow

```
Overseer (Role 0)
    ↓ Task Assignment
Role N (Primary Owner)
    ↓ Implementation + Proof
Overseer (Role 0)
    ↓ Evidence Validation
Quality Ledger Update
    ↓ Gate Advancement
Next Role (as needed)
```

### Cross-Role Dependencies

```
         To →
From ↓   | Overseer | Architect | Build | UI | Platform | Engine | Release
---------|----------|-----------|-------|----|---------|---------|---------
Overseer |    -     |   Tasks   | Tasks |Tasks| Tasks  | Tasks  | Tasks
Architect|  Review  |     -     |Contract|Spec| Contract| Spec   |   -
Build    |  Proof   |     -     |   -   |XAML|    -    |   -    | Publish
UI       |  Proof   |     -     |Issues |  - | Service | Status |  Test
Platform |  Proof   | Contract  |   -   |API |    -    | Adapter|  Boot
Engine   |  Proof   |   ADR     |Venv   |  - | Adapter |   -    |  Test
Release  |  Proof   |     -     | Logs  |  - |  Crash  |   -    |   -
```

---

## 🎯 CONFLICT RESOLUTION HIERARCHY

When roles conflict, authority follows this hierarchy by domain:

| Conflict Domain | Winning Role | Rationale |
|-----------------|--------------|-----------|
| **Boundaries/Contracts** | System Architect | Module boundaries are architectural invariants |
| **Determinism/Enforcement** | Build & Tooling | Build reproducibility is foundational |
| **UX + Desktop Correctness** | UI Engineer | User experience is domain expertise |
| **Runtime + Storage** | Core Platform | Data integrity and orchestration are critical |
| **Audio/Model Correctness** | Engine Engineer | ML/audio quality requires domain expertise |
| **Installer/Upgrade Safety** | Release Engineer | Deployment is high-risk, domain-specific |
| **Gates + Evidence** | Overseer | Quality governance supersedes all |

---

## 📊 CURRENT PROJECT STATUS

### Gate Status

| Gate | Status | Description |
|------|--------|-------------|
| A | ✅ GREEN | Governance + Compatibility Lock |
| B | ✅ GREEN | Build Determinism |
| C | ✅ GREEN | Boot + Health Stability |
| D | ✅ GREEN | Runtime + Storage Durability |
| E | ✅ GREEN | Engine Integration Baseline |
| F | ✅ GREEN | UI Compliance |
| G | ✅ GREEN | Comprehensive QA |
| H | ✅ GREEN | Packaging + Installer Validation |

**All Gates GREEN (100%)** — Quality Ledger 33/33 DONE

### Active Plan

**Ultimate Master Plan 2026 (Optimized)**
- 8 phases, 145 tasks
- Current: Phase 1, Task 1.1.1
- Owner: UI Engineer (Role 3)

### Next Immediate Priority

**Task 1.1.1** — Audit {Binding} vs {x:Bind} usage across all XAML Views (UI Engineer)

---

## 🛠️ PROMPT STRUCTURE OVERVIEW

Each role prompt includes:

### Standard Sections

1. **Role Identity** — Who you are, mission, responsibilities
2. **Non-Negotiables** — Absolute requirements and constraints
3. **Required Reading** — Documentation to review before action
4. **Current Responsibilities** — Active tasks and priorities
5. **Operational Workflows** — Step-by-step processes
6. **Quality Standards** — Definition of Done, verification methods
7. **Tools & Commands** — Scripts, CLI tools, MCP servers
8. **Role Coordination** — Dependencies and handoff patterns
9. **Output Format** — Expected deliverable structure
10. **Related Documentation** — Comprehensive guides and references
11. **Execution Philosophy** — Principles and approach

### Consistent Elements

- **Gate Focus**: Primary gates this role drives
- **Authority Domain**: Areas where this role has final say
- **Contact Pattern**: How to engage this role
- **Worked Examples**: Real ledger items demonstrating workflows

---

## 🔗 INTEGRATION WITH PROJECT DOCUMENTATION

### Comprehensive Role Guides

Each prompt is a **quick-start companion** to comprehensive role guides:

- Full workflows: `docs/governance/roles/ROLE_N_*_GUIDE.md`
- Role index: `docs/governance/ROLE_GUIDES_INDEX.md`
- Role protocol: `Recovery Plan/ROLE_SYSTEM_AND_OVERSEER_PROTOCOL.md`

### Relationship to Other Systems

```
Role Prompts (.cursor/prompts/)
    ↓ Quick-start for AI agents
Role Guides (docs/governance/roles/)
    ↓ Comprehensive workflows + examples
Agent Rules (.cursor/rules/)
    ↓ Mandatory compliance rules
Quality Ledger (Recovery Plan/)
    ↓ Task tracking + evidence
```

---

## 📚 RELATED DOCUMENTATION

### Primary References

| Document | Purpose | Owner |
|----------|---------|-------|
| [ROLE_GUIDES_INDEX.md](../../docs/governance/ROLE_GUIDES_INDEX.md) | Comprehensive role guides index | Overseer |
| [ROLE_SYSTEM_AND_OVERSEER_PROTOCOL.md](../../Recovery%20Plan/ROLE_SYSTEM_AND_OVERSEER_PROTOCOL.md) | Role boundaries protocol | Overseer |
| [QUALITY_LEDGER.md](../../Recovery%20Plan/QUALITY_LEDGER.md) | Task tracking + evidence | Overseer |
| [MASTER_ROADMAP_UNIFIED.md](../../docs/governance/MASTER_ROADMAP_UNIFIED.md) | Unified roadmap + gates | Overseer |
| [ROLE_CHEATSHEET.md](../../docs/developer/ROLE_CHEATSHEET.md) | Quick reference | Developer |

### Supporting References

| Document | Purpose |
|----------|---------|
| [OVERSEER_REFERENCE.md](../../docs/REFERENCE/OVERSEER_REFERENCE.md) | Overseer authority document |
| [WORKERS_REFERENCE.md](../../docs/REFERENCE/WORKERS_REFERENCE.md) | Worker system documentation |
| [DEFINITION_OF_DONE.md](../../docs/governance/DEFINITION_OF_DONE.md) | Quality standards |
| [CANONICAL_REGISTRY.md](../../docs/governance/CANONICAL_REGISTRY.md) | Document governance registry |

---

## 🎯 USAGE PATTERNS

### Single-Role Agent (Cursor Chat)

```
User opens Cursor chat → References role prompt → Agent assumes role → Executes task → Reports results
```

**Example**:
```
@.cursor/prompts/ROLE_2_BUILD_TOOLING_PROMPT.md

Please resolve VS-0035 (XAML compiler exit code 1).
```

### Multi-Role Orchestra (Advanced)

```
Overseer Chat (Role 0)
    ├─ Assigns tasks to roles
    ├─ Monitors progress via ledger
    └─ Coordinates handoffs

Worker Chats (Roles 1-6)
    ├─ Receive assignments via ledger
    ├─ Execute with role-specific prompts
    ├─ Report proof to Overseer
    └─ Update ledger with evidence
```

### Task-Specific Usage

| Task Type | Recommended Role | Prompt |
|-----------|------------------|--------|
| Fix build failure | Role 2 | ROLE_2_BUILD_TOOLING_PROMPT.md |
| Add new panel | Role 3 | ROLE_3_UI_ENGINEER_PROMPT.md |
| Implement storage | Role 4 | ROLE_4_CORE_PLATFORM_PROMPT.md |
| Integrate engine | Role 5 | ROLE_5_ENGINE_ENGINEER_PROMPT.md |
| Build installer | Role 6 | ROLE_6_RELEASE_ENGINEER_PROMPT.md |
| Review ADR | Role 1 | ROLE_1_SYSTEM_ARCHITECT_PROMPT.md |
| Gate coordination | Role 0 | ROLE_0_OVERSEER_PROMPT.md |
| Deep dive / research / trace (read-only) | Role 8 | ROLE_8_PROJECT_INTELLIGENCE_ANALYST_PROMPT.md |

---

## 🚀 QUICK START GUIDE

### Step 1: Identify Your Role

Check the responsibility matrix or task category to determine which role is appropriate.

### Step 2: Load Role Prompt

In Cursor: `@.cursor/prompts/ROLE_N_[NAME]_PROMPT.md`

In ChatGPT/Claude: Copy entire prompt content

### Step 3: Review Current Status

Check these files:
- `.cursor/STATE.md` — Current phase and active task
- `Recovery Plan/QUALITY_LEDGER.md` — Assigned issues
- Gate status in roadmap

### Step 4: Execute Task

Follow the role-specific workflows in the prompt.

### Step 5: Report Results

Update Quality Ledger with proof artifacts and evidence.

---

## 📊 ROLE SPECIALIZATION BREAKDOWN

### By Technology Stack

| Technology | Primary Role | Supporting Roles |
|------------|--------------|------------------|
| **WinUI 3 / XAML** | UI Engineer (3) | Build & Tooling (2) |
| **C# ViewModels** | UI Engineer (3) | Core Platform (4) |
| **FastAPI Backend** | Core Platform (4) | System Architect (1) |
| **Python Engines** | Engine Engineer (5) | Core Platform (4) |
| **MSBuild / CI** | Build & Tooling (2) | System Architect (1) |
| **Inno Setup** | Release Engineer (6) | Build & Tooling (2) |

### By Knowledge Domain

| Domain | Primary Role | Authority |
|--------|--------------|-----------|
| **Architecture** | System Architect (1) | Boundaries, Contracts |
| **Build System** | Build & Tooling (2) | Determinism, CI/CD |
| **User Experience** | UI Engineer (3) | Visual, MVVM |
| **Data/Runtime** | Core Platform (4) | Storage, Jobs |
| **ML/Audio** | Engine Engineer (5) | Quality, Inference |
| **Deployment** | Release Engineer (6) | Installer, Lifecycle |
| **Governance** | Overseer (0) | Gates, Evidence |

---

## 🎯 STANDARD PROMPT FORMAT

All role prompts follow this structure:

```markdown
# Role N: [Name] - Complete System Prompt

## 🎯 ROLE IDENTITY
- Who you are
- Core mission
- Primary responsibilities

## 🚨 NON-NEGOTIABLES
- Absolute constraints
- Red lines (what NEVER to do)

## 📖 REQUIRED READING (BEFORE ANY ACTION)
- Primary references (1-5)
- Secondary references (6-10)

## 🎯 CURRENT RESPONSIBILITIES
- Active gate focus
- Current status
- Immediate priorities

## 🔄 OPERATIONAL WORKFLOWS
- Step-by-step processes
- Decision trees
- Common scenarios

## ✅ QUALITY STANDARDS
- Definition of Done
- Verification methods
- Review checklists

## 🛠️ TOOLS & COMMANDS
- Scripts and CLI tools
- MCP servers
- Key commands

## 👥 ROLE COORDINATION
- Dependencies on other roles
- Conflict resolution
- Handoff patterns

## 🎨 OUTPUT FORMAT
- Expected deliverable structure
- Status reports
- Evidence format

## 📚 RELATED DOCUMENTATION
- Comprehensive guides
- Reference documents
- Supporting materials

## 🎯 EXECUTION PHILOSOPHY
- Core principles
- Operating philosophy
- Quality approach
```

---

## 🔗 INTEGRATION POINTS

### With Comprehensive Guides

Prompts are **quick-start companions** to comprehensive guides:

- **Prompts** (`.cursor/prompts/`): Quick role assumption for AI agents
- **Guides** (`docs/governance/roles/`): Detailed workflows, examples, templates

### With Agent Rules

Prompts reference but don't duplicate agent rules:

- **Prompts**: Role-specific context and workflows
- **Rules** (`.cursor/rules/`): Universal compliance requirements

### With Quality Ledger

Prompts guide agents to interact with Quality Ledger:

- **Prompts**: Explain ledger usage for each role
- **Ledger** (`Recovery Plan/QUALITY_LEDGER.md`): Source of truth for tasks

### With Gate System

Prompts align with phase-gate architecture:

- **Prompts**: Define gate responsibilities per role
- **Roadmap** (`docs/governance/MASTER_ROADMAP_UNIFIED.md`): Gate definitions

---

## 📋 PROMPT MAINTENANCE

### When to Update Prompts

Update prompts when:
- Gate definitions change
- Role responsibilities shift
- New tools/MCPs added
- Current status changes significantly
- Comprehensive guides updated

### Update Protocol

1. Identify change trigger
2. Update affected role prompt(s)
3. Update this index
4. Update comprehensive guide if needed
5. Update version number and date
6. Document change in changelog section

### Quality Standards for Prompts

- **Clear**: Unambiguous instructions
- **Complete**: All necessary context included
- **Current**: Reflects latest project status
- **Consistent**: Same structure across all roles
- **Actionable**: Provides concrete next steps

---

## 🎯 PROMPT EFFECTIVENESS METRICS

### Success Indicators

- Agent correctly assumes role identity
- Agent follows workflows in prompt
- Agent produces expected output format
- Agent coordinates with other roles
- Agent updates ledger appropriately
- Agent respects non-negotiables

### Common Issues

| Issue | Root Cause | Fix |
|-------|------------|-----|
| Agent ignores constraints | Non-negotiables not clear | Strengthen non-negotiables section |
| Agent skips reading | Too many references | Prioritize primary references |
| Agent produces wrong format | Output format unclear | Add more examples |
| Agent violates boundaries | Coordination not clear | Strengthen role coordination section |

---

## 📚 ADDITIONAL RESOURCES

### Command Shortcuts

For one-liner role invocation, see:
- `.cursor/commands/role-*.md` — Quick command files
- `docs/developer/ROLE_CHEATSHEET.md` — Quick reference

### Training Materials

For understanding the role system:
- `Recovery Plan/ROLE_SYSTEM_AND_OVERSEER_PROTOCOL.md` — Role boundaries
- `docs/governance/ROLE_GUIDES_INDEX.md` — Comprehensive guides
- `docs/REFERENCE/OVERSEER_REFERENCE.md` — Overseer authority

### Quality References

For quality standards:
- `docs/governance/DEFINITION_OF_DONE.md` — Quality standards
- `.cursor/rules/workflows/closure-protocol.mdc` — Closure requirements
- `.cursor/rules/workflows/error-resolution.mdc` — Error resolution standards

---

## 📝 CHANGELOG

| Version | Date | Changes |
|---------|------|---------|
| 1.1.0 | 2026-02-04 | Added Ultimate Master Plan 2026 phase ownership; updated gate status to all GREEN |
| 1.0.0 | 2026-01-25 | Initial creation of all 7 role prompts |

---

## 📞 SUPPORT & CONTACT

### For Role Questions

- Check comprehensive guide: `docs/governance/roles/ROLE_N_*_GUIDE.md`
- Check role protocol: `Recovery Plan/ROLE_SYSTEM_AND_OVERSEER_PROTOCOL.md`
- Escalate to Overseer (Role 0) for conflicts

### For Prompt Issues

- Report to: Overseer (Role 0)
- Update process: See "Prompt Maintenance" section above
- Emergency: Revert to comprehensive guide

---

**Status**: ACTIVE SYSTEM
**Version**: 1.1.0
**Last Updated**: 2026-02-04
**Owner**: Overseer (Role 0)
**Purpose**: AI agent role assumption for VoiceStudio development
**Active Plan**: Ultimate Master Plan 2026 (Optimized)
