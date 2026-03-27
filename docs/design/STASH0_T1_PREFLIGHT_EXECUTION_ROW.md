# stash@{0} — T1 Backend voice/synthesis + HTTP — preflight (planning-only)

**Row ID:** **GOV-STASH0-T1-PREFLIGHT-01**  
**Purpose:** Decompose [`.cursor/STATE.md`](../../.cursor/STATE.md) **T1 — Backend voice/synthesis + HTTP surface** (parked in **`stash@{0}`**) into **bounded subclusters**, recommend **one** first executable slice, and **freeze** a **future** implementation row **only after product picks that slice** — **no** stash extract and **no** `backend/**` or `app/core/**` edits under this preflight row.  
**Source stash:** *WIP: pre-Pass06-20260326 unclassified local and untracked* — inventory via `git stash show --name-only 'stash@{0}'` (**2026-03-24** preflight inventory pass).

**Post–T1-S1 (2026-03-27):** **T1-C1** (**eight-path** synthesis vertical) landed via **[`GOV-STASH0-T1-S1-01`](STASH0_T1_S1_EXECUTION_ROW.md)** (**closed** on **`main`**). **T1 remainder** re-baseline and next-slice planning: **[`GOV-STASH0-T1-R1-PREFLIGHT-01`](STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md)**. This doc remains the historical **T1-C1–C4** decomposition referenced by **T1-S1**.

**Related:** [STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md) (**remainder preflight**); [STASH0_T1_S1_EXECUTION_ROW.md](STASH0_T1_S1_EXECUTION_ROW.md) (**T1-S1** — **closed**); [STASH0_T2_VERIFY_CI_EXECUTION_ROW.md](STASH0_T2_VERIFY_CI_EXECUTION_ROW.md) (**T2** — **closed**); [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md); STATE **T3** row (contracts — **not** in scope for T1 preflight **IN** paths).

---

## 1. Sign-off

| Role | Decision | Date |
|------|----------|------|
| **Product / engineering** | **T1-S1 complete** — **T1-C1** slice implemented per [STASH0_T1_S1_EXECUTION_ROW.md](STASH0_T1_S1_EXECUTION_ROW.md). **Next:** accept **T1 remainder** breakdown and **one** slice per [STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md) **§5**, or **explicit pause**. | **2026-03-27** (narrative) |

---

## 2. Objective (single sentence)

Produce an **execution-grade preflight** that shrinks **T1** from a monolithic stash bucket into **2–4 subclusters** with blast radius and **T3 coupling** flags, and names **exactly one** recommended **first** executable slice — **without** landing stash hunks or changing backend/app-core code.

---

## 3. Baseline truth (do not contradict)

- **Repo-global Quick** (post–**T1-S1**): **`artifacts/verify/20260326_230934`** / **`commit_hash`** **`f60477978e72ad3bdbcfb2f2ba7e56c50ebc76c3`** — see [`latest_pointer.json`](../../artifacts/verify/latest_pointer.json). **T2** Quick **`20260326_211554`** / **`dc07e515`** is **historical**. **`git rev-parse HEAD`** may differ after **docs-only** commits; **do not** equate **HEAD** and **`latest_pointer.json`** without checking.
- **`GOV-STASH0-T1-S1-01`** is **closed**; **T1-C1** paths are on **`main`**. Remainder mapping: [STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md).
- **`stash@{0}`** remains **parked**; T1 **Partial**, T3/T4 **Parked**, T5 **Discard** per [`.cursor/STATE.md`](../../.cursor/STATE.md) topic table.
- **T3** paths present in the same stash entry (`docs/api/openapi.json`, `shared/schemas/*`, `tests/contract/*`) are **out of scope** for this preflight’s **IN** list; they are cited only under **coupling** for T1 slices.

---

## 4. Stash-derived T1 subclusters

Paths below are **T1-filtered** from `git stash show --name-only 'stash@{0}'` (voice/synthesis/service/v3 synthesis-adjacent, ancillary routes, cross-cutting HTTP, synthesis/golden tests). **Excluded** from cluster membership but **high-coupling** to T1: **T3** contract surfaces (see §6).

