# STATE.md Trim Plan

**Purpose:** Bounded plan for reducing `.cursor/STATE.md` bloat while preserving operational truth. **Do not execute until user approval.**  
**Date:** 2026-03-22  
**Target:** [.cursor/STATE.md](../../.cursor/STATE.md)  
**Related:** PR-13 Slice Selection plan

---

## Current State

- **File size:** ~1646 lines
- **Operational sections:** ACTIVE WINDOW, LATEST MILESTONE, LATEST PROOF INDEX
- **Historical sludge:** 20+ "Previous Active Task", Phase 1–9 summaries, Session Log, Test Coverage Summary, Verified UI/UX Components, Overseer Queue, SSOT Pointers, duplicate Proof Index

---

## Sections to Preserve (No Changes)

| Section | Lines (approx) | Reason |
|---------|----------------|--------|
| Baseline Protection | 3–30 | Reference for restore |
| ACTIVE WINDOW | 32–43 | Operational truth |
| HISTORY LEDGER | 45–82 | LATEST MILESTONE, LATEST PROOF INDEX |
| Current Phase | 83–88 | High-level context |
| Active Plan | 90–95 | Current plan reference |
| Active Task | 97–104 | Current task (if any) |

---

## Sections to Condense

| Section | Action |
|---------|--------|
| Previous Active Task (first 5–10) | Keep condensed: ID, Title, Status, 1-line summary. Remove long deliverables. |
| Next 3 Steps | Keep current; archive superseded bullets |

---

## Sections to Archive

| Section | Destination | Rationale |
|---------|--------------|-----------|
| Previous Active Task (beyond ~10) | `docs/archive/STATE_HISTORY.md` | Deep history no longer aids current work |
| Earlier Active Task, Earlier Milestone | `docs/archive/STATE_HISTORY.md` | Same |
| Phase 1–9 summaries (Phase N Summary — COMPLETE) | `docs/archive/STATE_HISTORY.md` | Pre-2026-03; reference only |
| Test Coverage Summary | `docs/archive/STATE_HISTORY.md` or `docs/reports/` | Stale; regenerate from pytest/dotnet test if needed |
| Verified UI/UX Components | `docs/archive/STATE_HISTORY.md` | Stale panel list |
| Architecture Verification | `docs/archive/STATE_HISTORY.md` | Static; move to docs/developer/ if still relevant |
| Test Artifact | `docs/archive/STATE_HISTORY.md` | Outdated |
| Session Log | `docs/archive/STATE_HISTORY.md` | Archived 2026-01-28; historical only |
| Next 3 Steps (superseded bullets) | Trim; keep last 10–15 actionable items | Reduce noise |
| Overseer Queue / Validator Escalations | Archive if empty/stale; keep if active | |
| Context Acknowledgment (duplicate) | Consolidate with ACTIVE WINDOW | |
| SSOT Pointers | Keep if current; archive if duplicated elsewhere | |
| Proof Index (if duplicated) | LATEST PROOF INDEX is canonical; remove duplicates | |
| Recently Completed (Phase 5, 6, 7, 8 details) | Archive; keep one "Phase 5–8 complete" summary line | |

---

## Execution Order (When Approved)

1. **Create archive file:** `docs/archive/STATE_HISTORY.md`
2. **Copy** archived sections to STATE_HISTORY.md with a header (e.g., "Archived from .cursor/STATE.md on YYYY-MM-DD")
3. **Remove** archived sections from STATE.md
4. **Condense** first 5–10 Previous Active Task entries
5. **Trim** Next 3 Steps to last 10–15 actionable bullets
6. **Verify** ACTIVE WINDOW, LATEST MILESTONE, LATEST PROOF INDEX unchanged and still at top

---

## Target Outcome

| Metric | Before | After (target) |
|--------|--------|----------------|
| Total lines | ~1646 | ~200–300 |
| ACTIVE WINDOW | Preserved | Preserved |
| LATEST MILESTONE | Preserved | Preserved |
| LATEST PROOF INDEX | Preserved | Preserved |
| Previous Active Task | 20+ entries | 5–10 condensed |
| Phase summaries | Inline | In docs/archive/STATE_HISTORY.md |

---

## Rollback

If trim causes loss of needed context:
- Restore from `docs/archive/STATE_HISTORY.md`
- Git revert the STATE.md edit

---

## Approval Gate

**Do not execute this plan until:**
- User explicitly approves
- No active extraction or closure in progress
