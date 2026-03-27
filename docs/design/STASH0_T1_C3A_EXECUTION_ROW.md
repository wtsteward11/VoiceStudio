# stash@{0} — T1-C3a — rate limiting (narrow) — execution row

**Row ID:** **GOV-STASH0-T1-C3A-EXEC-01**  
**Purpose:** Land **only** **`backend/api/rate_limiting.py`** and **`backend/api/rate_limiting_enhanced.py`** from **`stash@{0}`** per **[`GOV-STASH0-T1-C3-PREFLIGHT-01`](STASH0_T1_C3_PREFLIGHT_EXECUTION_ROW.md)** **§5 Option A** (**C3a**) — **without** **`auth_middleware.py`**, **without** **`models_additional.py`**, **without** **T3** contracts, **without** bulk **`stash pop`**.  
**Source stash:** *WIP: pre-Pass06-20260326 unclassified local and untracked*.  
**Date (row drafted):** 2026-03-28  
**Status:** **Closed (implementation + proof)** — **2026-03-28**  
**§1 authorization:** **Engineering preflight** — **§3** complete **2026-03-28**. **Product / engineering (implementation)** — **Implementation authorized** **2026-03-28**; **§8** closure **2026-03-28** (merge **`ab52c3de`**).

**Related:** [STASH0_T1_C3_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_C3_PREFLIGHT_EXECUTION_ROW.md) (**slice choice** **Option A**); [STASH0_T1_R1A_EXECUTION_ROW.md](STASH0_T1_R1A_EXECUTION_ROW.md) (**R1A** — **closed**); [`.cursor/STATE.md`](../../.cursor/STATE.md).

---

## 1. Sign-off

| Role | Decision | Date |
|------|----------|------|
| **Engineering (preflight)** | **Preflight complete** — **§3** filled from **`main`** **`843e86a251396fd47585da378294ae4fb9955b12`** vs **`stash@{0}`**; **`git merge-base main 'stash@{0}'`** **`a7a45f4cc2e8e81671eefffe885df3a86227b10a`**; **§4** both paths **non-empty** (combined **19 insertions, 12 deletions**); **`middleware_setup.py`** **empty** vs **`stash@{0}`** (no stash checkout needed for wiring). Re-run **§3** before extract if **`main`** or **`stash@{0}`** moves. | **2026-03-28** |
| **Product / engineering (implementation)** | **Implementation authorized** — Binding **go** to selective **`git checkout 'stash@{0}' --`** for **§4** only (**§7**); reconcile **`backend/**` under that lock; **§6 OUT** accepted; **§5** proof + **`verify.ps1 -Quick`** mandatory for closure. **Pre-extract** **2026-03-28**: **`stash@{0}`** message matches STATE; re-run **`git rev-parse HEAD`** + **`git diff --shortstat main 'stash@{0}' --`** each **§4** path before checkout (non-empty required). | **2026-03-28** |

---

## 2. Objective (single sentence)

Reconcile and land **only** the **rate-limiting core + enhanced middleware** deltas in **§4** from **`stash@{0}`** onto **`main`**, preserving **ADR-032** middleware behavior and limiter headers—**without** auth middleware changes (**C3b**), **without** **`models_additional`** (**C3c** / **T3** bleed), and **without** OpenAPI or shared-schema churn (**T3**).

**Narrowing rule:** If **§3** shows a **§4** path **empty** vs **`stash@{0}`**, **stop** and update **§3** + **§1** re-acknowledgment before extract.

---

## 3. Preflight — `git diff main 'stash@{0}' -- <path>`

**Recorded** **`main`** @ **`843e86a251396fd47585da378294ae4fb9955b12`** (**re-verify** with **`git rev-parse HEAD`** before relying on hashes). **`git merge-base main 'stash@{0}'`** = **`a7a45f4cc2e8e81671eefffe885df3a86227b10a`**. **Governance-only ledger sync** **2026-03-28**: **STATE** / **CANONICAL_REGISTRY** **`run_verification`** **`timestamp_short`** aligned to **`.buildlogs/verification/last_run.json`** (**`20260327-170027`**) — **no** **Quick** / pointer change.

**Sign-off readiness gates**

| Gate | Must be |
|------|---------|
| **Single slice?** | **Yes** — **C3a** only (**two** files); **not** **C3b** / **C3c** / **T1-C4** tests. |
| **Obsolete vs `main`?** | Each **§4** row **non-empty** vs **`stash@{0}`** (or **Drop** with **§1** re-ack). |
| **Wiring?** | **`middleware_setup.py`** consumes these modules; **no** **`stash@{0}`** delta on **`middleware_setup.py`** at preflight (**empty** diff) — land **§4** only unless a **new** row expands. |

| Path | Stat (`git diff --shortstat`) | Decision |
|------|-------------------------------|----------|
| `backend/api/rate_limiting.py` | 1 file changed, 15 insertions(+) | **Keep** — core limiter / bucket logic. |
| `backend/api/rate_limiting_enhanced.py` | 1 file changed, 4 insertions(+), 12 deletions(-) | **Keep** — enhanced **`RateLimitMiddleware`**; **Partial** merge if **`main`** intent conflicts. |

---

## 4. File lock (IN)

**Exactly** these **two** paths. **No** additions without a **new** row ID.

| Path |
|------|
| `backend/api/rate_limiting.py` |
| `backend/api/rate_limiting_enhanced.py` |