### 4.1 Subcluster table

| Subcluster | Name | Representative paths (stash@{0}) | Blast radius (one line) | T3 coupling risk |
|------------|------|-----------------------------------|-------------------------|------------------|
| **T1-C1** | **Synthesis vertical + engine/settings seam** | `backend/api/routes/voice/synthesis.py`, `backend/api/routes/voice/_shared.py`, `backend/services/synthesis_service.py`, `backend/api/v3/synthesis.py`, `backend/api/v3/engines.py`, `backend/api/v3/voices.py`, `app/core/engines/base.py`, `backend/core/settings.py` | End-to-end synthesis and engine selection break; golden-loop and stub/test-mode behavior at risk. | **High** — v3 and voice routes often drive request/response shapes consumed by contracts and clients. |
| **T1-C2** | **Ancillary HTTP routes + registry + supporting services** | `backend/api/route_registry.py`, `backend/api/routes/help.py`, `backend/api/routes/library.py`, `backend/api/routes/profiles.py`, `backend/api/routes/realtime_settings.py`, `backend/api/routes/search.py`, `backend/api/routes/shortcuts.py`, `backend/api/v3/projects.py`, `backend/api/v3/models.py`, `backend/services/audio_path_resolver.py`, `backend/services/audit_logger.py`, `backend/services/script_store.py` | Non-synthesis APIs, wiring, and helpers break; broader UI/backend integration surface. | **Medium–high** — especially `v3/models.py` and route payloads; registry changes can ripple to discovery tests. |
| **T1-C3** | **HTTP cross-cutting (auth, rate limits, extra models)** | `backend/api/middleware/auth_middleware.py`, `backend/api/rate_limiting.py`, `backend/api/rate_limiting_enhanced.py`, `backend/api/models_additional.py`, `tests/unit/backend/api/middleware/test_auth_middleware.py`, `tests/unit/backend/api/test_models_additional.py` | Every authenticated or rate-limited request path; global error/validation behavior. | **High** — `models_additional` and auth flows are contract-adjacent; STATE lists `test_models_additional` under **T3** as well as T1. Coordinate **future** row boundaries with OpenAPI/schema owners. |
| **T1-C4** | **Synthesis policy + golden-loop CI tests** | `tests/ci/test_golden_loop_smoke.py`, `tests/unit/test_synthesis_policy.py` | CI golden path and synthesis policy gates fail if mis-landed vs `main`. | **Low–medium** — tests assert backend behavior; contract files not in this cluster unless a future row explicitly adds them. |

**Gate:** Subclusters are **separable** (orthogonal files with manageable overlap at `synthesis_service` / v3 boundaries). Overlap is handled by **one** first slice that **owns** the synthesis vertical before sweeping ancillary routes or global middleware.

---

## 5. Recommendation — first executable slice

**Proposed slice ID:** **T1-S1** (maps to a **future** execution row such as **`GOV-STASH0-T1-S1-01`** — **not** defined in this file).

**IN (conceptual lock for the future row — not active until product signs that row):**

- **T1-C1** in full: voice synthesis routes, `synthesis_service.py`, v3 slices **`synthesis.py`**, **`engines.py`**, **`voices.py`**, **`app/core/engines/base.py`**, **`backend/core/settings.py`** (as reconciled vs `main`).

**Rationale:** Matches STATE’s leading **T1** representatives (`synthesis.py`, `synthesis_service.py`, v3 cluster, engine base, settings). Delivers a **single narrative** — “synthesis path coherence” — before touching ancillary routes or global middleware.

**Explicit deferrals (not T1-S1 until that row closes):**

