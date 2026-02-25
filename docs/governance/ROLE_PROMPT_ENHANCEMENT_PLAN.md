# VoiceStudio Role Prompts, Skills & Context — Enhancement Plan

> **Version**: 1.0.0
> **Date**: 2026-02-24
> **Author**: External Architecture Review
> **Status**: PROPOSAL — Requires Overseer Approval
> **Scope**: All 9 prompts (Roles 0–7 + Skeptical Validator), 60+ skills, 7 core rules

---

## Executive Summary

The VoiceStudio role system is impressively comprehensive — a 9-prompt, 60+ skill, multi-layer governance framework is rare in AI-assisted development. After a full review of every role prompt, representative skills, commands, and core rules, this document identifies **27 enhancement opportunities** organized into 5 priority tiers. The system's strengths are its gate discipline, evidence requirements, and clear boundaries. Its weaknesses are stale context, generic identity preambles, missing failure-recovery patterns, and gaps in cross-role coordination specifics.

---

## Table of Contents

1. [Findings by Category](#1-findings-by-category)
2. [Priority 1 — Critical Fixes (Stale/Contradictory Content)](#2-priority-1--critical-fixes)
3. [Priority 2 — Structural Enhancements (All Prompts)](#3-priority-2--structural-enhancements)
4. [Priority 3 — Per-Role Upgrades](#4-priority-3--per-role-upgrades)
5. [Priority 4 — Skills Gaps & Improvements](#5-priority-4--skills-gaps--improvements)
6. [Priority 5 — Context & Rules Refinements](#6-priority-5--context--rules-refinements)
7. [Implementation Roadmap](#7-implementation-roadmap)
8. [Appendix A — Proposed Universal Preamble](#appendix-a--proposed-universal-preamble)
9. [Appendix B — Proposed Failure-Recovery Template](#appendix-b--proposed-failure-recovery-template)
10. [Appendix C — Proposed Session-Continuity Protocol](#appendix-c--proposed-session-continuity-protocol)

---

## 1. Findings by Category

### 1.1 What Works Well

The existing system demonstrates several strong practices worth preserving:

- **Gate discipline** is well-defined with evidence requirements per gate (A–H).
- **Role boundaries** are explicit with a clear authority matrix and conflict resolution hierarchy.
- **Output specifications** per role ensure consistent deliverables.
- **ReAct reasoning pattern** is embedded in every prompt, encouraging structured thinking.
- **Cross-role coordination tables** exist in every prompt.
- **Skills** have consistent frontmatter schema and "When to Use" triggers.
- **Rules** use `alwaysApply: true` effectively for project-wide constraints.

### 1.2 Systemic Issues Found

| # | Issue | Severity | Affected |
|---|-------|----------|----------|
| 1 | **Identical generic preamble** across all roles — "You are a Professional senior software architecture engineer expert…" — undermines role differentiation | HIGH | All 8 roles |
| 2 | **Stale status data** baked into prompts — VS-0035 listed as "current blocker" in Role 2 while all gates are GREEN | HIGH | Roles 0, 2, 6 |
| 3 | **No failure-recovery playbooks** — prompts say what to do when things go right but lack structured recovery when things go wrong | HIGH | All roles |
| 4 | **No token/context budget awareness** — prompts don't guide agents on prioritizing reads when context window is limited | MEDIUM | All roles |
| 5 | **No session-continuity protocol** — how to resume after a conversation break or context compaction is undefined | MEDIUM | All roles |
| 6 | **Missing "What NOT to do" examples** — non-negotiables list prohibitions but lack concrete anti-pattern illustrations | MEDIUM | Roles 1, 3, 4, 5 |
| 7 | **Inconsistent audience/output spec depth** — Role 7 and Skeptical Validator have much tighter output specs than Roles 4 and 5 | MEDIUM | Roles 4, 5 |
| 8 | **Skills not mapped to roles** — no authoritative skill-to-role assignment matrix | MEDIUM | All skills |
| 9 | **Missing skills for critical domains** — no dedicated skills for IPC/named-pipes, database/migrations, CI/CD pipeline authoring, or accessibility testing | MEDIUM | Skills system |
| 10 | **Commands are thin wrappers** — role commands (`.cursor/commands/role-*.md`) are 14–35 lines vs. 400+ line prompts they reference; they add little value | LOW | All commands |

---

## 2. Priority 1 — Critical Fixes

These should be addressed immediately as they cause agents to operate on false information.

### 2.1 Replace Generic Preamble with Role-Specific Identity

**Problem**: Every role opens with the same sentence:
> "You are a Professional senior software architecture engineer expert. plan out your next tasks and complete them it must be approved by your peers who are also Professional senior software architecture engineer experts."

This is generic, grammatically awkward, and tells the agent nothing role-specific. It actually dilutes role identity because the agent sees the same text regardless of which role it assumes.

**Fix**: Replace with a role-specific identity block that immediately establishes expertise, voice, and operating posture. See [Appendix A](#appendix-a--proposed-universal-preamble) for the proposed replacement.

**Example for Role 5 (Engine Engineer)**:
```markdown
## 🎯 ROLE IDENTITY

You are the **VoiceStudio Engine Engineer (Role 5)** — a senior ML/audio 
systems specialist with deep expertise in TTS inference, voice cloning 
pipelines, audio DSP, and GPU-accelerated model serving.

You think in terms of: latency budgets, VRAM utilization, MOS scores, 
adapter contracts, and subprocess isolation. You are skeptical of quality 
claims without metrics and you never ship an engine change without a 
proof run.

Your voice is technical, precise, and evidence-driven. You communicate 
quality through numbers, not adjectives.
```

### 2.2 Remove Baked-In Status — Use Dynamic References Only

**Problem**: Multiple prompts contain hardcoded status that goes stale:
- Role 2 lists VS-0035 as "Current Critical Blocker" with detailed investigation steps
- Role 0 lists all gates as GREEN with specific task numbers
- Role 3 lists "Current Task: 1.1.1" which may no longer be current
- Role 6 lists specific Quality Ledger items by ID

**Fix**: Replace all hardcoded status with dynamic read instructions:

```markdown
## 🎯 CURRENT STATUS (ALWAYS READ FRESH)

> ⚠️ **Do NOT rely on any status information written in this prompt.**
> Status is dynamic. Always read these files at the start of every session:

1. **`.cursor/STATE.md`** — Current phase, active task, blockers, next 3 steps
2. **`Recovery Plan/QUALITY_LEDGER.md`** — Open issues assigned to your role
3. **Gate status** — Run `python scripts/run_verification.py` or read 
   `.buildlogs/verification/last_run.json`

### Your First Action Every Session
```bash
# 1. Read current state
cat .cursor/STATE.md

# 2. Check your assigned issues
grep -i "Role 5\|Engine" "Recovery Plan/QUALITY_LEDGER.md"

# 3. Verify gate status
python scripts/run_verification.py --quiet
```
```

### 2.3 Fix Architecture Rule Contradiction

**Problem**: The `architecture.mdc` rule states:
> "Distribution: Windows installer (MSIX or Inno Setup)."

But every role prompt has "NO MSIX" as a non-negotiable, and ADR-010 explicitly prohibits MSIX.

**Fix**: Update `architecture.mdc` line to:
```
- Distribution: Inno Setup installer (unpackaged EXE). MSIX is prohibited per ADR-010.
```

---

## 3. Priority 2 — Structural Enhancements

These changes should be applied to **all prompts** as a structural upgrade pattern.

### 3.1 Add Failure-Recovery Section to Every Role

**Problem**: Prompts describe success workflows but not failure workflows. When an agent encounters an unexpected state (build breaks mid-fix, test fails after change, storage corruption, etc.), there's no structured recovery guidance.

**Fix**: Add a `## 🚨 FAILURE RECOVERY` section to every prompt. See [Appendix B](#appendix-b--proposed-failure-recovery-template) for the template.

**Example for Role 2 (Build & Tooling)**:
```markdown
## 🚨 FAILURE RECOVERY

### If Your Fix Breaks the Build
1. `git stash` or `git checkout -- .` immediately
2. Verify clean build: `git clean -xfd && dotnet build VoiceStudio.sln ...`
3. If clean build also fails → this is a pre-existing issue, log in ledger
4. If clean build passes → your change introduced the break, bisect

### If CI Passes Locally But Fails Remotely
1. Check CI runner environment (SDK version, OS build)
2. Compare local `global.json` against CI workflow
3. Check for path-length issues (Windows MAX_PATH)
4. Escalate to Debug Agent (Role 7) if unclear after 15 minutes

### If You Don't Know What To Do
1. STOP — do not make speculative changes
2. Document current state in `.cursor/STATE.md` under "Blockers"
3. Escalate to Overseer (Role 0) with: symptom, what you tried, what you need
```

### 3.2 Add Session-Continuity Protocol

**Problem**: When a conversation ends (context window full, user returns later, or agent is swapped), there's no protocol for resuming work. Agents may lose context on what was attempted and what failed.

**Fix**: Add `## 🔄 SESSION CONTINUITY` to every prompt. See [Appendix C](#appendix-c--proposed-session-continuity-protocol).

### 3.3 Add Token-Budget Awareness

**Problem**: Prompts list 10+ "Required Reading" files without prioritization. Agents may exhaust context windows reading documentation before performing any work.

**Fix**: Add tiered reading priorities and estimated sizes:

```markdown
## 📖 REQUIRED READING (PRIORITIZED)

### Tier 1 — Always Read First (~2K tokens)
1. `.cursor/STATE.md` — Current phase and task (small file)
2. Gate status from last verification run

### Tier 2 — Read When Relevant (~5K tokens)
3. `Recovery Plan/QUALITY_LEDGER.md` — Scan for your assigned items only
4. Relevant `.cursor/rules/**/*.mdc` for your current task domain

### Tier 3 — Reference As Needed (large files, read sections)
5. Comprehensive role guide — read specific sections, not entire file
6. Architecture documentation — read only relevant ADRs

> **Context Budget Rule**: If you've consumed >50% of context on reading 
> alone, STOP reading and start working. You can look up specific details 
> as needed during implementation.
```

### 3.4 Add Concrete Anti-Pattern Examples to Non-Negotiables

**Problem**: Non-negotiables are stated as rules but lack illustrative examples of violations.

**Fix**: Add "What This Violation Looks Like" examples:

```markdown
## 🚨 NON-NEGOTIABLES

### ❌ NO placeholder implementations
**What This Violation Looks Like**:
```python
# VIOLATION — function exists but does nothing meaningful
async def synthesize(self, text: str, voice_id: str) -> AudioResult:
    return AudioResult(audio_path="placeholder.wav", metrics={})
    
# CORRECT — fully implemented or raises NotImplementedError with issue ref
async def synthesize(self, text: str, voice_id: str) -> AudioResult:
    raise NotImplementedError("VS-0045: XTTS init blocked, see ledger")
```
```

### 3.5 Strengthen Command Files

**Problem**: Command files (`.cursor/commands/role-*.md`) are thin and add little value beyond "reference the prompt file."

**Fix**: Enhance commands to include a compact "cold start" checklist:

```markdown
# Role: Engine Engineer (Role 5)

## Quick Assume
@.cursor/prompts/ROLE_5_ENGINE_ENGINEER_PROMPT.md

## Cold Start Checklist (run these first)
1. `cat .cursor/STATE.md` — check current phase
2. `grep "Role 5\|Engine\|Gate E" "Recovery Plan/QUALITY_LEDGER.md"` — your issues
3. `python scripts/run_verification.py --quiet` — gate status
4. `ls engines/*/engine.manifest.json | wc -l` — engine count

## Quick Commands
- Smoke test: `python app/cli/benchmark_engines.py --engine <name>`
- Proof run: `python scripts/baseline_voice_workflow_proof.py --engine <name>`
- All engines: `curl http://localhost:8000/api/engines`
- Quality: `python scripts/quality_scorecard.py`

## Escalation
- Build issue → Role 2 (Build & Tooling)
- Contract change → Role 1 (System Architect)
- Cross-layer bug → Role 7 (Debug Agent)
- Blocker → Role 0 (Overseer)
```

---

## 4. Priority 3 — Per-Role Upgrades

### 4.1 Role 0 (Overseer) — Add Decision Framework

**Gap**: The Overseer prompt describes daily cadence but lacks a decision framework for common judgment calls (when to block a gate, when to approve a waiver, how to prioritize competing S1 issues).

**Add**:
```markdown
## ⚖️ DECISION FRAMEWORK

### When to Block a Gate
Block gate advancement when:
- ANY S0 issue is open against the gate
- Proof artifacts are missing or invalid
- Evidence was produced more than 7 days ago (may be stale)

### When to Grant a Conditional Pass
- S1/S2 issues exist but are documented with mitigations
- Time-boxed waiver with explicit re-check date
- Document waiver in ledger with "CONDITIONAL PASS" tag

### Prioritizing Competing Issues
Priority order: Safety > Data Integrity > Build Stability > Functionality > UX > Performance
Within same tier: S0 > S1 > S2; Older > Newer; Blocking-others > Self-contained
```

### 4.2 Role 1 (System Architect) — Add Contract Versioning Strategy

**Gap**: The Architect prompt discusses contracts but doesn't define a versioning strategy for shared schemas.

**Add**: A section on schema versioning patterns (additive-only changes vs. breaking changes, deprecation timelines, consumer migration windows).

### 4.3 Role 3 (UI Engineer) — Add XAML Safety Cross-Reference

**Gap**: Role 3 doesn't reference the `xaml-build-doctor` skill despite XAML being its primary domain.

**Add**: Explicit skill activation guidance:
```markdown
### Required Skill Activation
When editing ANY .xaml file, activate the XAML Build Doctor skill:
@.cursor/skills/xaml-build-doctor/skill.md
This skill contains forbidden patterns that crash the WinAppSDK 1.8 compiler.
```

### 4.4 Role 4 (Core Platform) — Add Concurrency Patterns

**Gap**: Role 4 owns job runtime and storage but the prompt lacks async/concurrency guidance for Python.

**Add**: Section on asyncio patterns, file locking for atomic writes on Windows, and the specific race conditions that have bitten this project before.

### 4.5 Role 5 (Engine Engineer) — Add GPU/CPU Fallback Decision Matrix

**Gap**: Prompt says "implement CPU fallback" but doesn't specify decision criteria.

**Add**:
```markdown
### GPU/CPU Fallback Decision Matrix
| Engine | GPU Required | CPU Viable | Fallback Strategy |
|--------|-------------|------------|-------------------|
| XTTS v2 | Recommended | Slow but works | Auto-detect, warn on CPU |
| Whisper | Recommended | Works well | Auto-detect, no warning |
| Piper | No | Primary | CPU-only engine |
| RVC v2 | Required | Not viable | Error with clear message |
| Bark | Recommended | Very slow | Auto-detect, warn user |
```

### 4.6 Role 7 (Debug Agent) — Add Diagnostic Decision Tree

**Gap**: Role 7 has investigation workflows but lacks a structured diagnostic decision tree for common failure categories.

**Add**: A flowchart-style section covering the top 5 failure modes (build failures, runtime crashes, test failures, performance regressions, cross-layer contract mismatches) with specific first-step diagnostics for each.

### 4.7 Skeptical Validator — Add False-Positive Detection Guidance

**Gap**: The Validator prompt covers verification workflow but doesn't address how to distinguish genuine completions from false positives (tests that pass for wrong reasons, builds that succeed with warnings suppressed, etc.).

**Add**: Section on common false-positive patterns and how to detect them.

---

## 5. Priority 4 — Skills Gaps & Improvements

### 5.1 Missing Skills (Create New)

| Skill Name | Domain | Priority | Rationale |
|------------|--------|----------|-----------|
| `ipc-named-pipes` | Backend/IPC | HIGH | Named pipe IPC is core architecture (ADR-007) but has no skill |
| `database-migrations` | Backend/Storage | HIGH | SQLite migrations are critical for storage durability |
| `ci-cd-pipeline` | Build/DevOps | MEDIUM | GitHub Actions workflow authoring needs guardrails |
| `accessibility-testing` | UI/Quality | MEDIUM | Accessibility is a stated responsibility but no skill exists |
| `python-async-patterns` | Backend/Runtime | MEDIUM | Async job runtime is a common source of bugs |
| `inno-setup-installer` | Release | MEDIUM | Installer scripting has no dedicated skill |
| `context-manager` | Platform/Tools | LOW | Role 4 owns context manager but no skill guides usage |

### 5.2 Skill Structure Improvements

**Add Role Ownership to Every Skill**: Each skill should declare which role(s) own it:
```yaml
---
name: xaml-build-doctor
roles: [2, 3]  # Build & Tooling, UI Engineer
...
---
```

**Add "When NOT to Use" Section**: Skills have "When to Use" but lack negative triggers to prevent over-activation.

**Add Estimated Token Cost**: Skills vary from 150 to 200 lines. Agents should know the cost:
```yaml
---
name: audio-engine-specialist
token_estimate: ~3500
---
```

### 5.3 Skill-to-Role Assignment Matrix

Create a new file `.cursor/skills/SKILL_ROLE_MATRIX.md`:

```markdown
| Skill | Role 0 | Role 1 | Role 2 | Role 3 | Role 4 | Role 5 | Role 6 | Role 7 |
|-------|--------|--------|--------|--------|--------|--------|--------|--------|
| audio-engine-specialist | | | | | | **P** | | S |
| xaml-build-doctor | | | **P** | **P** | | | | S |
| security-auditor | S | S | S | | S | | S | S |
| ... | | | | | | | | |
```

---

## 6. Priority 5 — Context & Rules Refinements

### 6.1 Update `architecture.mdc`

- Fix MSIX contradiction (Section 2.3 above)
- Add explicit mention of the 44-engine manifest system
- Add subprocess boundary rule (engines run in isolated processes)
- Reference ADR-007 for IPC boundary

### 6.2 Update `project-context.mdc`

- Add the 7-role system as project context (currently absent from this always-apply rule)
- Add reference to STATE.md as the session-start file
- Add Quality Ledger reference
- Add gate system summary (A–H)

### 6.3 Create New Rule: `session-protocol.mdc`

```markdown
---
description: "Required session start/end protocol for all AI agents."
alwaysApply: true
---

# Session Protocol

## On Session Start
1. Read `.cursor/STATE.md` for current phase, task, blockers
2. Read your role prompt for identity and constraints
3. Check Quality Ledger for assigned issues
4. State your role, current task, and planned next steps

## On Session End (or context getting long)
1. Update `.cursor/STATE.md` with:
   - What was accomplished
   - What remains
   - Any new blockers discovered
2. Commit or stash any work-in-progress
3. Document any decisions made (ADR if structural)

## On Context Compaction
If the conversation is being compacted or summarized:
1. Preserve: current task, what's been tried, what failed, next step
2. Drop: file contents already committed, passing test output, exploratory reads
```

### 6.4 Create New Rule: `evidence-standards.mdc`

Consolidate the scattered evidence requirements into a single always-apply rule that defines what constitutes valid proof for each gate.

---

## 7. Implementation Roadmap

### Phase 1: Critical Fixes (1 session)
| Task | Owner | Est. Effort |
|------|-------|-------------|
| 2.1 Replace generic preambles in all 8 role prompts | Overseer | 30 min |
| 2.2 Remove baked-in status from all prompts | Overseer | 20 min |
| 2.3 Fix MSIX contradiction in architecture.mdc | System Architect | 5 min |

### Phase 2: Structural Enhancements (2 sessions)
| Task | Owner | Est. Effort |
|------|-------|-------------|
| 3.1 Add Failure Recovery section to all prompts | Overseer + each role | 60 min |
| 3.2 Add Session Continuity protocol | Overseer | 20 min |
| 3.3 Add Token-Budget awareness to Required Reading | Overseer | 30 min |
| 3.4 Add anti-pattern examples to Non-Negotiables | Each role owner | 45 min |
| 3.5 Enhance command files | Overseer | 30 min |

### Phase 3: Per-Role Upgrades (2 sessions)
| Task | Owner | Est. Effort |
|------|-------|-------------|
| 4.1–4.7 Role-specific enhancements | Each role owner | 20 min each |

### Phase 4: Skills & Rules (2 sessions)
| Task | Owner | Est. Effort |
|------|-------|-------------|
| 5.1 Create 7 new skills | Domain owners | 30 min each |
| 5.2–5.3 Skill metadata & matrix | Overseer | 45 min |
| 6.1–6.4 Rule updates and new rules | System Architect | 60 min |

**Total estimated effort**: 6–8 focused sessions

---

## Appendix A — Proposed Universal Preamble

Replace the current identical opener in all prompts with this two-part structure:

### Part 1: Universal (same in all prompts, but well-crafted)

```markdown
## 🌐 UNIVERSAL CONTEXT

You are an AI agent operating within the **VoiceStudio** repository — a hybrid 
native Windows desktop application (WinUI 3 + C# frontend, FastAPI + Python 
backend, 44 AI/ML engine integrations). The project follows an 8-gate quality 
system (A–H) with a 7-role engineering team.

**Core Constraints** (apply to ALL roles):
- Local-first: core features work offline
- No MSIX: Inno Setup installer only (ADR-010)
- No placeholders: fully implemented or explicitly raise NotImplementedError
- No drift: architectural changes require ADRs
- Evidence required: no task closure without proof artifacts
- Read `.cursor/STATE.md` before any action
```

### Part 2: Role-Specific (unique per role)

Each role gets a tailored identity paragraph that establishes:
1. **Expertise domain** (what you know deeply)
2. **Thinking style** (what you optimize for)
3. **Communication voice** (how you report)
4. **Operating posture** (cautious/aggressive/balanced)

Example for Role 3 (UI Engineer):
```markdown
You are the **VoiceStudio UI Engineer (Role 3)** — a senior WinUI 3 and XAML 
specialist with deep expertise in MVVM architecture, Fluent Design token systems, 
data binding pipelines, and desktop accessibility.

You think in terms of: binding contexts, template selectors, panel host layouts, 
and design token compliance. You are protective of the 3-row shell structure and 
treat binding failures as blockers, not warnings.

Your voice is visual and precise. You communicate with layout diagrams, token 
references, and screenshot evidence. You escalate XAML compiler issues immediately 
rather than attempting speculative fixes.
```

---

## Appendix B — Proposed Failure-Recovery Template

Add this section to every role prompt, customized for the role's domain:

```markdown
## 🚨 FAILURE RECOVERY

### Recovery Principle
When something goes wrong, **stabilize first, investigate second, fix third**.
Never make a second change to fix a first change that broke something.

### If Your Change Breaks the Build
1. Revert immediately: `git checkout -- <files>` or `git stash`
2. Verify clean state builds: `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
3. If clean also fails → pre-existing issue, log in ledger, do not blame your change
4. If clean passes → your change caused it, bisect to identify the breaking line

### If Your Change Passes Build But Fails Tests
1. Read the test failure carefully — is it testing YOUR change or unrelated?
2. If your change → fix the implementation, not the test (unless test is wrong)
3. If unrelated → log as separate issue, proceed with your task

### If You're Stuck (>15 minutes with no progress)
1. STOP making changes
2. Document what you know and what you've tried
3. Escalate:
   - Technical unknown → Debug Agent (Role 7)
   - Scope/priority unclear → Overseer (Role 0)
   - Boundary/contract question → System Architect (Role 1)

### If You Discover Something Alarming
(Security vulnerability, data corruption risk, gate regression)
1. Stop current work
2. Log immediately in Quality Ledger with S0 severity
3. Notify Overseer (Role 0) in your output
4. Do NOT attempt to fix silently
```

---

## Appendix C — Proposed Session-Continuity Protocol

```markdown
## 🔄 SESSION CONTINUITY

### Starting a New Session
Every session begins with this 60-second boot sequence:

1. **Read state**: `cat .cursor/STATE.md` 
2. **Identify yourself**: State your role number, name, and current task
3. **Check blockers**: Are there any S0/S1 issues blocking your work?
4. **State plan**: "I will [specific action] to advance [specific goal]"

### Example Session Start Output
```
SESSION START — Role 5 (Engine Engineer)
State: Phase 3 (API/Contract Sync), Task 3.2.1
My Task: Validate XTTS adapter contract against manifest v3 schema
Blockers: None (Gate E is GREEN)
Plan: Read current XTTS manifest, compare to IEngine contract, 
      run adapter smoke test, report compliance status.
```

### Ending a Session (or approaching context limit)
Before ending or when context is getting long:

1. **Summarize progress**: What was accomplished this session
2. **Document state**: Update `.cursor/STATE.md` if task status changed
3. **Flag continuations**: What should the next session pick up
4. **Commit**: `git add -A && git commit -m "WIP: [brief description]"`

### Resuming After a Break
If you're continuing work that a previous session started:

1. Check git log: `git log --oneline -5`
2. Check STATE.md for continuation notes
3. Verify current build state before making changes
4. Re-read your role prompt's Non-Negotiables (easy to forget across sessions)
```

---

## Summary of All Proposed Changes

| # | Change | Type | Priority | Effort |
|---|--------|------|----------|--------|
| 1 | Replace generic preamble with role-specific identity | Fix | P1 | Low |
| 2 | Remove hardcoded status, use dynamic reads | Fix | P1 | Low |
| 3 | Fix MSIX contradiction in architecture.mdc | Fix | P1 | Trivial |
| 4 | Add Failure Recovery section to all prompts | Enhancement | P2 | Medium |
| 5 | Add Session Continuity protocol | Enhancement | P2 | Low |
| 6 | Add Token-Budget awareness to readings | Enhancement | P2 | Low |
| 7 | Add anti-pattern examples to non-negotiables | Enhancement | P2 | Medium |
| 8 | Enhance command files with cold-start checklists | Enhancement | P2 | Low |
| 9 | Role 0: Add decision framework | Enhancement | P3 | Low |
| 10 | Role 1: Add contract versioning strategy | Enhancement | P3 | Low |
| 11 | Role 3: Add XAML skill cross-reference | Enhancement | P3 | Trivial |
| 12 | Role 4: Add concurrency patterns | Enhancement | P3 | Medium |
| 13 | Role 5: Add GPU/CPU fallback matrix | Enhancement | P3 | Low |
| 14 | Role 7: Add diagnostic decision tree | Enhancement | P3 | Medium |
| 15 | Skeptical Validator: Add false-positive guidance | Enhancement | P3 | Low |
| 16 | Create 7 new skills for gap domains | New | P4 | High |
| 17 | Add role ownership to all skill frontmatter | Enhancement | P4 | Medium |
| 18 | Add "When NOT to Use" to skills | Enhancement | P4 | Medium |
| 19 | Add token estimates to skill frontmatter | Enhancement | P4 | Low |
| 20 | Create skill-to-role matrix | New | P4 | Low |
| 21 | Update architecture.mdc | Fix | P5 | Low |
| 22 | Update project-context.mdc | Enhancement | P5 | Low |
| 23 | Create session-protocol.mdc | New | P5 | Low |
| 24 | Create evidence-standards.mdc | New | P5 | Medium |
| 25 | Add role system to project-context.mdc | Enhancement | P5 | Low |
| 26 | Add gate system to project-context.mdc | Enhancement | P5 | Low |
| 27 | Standardize output spec depth across all roles | Enhancement | P5 | Medium |

---

**Document Status**: PROPOSAL
**Next Step**: Overseer (Role 0) review and approval
**Implementation Owner**: Overseer coordinates, each role owner implements their section
