# stash@{0} — T1 remainder — preflight (planning-only, post–T1-S1)

**Row ID:** **GOV-STASH0-T1-R1-PREFLIGHT-01**  
**Purpose:** **Re-baseline** the **`stash@{0}`** **T1** surface against current **`main`** after **[`GOV-STASH0-T1-S1-01`](STASH0_T1_S1_EXECUTION_ROW.md)** landed the **T1-C1 / eight-path** synthesis vertical. Produce **one** narrow candidate for the **next** implementation slice—**no** selective checkout, **no** `backend/**` or **`app/core/**`** edits, **no** bulk **`stash pop`** under this row.  
**Source stash:** *WIP: pre-Pass06-20260326 unclassified local and untracked* (same entry as prior T1 rows).  
**Date (row drafted):** 2026-03-27  
**§1 authorization:** **Pending** — engineering accepts **§3** re-baseline; product / engineering picks **exactly one** remainder slice (**§5**) before any **new** execution row **§1** implementation date.

**Related:** [STASH0_T1_S1_EXECUTION_ROW.md](STASH0_T1_S1_EXECUTION_ROW.md) (**T1-S1** — **closed** on **`main`**); [STASH0_T1_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_PREFLIGHT_EXECUTION_ROW.md) (original **T1-C1–C4** decomposition — **T1-S1** consumed **T1-C1**); [STASH0_T2_VERIFY_CI_EXECUTION_ROW.md](STASH0_T2_VERIFY_CI_EXECUTION_ROW.md); [`.cursor/STATE.md`](../../.cursor/STATE.md).

---

## 1. Sign-off

| Role | Decision | Date |
|------|----------|------|
| **Engineering (preflight)** | **Pending** — Confirm **§3** table reflects current **`main`** vs **`stash@{0}`** (re-run per-path diff if either tip moved). | — |
| **Product / engineering (slice choice)** | **Pending** — Select **one** option from **§5** (or record **explicit pause**: no next T1 slice until new product gate). | — |

**Hard gate:** **No** **§4 file lock** exists on **this** preflight doc. The **future** execution row (new ID, e.g. **`GOV-STASH0-T1-S2-01`** or other) owns **§4** / **§7** / **§8** only after **§1** here is satisfied and that row is drafted.

---

## 2. Objective (single sentence)

After **T1-S1**, classify every **`stash@{0}`** path that still differs from **`main`** in the **T1** topic into **remainder** buckets, explicitly **OUT** items, and **test-only** deltas—so product can authorize **one** next bounded extraction row **without** pretending the old preflight map is unchanged.

---

## 3. Baseline truth (do not contradict)

- **`GOV-STASH0-T1-S1-01`** is **closed**; **T1-C1** (eight paths) is on **`main`**. Repo-global **Quick** remains **`artifacts/verify/20260326_230934`** / **`commit_hash`** **`f60477978e72ad3bdbcfb2f2ba7e56c50ebc76c3`** until a new green **Quick** advances [`latest_pointer.json`](../../artifacts/verify/latest_pointer.json).
- **`git merge-base main 'stash@{0}'`** = **`a7a45f4cc2e8e81671eefffe885df3a86227b10a`** (recorded **2026-03-27**). **`main`** tip recorded at preflight authoring: **`737f0886`** — **verify** with **`git rev-parse HEAD`** before relying on hashes.
- **`T1-S1` eight paths** vs **`stash@{0}`**: **empty diffs** (stash content **matches** landed **`main`** for those files). **Do not** re-extract them under a “remainder” row without a **new** §1 and cause.

---

## 4. Re-baseline — `git diff main 'stash@{0}'` (T1 stash inventory)

**Scope:** All **`stash@{0}`** paths under **`backend/`** and **`app/core/`** (PowerShell inventory **2026-03-27**).  
**Command template per path:** `git diff --stat main 'stash@{0}' -- <path>`

### 4.1 **T1-S1 lock (landed — empty vs `main`)**