- **T1-C3** (auth, rate limits, `models_additional`) — default **later** unless product explicitly prioritizes security/limits ahead of synthesis landing.
- **T1-C2** (ancillary routes + `projects`/`models` v3 + helper services) — after **T1-S1** unless product orders otherwise.
- **T1-C4** — may be included **with** **T1-S1** **only if** the signed execution row states **proof co-dependency**; default: follow **T1-S1** or land as **T1-S2** “test alignment” slice to keep first merge smaller.

**Alternative first slice (if product insists tests-first):** **T1-S2** = **T1-C4** only — smaller diff, but **without** service/route reconciliation may leave **red** CI until **T1-S1** lands; treat as **non-default**.

---

## 6. Anti-patterns (reject if seen)

| Anti-pattern | Why it fails |
|--------------|--------------|
| **Mono-row “all of T1”** | Swallows `backend/api/v3/*.py`, all rate limiting, all routes — unmaintainable review; violates stash discipline. |
| **T3 smuggled under T1** | Editing `docs/api/openapi.json`, `shared/schemas/**`, or `tests/contract/**` without a **T3**-aware or **joint T1+T3** signed row breaks contract honesty. |
| **`git stash pop`** / **bulk checkout** of **`stash@{0}`** | Reintroduces T2/T3/T4/T5 churn; use **selective** apply only on a **future** signed file lock (mirror T2 §7). |
| **Pointer vs HEAD collapse** | Claiming **`latest_pointer.json`** commit = **`HEAD`** after doc-only commits — false proof narrative. |

---

## 7. Next row placeholder (future work)

When product confirms **T1-S1** (or another slice):

1. **Draft a new execution row** with its **own** ID (recommended: **`GOV-STASH0-T1-S1-01`**).
2. That row must include: **file lock (IN)**, **OUT** (explicit **T3** unless joint scope), **§5 proof commands**, **§8 closure** discipline — mirroring [STASH0_T2_VERIFY_CI_EXECUTION_ROW.md](STASH0_T2_VERIFY_CI_EXECUTION_ROW.md) where appropriate.
3. **No** `backend/**` or `app/core/**` implementation until that row’s **§1** is dated for **implementation**.

**Execution row drafted (2026-03-24):** [STASH0_T1_S1_EXECUTION_ROW.md](STASH0_T1_S1_EXECUTION_ROW.md) — **`GOV-STASH0-T1-S1-01`**; **eight-path** lock (**T1-C1**); **§1 Pending** until product completes **§3** preflight and signs.

---

## 8. Product gate (fork)

| Outcome | Action |
|---------|--------|
| **Product picks T1-S1** (recommended) | **§1** on [STASH0_T1_S1_EXECUTION_ROW.md](STASH0_T1_S1_EXECUTION_ROW.md) — selective checkout from **`stash@{0}`** per **eight-path** lock (**§4**). |
| **Product picks another slice** (e.g. T1-S2 tests-first, or T1-C3 priority) | Same — **new** row ID; **do not** repurpose this preflight doc as the execution lock. |
| **Product declines / pause** | STATE **Active Task** → **None** + dated pause; this doc remains **Deferred** or **archived recommendation**; T1 stays **Parked**. |

---

## 9. Preflight success criteria (this document only)

- [x] **GOV-STASH0-T1-PREFLIGHT-01** documented with **four** subclusters (**T1-C1**–**T1-C4**), blast radius, **T3** coupling column.
- [x] **One** recommended first slice (**T1-S1** = **T1-C1**) with deferrals and alternative noted.
- [x] Anti-patterns and **future** execution-row placeholder recorded.
- [x] **No** stash extract and **no** backend implementation in this lane.

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-24 | **T1-S1 execution row drafted** — [STASH0_T1_S1_EXECUTION_ROW.md](STASH0_T1_S1_EXECUTION_ROW.md) (**`GOV-STASH0-T1-S1-01`**); **§1**/**§7**/**§8** pointer updated in this doc. |
| 2026-03-27 | **T1-S1 closed** on **`main`**; **§3** baseline refreshed (global Quick = **T1-S1**); **T1 remainder** → [STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md) (**`GOV-STASH0-T1-R1-PREFLIGHT-01`**). |
