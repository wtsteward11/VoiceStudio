# Execution row discipline (bounded lanes)

**Status:** Canonical governance (2026-04-07).  
**Note:** Cursor rule files under `.cursor/rules/` are **user-owned**; this document is the repo’s **canonical** execution-row contract until or unless the same text is adopted into rules with explicit user approval.

## 1. Row type (required at freeze)

Every execution row MUST declare one of:

| Type | Meaning |
|------|---------|
| **runtime-affecting** | Production code, tests, schemas, or installer behavior may change. |
| **proof-hardening** | **No production behavior change** — documentation, tracker/registry/STATE, or test-only locks for already-shipped behavior. |

**Proof-hardening** rows MUST state explicitly: *“No production code paths changed in this lane.”* (Test-only changes are allowed if they add assertions for existing behavior.)

## 2. Failure-path parity (closure gate)

A lane MUST NOT be marked **Closed** unless:

- The **happy path** is specified in the execution row or closure report, and
- The **failure / degraded path** is specified with equal rigor (what the operator sees, what is rolled back, what events fire or do not fire, what consumers do).

## 3. Allowlist commits

- Stage **only** paths listed on the row’s allowlist (plus unavoidable typo fixes in the same files).
- **STATE.md** only when closure-grade sync is complete in the same commit.
- One bounded lane SHOULD correspond to **one behavior narrative** so **revert** removes one story, not several.

## 4. Honest limits as backlog

Closure report **Honest limits** / **Hard OUT** bullets are **first-class backlog** inputs for the next lane queue — prefer them over undisciplined feature brainstorming.

## 5. Pre-existing code

If runtime code existed before the row was frozen, the row MUST say:

- what **pre-existed**,
- what was **newly validated** or **changed** in this lane.

## 6. References

- Gap tracker: [PROFESSIONAL_GAP_TRACKER.md](../design/PROFESSIONAL_GAP_TRACKER.md)
- Panel / architecture guardrails: [GUARDRAILS.md](../design/GUARDRAILS.md) (includes transcript mutation outcome taxonomy)
- Document governance: [DOCUMENT_GOVERNANCE.md](DOCUMENT_GOVERNANCE.md)