| Path | `git diff --shortstat` | Note |
|------|------------------------|------|
| `app/core/engines/base.py` | *(empty)* | **T1-S1** landed |
| `backend/api/routes/voice/_shared.py` | *(empty)* | **T1-S1** landed |
| `backend/api/routes/voice/synthesis.py` | *(empty)* | **T1-S1** landed |
| `backend/api/v3/engines.py` | *(empty)* | **T1-S1** landed |
| `backend/api/v3/synthesis.py` | *(empty)* | **T1-S1** landed |
| `backend/api/v3/voices.py` | *(empty)* | **T1-S1** landed |
| `backend/core/settings.py` | *(empty)* | **T1-S1** landed |
| `backend/services/synthesis_service.py` | *(empty)* | **T1-S1** landed |

### 4.2 **Remainder — still material vs `main`**

| Path | `git diff --shortstat` (2026-03-27) | Proposed bucket |
|------|-------------------------------------|-----------------|
| `backend/api/middleware/auth_middleware.py` | 4 insertions, 1 deletion | **T1-C3** (cross-cutting) |
| `backend/api/models_additional.py` | 45 insertions, 15 deletions | **T1-C3** |
| `backend/api/rate_limiting.py` | 15 insertions | **T1-C3** |
| `backend/api/rate_limiting_enhanced.py` | 4 insertions, 12 deletions | **T1-C3** |
| `backend/api/route_registry.py` | 2 insertions | **T1-C2** (ancillary / registry) |
| `backend/api/routes/help.py` | 4 insertions, 4 deletions | **T1-C2** |
| `backend/api/routes/library.py` | 9 insertions | **T1-C2** |
| `backend/api/routes/profiles.py` | 10 insertions, 2 deletions | **T1-C2** |
| `backend/api/routes/realtime_settings.py` | 4 insertions, 3 deletions | **T1-C2** |
| `backend/api/routes/search.py` | 1 insertion, 1 deletion | **T1-C2** |
| `backend/api/routes/shortcuts.py` | 1 insertion, 1 deletion | **T1-C2** |
| `backend/api/v3/models.py` | 4 insertions, 3 deletions | **T1-C2** (T3-coupled — see **§6**) |
| `backend/api/v3/projects.py` | 4 insertions, 3 deletions | **T1-C2** (T3-coupled — see **§6**) |
| `backend/services/audio_path_resolver.py` | 7 insertions | **T1-C2** |
| `backend/services/audit_logger.py` | 19 insertions, 7 deletions | **T1-C2** |
| `backend/services/script_store.py` | 6 insertions, 4 deletions | **T1-C2** |
| `backend/mcp_bridge/README.md` | 1 insertion, 1 deletion | **OUT** — tooling/doc; not synthesis vertical |
| `backend/plugins/sandbox/resource_monitor.py` | 10 insertions, 4 deletions | **OUT** — plugin sandbox; not default T1 remainder |

### 4.3 **Tests in `stash@{0}`** (proof / policy — **not** in **T1-S1** §4)

| Path | `git diff --shortstat` | Note |
|------|------------------------|------|
| `tests/ci/test_golden_loop_smoke.py` | 38 insertions, 1 deletion | **T1-C4** — new row must state **Option A/B** for stash vs proof-only |
| `tests/unit/test_synthesis_policy.py` | 9 insertions, 3 deletions | **T1-C4** |
| `tests/unit/backend/api/middleware/test_auth_middleware.py` | 62 insertions, 53 deletions | **T1-C3** companion — **OUT** unless **T1-C3** slice is chosen |
| `tests/unit/backend/api/test_models_additional.py` | 49 insertions | **T1-C3** / **T3**-adjacent — **OUT** unless **T1-C3** slice is chosen |

**Narrowing rule:** If any path above is **empty** on re-run, update this table **before** dating **§1** (same discipline as [STASH0_T1_S1_EXECUTION_ROW.md](STASH0_T1_S1_EXECUTION_ROW.md) **§2**).

---

## 5. Recommendation — **one** next slice (product choice)

