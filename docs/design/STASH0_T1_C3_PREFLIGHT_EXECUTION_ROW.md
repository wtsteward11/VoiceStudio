# stash@{0} — T1-C3 cross-cutting — preflight (planning-only, post–R1A)

**Row ID:** **GOV-STASH0-T1-C3-PREFLIGHT-01**  
**Purpose:** **Re-baseline** the **T1-C3** candidate surface (**auth**, **rate limiting**, **`models_additional`**, companion tests) in **`stash@{0}`** vs **current** **`main`** after **[`GOV-STASH0-T1-R1A-EXEC-01`](STASH0_T1_R1A_EXECUTION_ROW.md)** closed. Classify **subclusters**, **T3 bleed risk**, and **test companions**; recommend **exactly one** next bounded implementation slice. **No** selective checkout, **no** `backend/**` or **`app/core/**`** edits, **no** bulk **`stash pop`** under this row.  
**Source stash:** *WIP: pre-Pass06-20260326 unclassified local and untracked* (same entry as prior T1 rows).  
**Date (row drafted):** 2026-03-28  
**§1 authorization:** **Engineering (preflight)** — **§3/§4** re-baseline **2026-03-28** on **`main`** **`37fb89e6`** (preflight authoring tip); **post-preflight** governance commit **`843e86a2`** — **re-verify** **§4.1** shortstats before extract. **Product / engineering (slice choice)** — **Option A** (**C3a** — rate limiting only) **dated** **2026-03-28**. **Implementation** authorized only on **[`GOV-STASH0-T1-C3A-EXEC-01`](STASH0_T1_C3A_EXECUTION_ROW.md)** after **that** row’s **§1** **implementation** sign-off is dated.

**Related:** [STASH0_T1_R1A_EXECUTION_ROW.md](STASH0_T1_R1A_EXECUTION_ROW.md) (**R1A** — **closed**); [STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md) (**T1-C3** seed list **§4.2**); [STASH0_T1_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_PREFLIGHT_EXECUTION_ROW.md) (original **T1** decomposition); [`.cursor/STATE.md`](../../.cursor/STATE.md).

---

## 1. Sign-off

