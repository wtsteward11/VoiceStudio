# stash@{0} — T1-C3c — models_additional (T3-adjacent, narrow) — execution row

**Row ID:** **GOV-STASH0-T1-C3C-EXEC-01**  
**Purpose:** Land **only** **`backend/api/models_additional.py`** and **`tests/unit/backend/api/test_models_additional.py`** from **`stash@{0}`** per **[`GOV-STASH0-T1-C3-PREFLIGHT-01`](STASH0_T1_C3_PREFLIGHT_EXECUTION_ROW.md)** **§4.1** / **§5 Option C** (**C3c**) — **without** **`docs/api/openapi.json`**, **`shared/schemas/**`**, **`tests/contract/**`**, **without** re-checking **C3a** / **C3b** paths from stash, **without** bulk **`stash pop`**. **T3-adjacent:** reconcile must reject hunks that **require** contract/schema/OpenAPI edits under this row; any such need → **Pause** / **new T3 row**.  
**Source stash:** *WIP: pre-Pass06-20260326 unclassified local and untracked*.  
**Date (row drafted):** 2026-03-28  
**Status:** **Open** — **§1** **implementation** **Go** **2026-03-28** — extract + proof in flight.  
**§1 authorization:** **Engineering (preflight)** — **§3** complete **2026-03-28**; **§3 re-verified** **2026-03-28** on **`main`** **`9f7e19e9`** (shortstats unchanged vs **`stash@{0}`**). **Product / engineering (implementation)** — **Go** **2026-03-28** — selective **`stash@{0}`** checkout **§4** only; **§6 OUT**; **§5** + **Quick** for closure.

**Related:** [STASH0_T1_C3_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_C3_PREFLIGHT_EXECUTION_ROW.md) (**§5 Option C** / **C3c**); [STASH0_T1_C3A_EXECUTION_ROW.md](STASH0_T1_C3A_EXECUTION_ROW.md) (**C3a** — **closed**); [STASH0_T1_C3B_EXECUTION_ROW.md](STASH0_T1_C3B_EXECUTION_ROW.md) (**C3b** — **closed**); [`.cursor/STATE.md`](../../.cursor/STATE.md).

---

## 1. Sign-off

| Role | Decision | Date |
|------|----------|------|
| **Engineering (preflight)** | **Preflight complete** — **§3** refreshed **2026-03-28** on **`main`** **`9f7e19e93ddf115ebc2569feb2af9a9045414363`** vs **`stash@{0}`**; **`git merge-base main 'stash@{0}'`** **`a7a45f4cc2e8e81671eefffe885df3a86227b10a`**; **§4** both paths **non-empty**; stash top matches STATE. Re-run **§3** if **`main`** or **`stash@{0}`** moves before checkout. | **2026-03-28** |
| **Product / engineering (implementation)** | **Implementation authorized** — Binding **go** to selective **`git checkout 'stash@{0}' --`** for **§4** only (**§7**); reconcile under lock; **§6 OUT** accepted; **§5** proof + **`verify.ps1 -Quick`** mandatory for closure. **Pre-extract** **2026-03-28**: **`stash@{0}`** message matches STATE; **`main`** **`9f7e19e9`**; **§4** shortstats **unchanged** vs prior draft. | **2026-03-28** |

---

## 2. Objective (single sentence)

Reconcile and land **only** the **Pydantic models adjunct + companion unit tests** in **§4** from **`stash@{0}`** onto **`main`**, preserving import surfaces consumed by routes/services—**without** OpenAPI regeneration, **without** **`shared/schemas/**`** or **`tests/contract/**`** edits, **without** widening into **v3** route files or **app/core**, and **without** re-mining **C3a**/**C3b** stash paths.

**Narrowing rule:** If **§3** shows a **§4** path **empty** vs **`stash@{0}`**, **stop** and update **§3** + **§1** re-acknowledgment before extract.

**Coupling note:** **`models_additional.py`** is **T3-adjacent** per preflight **§4.2**; if stash introduces or assumes schema contract changes that belong in **T3**, **Partial** strip those hunks or **Drop** the row until a **T3**-signed lane exists. **Do not** “fix” failing tests by pulling **`openapi.json`** / **`shared/schemas`** into this row.

---

## 3. Preflight — `git diff main 'stash@{0}' -- <path>`

**Recorded** **`main`** @ **`9f7e19e93ddf115ebc2569feb2af9a9045414363`** (**§3 refresh** **2026-03-28** — **re-verify** with **`git rev-parse HEAD`** before checkout if **`main`** moves again). **`git merge-base main 'stash@{0}'`** = **`a7a45f4cc2e8e81671eefffe885df3a86227b10a`**.

**Sign-off readiness gates**

| Gate | Must be |
|------|---------|
| **Single slice?** | **Yes** — **C3c** only (**two** files); **not** **C3a**/**C3b** stash re-checkout; **not** **T1-C4** tests unless **§4** expands under **new** **§1**. |
| **Obsolete vs `main`?** | Each **§4** row **non-empty** vs **`stash@{0}`** (or **Drop** with **§1** re-ack). |
| **C3a / C3b on `main`?** | **`rate_limiting*.py`**, **`auth_middleware.py`** (+ C3b tests) — **on `main`** via **[`GOV-STASH0-T1-C3A-EXEC-01`](STASH0_T1_C3A_EXECUTION_ROW.md)** / **[`GOV-STASH0-T1-C3B-EXEC-01`](STASH0_T1_C3B_EXECUTION_ROW.md)**; **do not** **`stash@{0}`** checkout those paths in this row. |