---

## 5. Proof commands (post-implementation)

Run **after** reconciled diff is on the branch that will merge to **`main`** (or PR verification tree).

| # | Command | Expected |
|---|---------|----------|
| 1 | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0** errors |
| 2 | `python -m pytest tests/unit/backend/api/test_rate_limiting.py tests/unit/backend/api/test_rate_limiting_enhanced.py -q --tb=line` | **PASS** — on **`main`** **2026-03-28**: **4** passed, **1** skipped (record **exact** counts in **§8** at closure if they differ). **`test_rate_limiting_enhanced`** may **skip** at collection when optional deps missing — document in **§8** if behavior changes. |
| 3 | `python scripts/run_verification.py` | **ALL PASS**; if updating **STATE** / registry proof lines, copy **`timestamp_short`** **verbatim** from `.buildlogs/verification/last_run.json` |
| 4 | `.\scripts\verify.ps1 -Quick` | **PASS** — **mandatory** for **closure**; advances **`artifacts/verify/latest_pointer.json`** only on **PASSED** **Quick** for the **implementation** commit tree |

### 5.1 Coverage honesty

- **Direct:** unit tests above exercise **`rate_limiting`** / **`rate_limiting_enhanced`** imports and key symbols.
- **Indirect:** broader security / integration suites (e.g. **`tests/security/test_route_security_matrix.py`**) are **not** in the frozen **§5** subset — **Quick** + **§5** **#2** are the **primary** regression signal for this row. Expanding **§5** requires **§4** expansion + **§1** re-sign.

---

## 6. OUT (strict)

| # | OUT |
|---|-----|
| 1 | **`backend/api/middleware/auth_middleware.py`**, **`tests/unit/backend/api/middleware/test_auth_middleware.py`** — **C3b**; **new** row. |
| 2 | **`backend/api/models_additional.py`**, **`tests/unit/backend/api/test_models_additional.py`** — **C3c** / **T3**-adjacent; **new** row. |
| 3 | **`docs/api/openapi.json`**, **`shared/schemas/**`**, **`tests/contract/**`** — **T3**; joint row only. |
| 4 | **`backend/api/v3/models.py`**, **`backend/api/v3/projects.py`**, **R1A** ancillary paths, **`app/core/**`**, **`src/VoiceStudio.App/**`**. |
| 5 | **T1-C4** stash test checkouts — **no** **`stash@{0}`** checkout of **`test_synthesis_policy.py`** / **`test_golden_loop_smoke.py`** unless **§4** expands under **new** **§1**. |
| 6 | **`backend/mcp_bridge/README.md`**, **`backend/plugins/sandbox/resource_monitor.py`**, **T5** `LibraryView.xaml.cs`, **T4** sweeps except minimal **STATE**/registry **after** proof. |
| 7 | Mining **`stash@{0}`** outside **§4**; **`git stash pop`**. |
| 8 | Reopening **W8 / W7 / Product Trust / A4 / Pass 06** under this banner. |

---

## 7. Execution sequence (extraction — **after** implementation **§1** dated)

1. **`git stash list`** — confirm **`stash@{0}`** matches [`.cursor/STATE.md`](../../.cursor/STATE.md).  
2. **`git switch -c stash0-t1-c3a-01 main`** (name per team convention).  
3. **`git checkout 'stash@{0}' -- backend/api/rate_limiting.py backend/api/rate_limiting_enhanced.py`** (PowerShell-quoted **`stash@{0}`**).

**Do not** use **`git stash pop`**.

**Reconcile** each file: **Keep** / **Partial** / **Drop** (see **§2**).

---

## 8. Closure record (fill after proof only)

| Field | Value |
|-------|--------|
| **Quick** | **`artifacts/verify/20260327_175838`** (**PASS**) |
| **`latest_pointer.json` `commit_hash`** | **`ab52c3dee676f13b921e6e6b9cd31eb96b1f9f1a`** |
| **`run_verification.py` `timestamp_short`** | **`20260327-180527`** (verbatim **`.buildlogs/verification/last_run.json`** — includes post-closure doc commits + final **`python scripts/run_verification.py`**) |
| **Notes** | Branch **`stash0-t1-c3a-01`**; selective **`git checkout 'stash@{0}' --`** **§4** only (**`rate_limiting.py`**, **`rate_limiting_enhanced.py`**); **Keep** stash semantics (localhost exempt when **`rate_limit_localhost_exempt`**, **`middleware_setup`** unchanged vs stash); **fast-forward** merged to **`main`** **`ab52c3de`**; **§5** **`pytest`** **4** passed, **1** skipped (**`test_rate_limiting_enhanced`** collection skip unchanged); **`dotnet build`** **0** errors; **`stash@{0}`** **unchanged**. |

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-28 | **Created** — **`GOV-STASH0-T1-C3A-EXEC-01`**; **§4** **two**-path **C3a** lock; **§1** implementation **Pending**. |
| 2026-03-28 | **§1** **implementation** **Go**; **§3** note — governance **`run_verification`** stamp sync (**`20260327-170027`**). |
| 2026-03-28 | **Closed** — **`stash0-t1-c3a-01`** → **`main`** **`ab52c3de`**; **Quick** **`20260327_175838`**; **`run_verification`** **`20260327-180527`** (final ledger sync); **§5** pytest **4** passed **1** skipped. |