| Role | Decision | Date |
|------|----------|------|
| **Engineering (preflight)** | **Preflight acknowledged** — **§3** + **§4** re-run on **`main`** **`37fb89e6`** vs **`stash@{0}`**; **`git merge-base main 'stash@{0}'`** **`a7a45f4cc2e8e81671eefffe885df3a86227b10a`**; top stash message matches STATE; all six T1-C3 candidate paths show **non-empty** material diffs (see **§4.1**). | **2026-03-28** |
| **Product / engineering (slice choice)** | **Option A** — **C3a** first: **`backend/api/rate_limiting.py`** + **`backend/api/rate_limiting_enhanced.py`** only; **exclude** **C3b**/**C3c** for this slice. Child execution row: **[`GOV-STASH0-T1-C3A-EXEC-01`](STASH0_T1_C3A_EXECUTION_ROW.md)**. | **2026-03-28** |

**Hard gate:** **No** implementation **§4 file lock** on **this** preflight doc. **Child execution row** owns **§4** lock, **§5** frozen proof, **§7** extract, **§8** closure.

---

## 2. Objective (single sentence)

After **R1A**, re-verify every **T1-C3** stash path that still differs from **`main`**, cluster by coupling and risk, and force **one** recommended next extraction slice—**without** code, **without** stash checkout, and **without** smuggling **T3** contract work under a **T1** banner.

---

## 3. Baseline truth (do not contradict)

- **`GOV-STASH0-T1-R1A-EXEC-01`** is **closed**; **R1A** merge tree **`0e0d0a91`** passed repo-global **Quick** **`artifacts/verify/20260327_165459`** (see **R1A §8**). **`main`** tip when **§4** stats were recorded: **`37fb89e6`**; **governance** commit adding this preflight: **`843e86a2`** — **re-run** **`git diff --shortstat main 'stash@{0}' --`** on each **§4.1** path before **C3A** extract (**narrowing rule**).
- **`git merge-base main 'stash@{0}'`** = **`a7a45f4cc2e8e81671eefffe885df3a86227b10a`** (**2026-03-28**).
- **`git stash list`** (top): **`stash@{0}: On main: WIP: pre-Pass06-20260326 unclassified local and untracked`** — matches [`.cursor/STATE.md`](../../.cursor/STATE.md) stash disposition.
- **Already landed (do not re-extract as T1-C3 remainder without cause):** **T1-S1** eight paths; **R1A** **§4** ten paths with **Partial** on **`backend/services/script_store.py`** and **`backend/api/route_registry.py`** (**main** retained). **Do not** treat R1A paths as T1-C3 scope.

---

## 4. Re-baseline — `git diff main 'stash@{0}'` (T1-C3 candidates)

**Command template per path:** `git diff --shortstat main 'stash@{0}' -- <path>` (PowerShell-quoted **`stash@{0}`**).

### 4.1 Production + companion paths (all material **2026-03-28**)

| Path | `git diff --shortstat` | Proposed bucket |
|------|------------------------|-----------------|
| `backend/api/middleware/auth_middleware.py` | 1 file changed, 4 insertions(+), 1 deletion(-) | **C3b** — auth / route dependencies |
| `backend/api/rate_limiting.py` | 1 file changed, 15 insertions(+) | **C3a** — rate limit core |
| `backend/api/rate_limiting_enhanced.py` | 1 file changed, 4 insertions(+), 12 deletions(-) | **C3a** — rate limit enhanced |
| `backend/api/models_additional.py` | 1 file changed, 45 insertions(+), 15 deletions(-) | **C3c** — **T3-adjacent** Pydantic adjunct |
| `tests/unit/backend/api/middleware/test_auth_middleware.py` | 1 file changed, 62 insertions(+), 53 deletions(-) | **Companion** — **OUT** unless child **§4** lists |
| `tests/unit/backend/api/test_models_additional.py` | 1 file changed, 49 insertions(+) | **Companion** — **OUT** unless child **§4** lists; **T3** coupling per [STASH0_T1_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_PREFLIGHT_EXECUTION_ROW.md) |

**Narrowing rule:** If any row above becomes **empty** on re-run, update **§4** and obtain **§1** re-acknowledgment before authorizing extract.

### 4.2 Subclusters (coupling narrative)

- **C3a — Rate limiting only:** `rate_limiting.py` + `rate_limiting_enhanced.py`. Wired from [`backend/api/middleware_setup.py`](../../backend/api/middleware_setup.py) via **`_initialize_rate_limiting`** (imports **`RateLimitMiddleware`** / **`rate_limit_middleware`**). **ADR-032** stack order remains authoritative for middleware; rate limit sits in the configured HTTP pipeline **before** downstream class-based middleware (CORS, correlation, etc.).  
- **C3b — Auth middleware only:** `auth_middleware.py`. Consumed primarily as **route dependencies** (**`require_auth_if_enabled`**, **`require_authentication`**) across multiple route modules, **not** as the same layer as rate-limit registration in **`middleware_setup`**. **Implication:** **C3a** can land **without** **C3b** from a **Starlette add_middleware ordering** perspective; behavioral coherence still needs **review** (e.g. unauthenticated routes vs limiter identity) on the **child execution row**.
- **C3c — Models adjunct:** `models_additional.py` — **largest diff**; highest risk of **contract / OpenAPI / consumer drift**. Treat as **isolated** slice or **defer** until **T3**-aware row if product demands schema parity.
- **C3d — Tests-only:** Pulling companion tests **without** their production files is **invalid** for default lanes; **forbid** unless execution **§4** explicitly pairs them and proof is defined.

---

## 5. Recommendation — **one** next slice (product choice)

**Do not** implement multiple clusters in one row.

| Option | Scope (indicative) | When to pick |
|--------|-------------------|--------------|
| **A (default — mentor alignment)** | **C3a only:** `backend/api/rate_limiting.py` + `backend/api/rate_limiting_enhanced.py` — **exclude** `auth_middleware.py` and **`models_additional.py`** for first landing | Prefer **HTTP limiter** coherence **without** auth dependency churn or **T3**-heavy models |
| **B** | **C3b only:** `backend/api/middleware/auth_middleware.py` (+ **`test_auth_middleware.py`** only if child **§4** lists both) | Product prioritizes **auth** behavior over rate limits |
| **C** | **C3c only:** `backend/api/models_additional.py` (+ **`test_models_additional.py`** if row expands) | Accept **T3-adjacent** review; still **no** `openapi.json` / `shared/schemas/**` / `tests/contract/**` edits under **T1** row unless **T3** row authorizes |
| **D** | **T1-C4 pivot:** `tests/ci/test_golden_loop_smoke.py`, `tests/unit/test_synthesis_policy.py` (from [STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md) **§4.3**) | Smaller surface; **must** define frozen **pytest** + proof on **execution** row |
| **E** | **Explicit pause** | No extract; **STATE** holds; **stash@{0}** unchanged |

**Primary recommendation (engineering):** **Option A** — land **C3a** first, then reassess **C3b** / **C3c** with fresh diffs.  
**Backup:** **Option B** if product insists auth before limits.  
**Forbidden:** “All **T1-C3** in one row”, **`git stash pop`**, **`v3/models.py`** / **`v3/projects.py`** under this banner, **T3** lock files without **T3** row, reopening **W8** / **W7** / Product Trust / **A4** / Pass 06 slice 5.

---

## 6. OUT (strict — any child execution row unless expanded)

Unless a **new** signed row explicitly expands scope:

- **T3:** `docs/api/openapi.json`, `shared/schemas/**`, `tests/contract/**` (read-only for analysis unless **T3** row)
- **T4:** Doc/governance sweeps (narrow **STATE** / registry lines **after** proof only)
- **T5:** `src/VoiceStudio.App/Views/Panels/LibraryView.xaml.cs` stash vs **A4** **`main`**
- **`app/core/**`**, **`src/VoiceStudio.App/**`**
- **R1A §6** carry-forward: **`backend/api/v3/models.py`**, **`backend/api/v3/projects.py`**, T1-C4 stash test checkouts, **`test_synthesis_policy.py`** / **`test_golden_loop_smoke.py`** — **OUT** unless **§5 Option D** (or new row) **§4** lists them
- Any path **not** on child execution **§4**

---

## 7. Extraction method (future execution row — mirror T1-S1 / T2 / R1A)

1. **`git stash list`** — confirm **`stash@{0}`** matches [`.cursor/STATE.md`](../../.cursor/STATE.md).  
2. **`git switch -c <branch> main`**  
3. **`git checkout 'stash@{0}' -- <exact child §4 list>`** — **no** **`stash pop`**.  
4. **Reconcile** per file (**Keep** / **Partial** / **Drop**); run child **§5** proof ladder; fill child **§8**; advance **`latest_pointer.json`** only on **PASSED** **Quick**.

---

## 8. Next actions

| Step | Owner | Action |
|------|-------|--------|
| 1 | Engineering | **Done** **2026-03-28** — **§1** engineering preflight dated; **§4** verified on **`37fb89e6`**. |
| 2 | Product | **Done** **2026-03-28** — **§5 Option A** (**C3a**). |
| 3 | Overseer / engineer | **Done** **2026-03-28** — **[`STASH0_T1_C3A_EXECUTION_ROW.md`](STASH0_T1_C3A_EXECUTION_ROW.md)** (**`GOV-STASH0-T1-C3A-EXEC-01`**). **Next:** date **implementation §1** on **that** row before extract. |
| 4 | — | **No** `backend/**` / **`app/core/**`** code under **`GOV-STASH0-T1-C3-PREFLIGHT-01`**. |

---

## 9. Preflight success criteria (this document only)

- [x] **R1A** closure cited; **`main`** tip + **merge-base** + stash message recorded (**§3**).
- [x] All **§4.1** paths tabled with **fresh** **`git diff --shortstat`** (**2026-03-28**).
- [x] **Subclusters** **C3a**–**C3d** documented; **T3** bleed called out for **C3c**.
- [x] **§5** forces **one** primary recommendation + backup + pause; **T3** firewall explicit.
- [x] **No** stash extract and **no** implementation under **`GOV-STASH0-T1-C3-PREFLIGHT-01`**.

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-28 | **Created** — **`GOV-STASH0-T1-C3-PREFLIGHT-01`**: post–**R1A** **T1-C3** re-baseline; **§1** product slice **Pending**; child execution row **deferred**. |
| 2026-03-28 | **§1 slice choice** — **Option A** (**C3a**); child execution row **[`GOV-STASH0-T1-C3A-EXEC-01`](STASH0_T1_C3A_EXECUTION_ROW.md)** drafted; **implementation** sign-off **on child row** still **Pending**. |