| Path | Stat (`git diff --shortstat`) | Decision |
|------|-------------------------------|----------|
| `backend/api/models_additional.py` | 1 file changed, 45 insertions(+), 15 deletions(-) | **Keep** — models adjunct; **Partial** if T3-coupled hunks must be stripped. |
| `tests/unit/backend/api/test_models_additional.py` | 1 file changed, 49 insertions(+) | **Keep** — companion tests; **Partial** if **`main`** test intent conflicts. |

---

## 4. File lock (IN)

**Exactly** these **two** paths. **No** additions without a **new** row ID.

| Path |
|------|
| `backend/api/models_additional.py` |
| `tests/unit/backend/api/test_models_additional.py` |

---

## 5. Proof commands (post-implementation)

Run **after** reconciled diff is on the branch that will merge to **`main`** (or PR verification tree).

| # | Command | Expected |
|---|---------|----------|
| 1 | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0** errors |
| 2 | `python -m pytest tests/unit/backend/api/test_models_additional.py -q --tb=line` | **PASS** — record **exact** passed/skipped/failed in **§8** at closure |
| 3 | `python scripts/run_verification.py` | **ALL PASS**; if updating **STATE** / registry proof lines, copy **`timestamp_short`** **verbatim** from `.buildlogs/verification/last_run.json` |
| 4 | `.\scripts\verify.ps1 -Quick` | **PASS** — **mandatory** for **closure**; advances **`artifacts/verify/latest_pointer.json`** only on **PASSED** **Quick** for the **implementation** commit tree |

### 5.1 Coverage honesty

- **Direct:** frozen **§5** **#2** exercises **`models_additional`** via **`test_models_additional`**.
- **Indirect:** full backend matrix, contract suites, and **T3** paths are **not** in the frozen **§5** subset unless **§4** expands + **§1** re-sign. **Quick** + **§5** **#2** are the **primary** regression signal for this row.

---

## 6. OUT (strict) — T3 firewall

| # | OUT |
|---|-----|
| 1 | **`docs/api/openapi.json`**, **`shared/schemas/**`**, **`tests/contract/**`** — **T3**; **not** under **C3c** unless a **separate signed T3 row** authorizes. |
| 2 | **`backend/api/rate_limiting.py`**, **`backend/api/rate_limiting_enhanced.py`** — **C3a**; **on `main`**; **no** stash checkout. |
| 3 | **`backend/api/middleware/auth_middleware.py`**, **`tests/unit/backend/api/middleware/test_auth_middleware.py`** — **C3b**; **on `main`**; **no** stash checkout. |
| 4 | **`backend/api/v3/models.py`**, **`backend/api/v3/projects.py`**, **R1A** ancillary paths, **`app/core/**`**, **`src/VoiceStudio.App/**`**. |
| 5 | **T1-C4** stash test checkouts — **no** **`stash@{0}`** checkout of **`test_synthesis_policy.py`** / **`test_golden_loop_smoke.py`** unless **§4** expands under **new** **§1**. |
| 6 | **`backend/mcp_bridge/README.md`**, **`backend/plugins/sandbox/resource_monitor.py`**, **T5** `LibraryView.xaml.cs`, **T4** sweeps except minimal **STATE**/registry **after** proof. |
| 7 | Mining **`stash@{0}`** outside **§4**; **`git stash pop`**. |
| 8 | Reopening **W8 / W7 / Product Trust / A4 / Pass 06** under this banner. |

---

## 7. Execution sequence (extraction — **after** implementation **§1** dated)

1. **`git stash list`** — confirm **`stash@{0}`** matches [`.cursor/STATE.md`](../../.cursor/STATE.md).  
2. **`git switch -c stash0-t1-c3c-01 main`** (name aligned with **`stash0-t1-c3a-01`** / **`stash0-t1-c3b-01`**).  
3. **`git checkout 'stash@{0}' -- backend/api/models_additional.py tests/unit/backend/api/test_models_additional.py`** (PowerShell-quoted **`stash@{0}`**).

**Do not** use **`git stash pop`**.

**Reconcile** each file: **Keep** / **Partial** / **Drop** (see **§2**).

---

## 8. Closure record (fill after proof only)

| Field | Value |
|-------|--------|
| **Quick** | *(pending)* |
| **`latest_pointer.json` `commit_hash`** | *(pending)* |
| **`run_verification.py` `timestamp_short`** | *(verbatim `.buildlogs/verification/last_run.json`)* |
| **Notes** | *(branch, merge, reconcile, pytest counts, T3 firewall adherence)* |

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-28 | **Created** — **`GOV-STASH0-T1-C3C-EXEC-01`**; **§4** two-path **C3c** lock (**`models_additional`** + companion test); **§1** implementation **Pending**. |
| 2026-03-28 | **§3** refreshed — **`main`** **`9f7e19e9`**; shortstats unchanged (**45+/15−**, **49+** test). **§1** **implementation** **Go**. |