**Do not** implement multiple clusters in one row. Pick **one**:

| Option | Scope (indicative) | When to pick |
|--------|-------------------|--------------|
| **A (default mentor alignment)** | **T1-C2 remainder only:** ancillary routes + **`route_registry.py`** + **`audio_path_resolver`**, **`audit_logger`**, **`script_store`** — **exclude** `v3/models.py` and `v3/projects.py` **unless** product accepts **T3 coupling** risk in the same row | Prefer **HTTP coherence** without global auth/rate-limit churn |
| **B** | **T1-C2 “wide”** — Option **A** **plus** **`backend/api/v3/models.py`** + **`projects.py`** | Accept **higher T3 coupling**; still **no** OpenAPI/schema lock files **unless** a **T3** row says so |
| **C** | **T1-C3** — auth, rate limiting, **`models_additional.py`** (+ tests if row §4 lists them) | Product prioritizes cross-cutting security/limits **over** route sweep |
| **D** | **T1-C4 tests-first** — golden-loop + synthesis policy tests from stash | Smaller code blast; **must** define proof policy (**§5.1**-style) on the **execution** row to avoid red CI |
| **E** | **Explicit pause** | No pressure — **STATE** documents pause; **no** extract |

**Forbidden:** “All remainder T1 in one row”, **`git stash pop`**, **T3** contracts/OpenAPI/`shared/schemas/**` under a T1 banner, **W8 / W7 / Product Trust / A4 / Pass 06** reopen, **`stash@{0}`** mining outside the **execution** row **§4**.

---

## 6. OUT (strict — any execution row spawned from this preflight)

Unless a **new** signed row explicitly expands scope:

- **T3:** `docs/api/openapi.json`, `shared/schemas/**`, `tests/contract/**` (except as **read-only** proof if a row allows)
- **T4:** Doc/governance sweeps (minimal **STATE** / registry lines **after** proof only)
- **T5:** `LibraryView.xaml.cs` stash resurrection vs **A4** **`main`**
- **`backend/mcp_bridge/README.md`**, **`backend/plugins/sandbox/resource_monitor.py`** — default **OUT** for **Option A / E**; require **new** row ID to include
- Any path **not** listed on the **execution** row **§4**

---

## 7. Extraction method (future execution row — mirror T1-S1 / T2)

1. **`git stash list`** — confirm **`stash@{0}`** message matches [`.cursor/STATE.md`](../../.cursor/STATE.md).  
2. **`git switch -c <branch> main`**  
3. **`git checkout 'stash@{0}' -- <exact §4 list>`** — **no** bulk **`stash pop`**.

---

## 8. Next actions

| Step | Owner | Action |
|------|-------|--------|
| 1 | Engineering | Date **§1** preflight row after confirming **§3** (or refresh **§3** if tips moved) |
| 2 | Product | Choose **§5** option **A–E** |
| 3 | Overseer / engineer | Draft **new** execution row: **§4** file lock, **§5** proof commands, **§6** OUT, **§7** extract, **§8** closure — **then** date **implementation §1** on **that** row |
| 4 | — | **No** `backend/**` / **`app/core/**`** code until step 3 **implementation** **§1** is dated |

---

## 9. Preflight success criteria (this document only)

- [x] **T1-S1** eight paths show **empty** diff vs **`stash@{0}`** on **`main`** (recorded **2026-03-27**).
- [x] All other **`backend/`** / **`app/core/`** stash paths tabled with **bucket** + **OUT** where applicable.
- [x] Stash **test** deltas cited; **T1-C3** test companions flagged **OUT** unless **T1-C3** slice is chosen.
- [x] **§5** forces **one** slice; default **T1-C2** narrow **Option A** documented.
- [x] **No** stash extract and **no** implementation under **`GOV-STASH0-T1-R1-PREFLIGHT-01`**.

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-27 | **Created** — **`GOV-STASH0-T1-R1-PREFLIGHT-01`**: post–**T1-S1** re-baseline; remainder map; **§1** **Pending**. |
