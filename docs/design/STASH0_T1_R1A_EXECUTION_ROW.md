# stash@{0} — T1-R1A — narrow T1-C2 (ancillary routes + registry + helpers) — execution row

**Row ID:** **GOV-STASH0-T1-R1A-EXEC-01**  
**Purpose:** Land **only** the **narrow T1-C2** remainder from **`stash@{0}`** per **[`GOV-STASH0-T1-R1-PREFLIGHT-01`](STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md)** **§5 Option A** — ancillary HTTP routes, **`route_registry`**, and helper services **listed in §4** — **without** **`v3/models.py`** / **`v3/projects.py`**, **without** **T1-C3** cross-cutting, **without** **T3** contracts, and **without** bulk **`stash pop`**.  
**Source stash:** *WIP: pre-Pass06-20260326 unclassified local and untracked*.  
**Date (row drafted):** 2026-03-27  
**§1 authorization:** **Engineering preflight** — **§3** complete **2026-03-27**. **Product / engineering (implementation)** — **Authorized** **2026-03-28** (selective extract **§7**, **§6 OUT** binding, **§5** + **Quick** closure).

**Related:** [STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_R1_PREFLIGHT_EXECUTION_ROW.md) (**slice choice** **Option A**); [STASH0_T1_S1_EXECUTION_ROW.md](STASH0_T1_S1_EXECUTION_ROW.md) (**T1-S1** — **closed**); [`.cursor/STATE.md`](../../.cursor/STATE.md).

---

## 1. Sign-off

| Role | Decision | Date |
|------|----------|------|
| **Engineering (preflight)** | **Preflight complete** — **§3** filled from **`main`** vs **`stash@{0}`**; **re-verified** **`main`** **`8d4a2cc60ecafa6f23ba5fa7fca9a1f40ca5ffc7`** (**2026-03-27**); all **§4** paths **non-empty** diff (stats unchanged vs R1 table); **§4** = **ten** paths (minimal **Option A**). Re-run **§3** before extract if **`main`** or **`stash@{0}`** moves. | **2026-03-27** |
| **Product / engineering (implementation)** | **Implementation authorized** — Binding **go** to selective **`git checkout 'stash@{0}' --`** for **§4** only (**§7**); edit **`backend/**` under that lock; **§6 OUT** accepted; **§5** proof (**§5.1** proof-only tests, no stash checkout of pytest paths) + **`verify.ps1 -Quick`** mandatory for closure. **Pre-extract** **2026-03-28**: **`stash@{0}`** message matches STATE; **`main`** **`8d4a2cc60ecafa6f23ba5fa7fca9a1f40ca5ffc7`**; all **ten** **§4** paths **non-empty** vs **`stash@{0}`**. | **2026-03-28** |

---

## 2. Objective (single sentence)

Reconcile and land **only** the **ancillary route + route registry + helper service** deltas in **§4** from **`stash@{0}`** onto **`main`**, preserving a **single** coherent HTTP surface narrative—**without** pulling **`v3/models`** / **`v3/projects`**, **without** auth/rate-limit/model-adjunct refactors (**T1-C3**), and **without** OpenAPI or shared-schema churn (**T3**).

**Narrowing rule:** If **§3** shows a **§4** path **empty** vs **`main`** or **not** justified by **§2**, **drop** it from **§4** (requires **§1** re-acknowledgment if the lock list changes).

---

## 3. Preflight — `git diff main 'stash@{0}' -- <path>`

**Recorded** **`main`** @ **`8d4a2cc60ecafa6f23ba5fa7fca9a1f40ca5ffc7`** (**re-verify** with **`git rev-parse HEAD`** before relying on hashes). **`git merge-base main 'stash@{0}'`** = **`a7a45f4cc2e8e81671eefffe885df3a86227b10a`**. Re-run **§3** before extract if tips move.

**Sign-off readiness gates** (mirror **T1-S1** / **T2** discipline):

| Gate | Must be |
|------|---------|
| **Single slice?** | **Yes** — **T1-C2** ancillary routes + **`route_registry`** + listed helpers only; **not** T1-C3 / T1-C4 / **v3/models+projects**. |
| **Obsolete vs `main`?** | Each **§4** row **non-empty** vs **`stash@{0}`** (or **Drop** with **§1** re-ack). |
| **One objective?** | **Yes** — **§2** HTTP ancillary coherence without T3 coupling. |
| **OUT blocks bleed?** | **Yes** — **§6** binding (**v3/models**, **v3/projects**, **T1-C3**, **T3**, **app/core**). |

| Path | Stat (`git diff --shortstat`) | Decision |
|------|-------------------------------|----------|
| `backend/api/route_registry.py` | 1 file changed, 2 insertions(+) | **Keep** — registry wiring for route discovery. |
| `backend/api/routes/help.py` | 1 file changed, 4 insertions(+), 4 deletions(-) | **Keep** — ancillary help route. |
| `backend/api/routes/library.py` | 1 file changed, 9 insertions(+) | **Keep** — library route surface. |
| `backend/api/routes/profiles.py` | 1 file changed, 10 insertions(+), 2 deletions(-) | **Keep** — profiles route surface. |
| `backend/api/routes/realtime_settings.py` | 1 file changed, 4 insertions(+), 3 deletions(-) | **Keep** — realtime settings route. |
| `backend/api/routes/search.py` | 1 file changed, 1 insertion(+), 1 deletion(-) | **Keep** — search route. |
| `backend/api/routes/shortcuts.py` | 1 file changed, 1 insertion(+), 1 deletion(-) | **Keep** — shortcuts route. |
| `backend/services/audio_path_resolver.py` | 1 file changed, 7 insertions(+) | **Keep** — helper used by ancillary flows; reconcile vs **`main`** callers (**Partial** allowed). |
| `backend/services/audit_logger.py` | 1 file changed, 19 insertions(+), 7 deletions(-) | **Keep** — audit logging helper; **Partial** merge if deletions risky. |
| `backend/services/script_store.py` | 1 file changed, 6 insertions(+), 4 deletions(-) | **Keep** — script store service seam. |

---

## 4. File lock (IN)

**Exactly** these **ten** paths. **No** additions without a **new** row ID.

| Path |
|------|
| `backend/api/route_registry.py` |
| `backend/api/routes/help.py` |
| `backend/api/routes/library.py` |
| `backend/api/routes/profiles.py` |
| `backend/api/routes/realtime_settings.py` |
| `backend/api/routes/search.py` |
| `backend/api/routes/shortcuts.py` |
| `backend/services/audio_path_resolver.py` |
| `backend/services/audit_logger.py` |
| `backend/services/script_store.py` |

---

## 5. Proof commands (post-implementation)

Run **after** reconciled diff is on the branch that will merge to **`main`** (or PR verification tree).

| # | Command | Expected |
|---|---------|----------|
| 1 | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0** errors |
| 2 | `python -m pytest tests/unit/backend/api/routes/test_help.py tests/unit/backend/api/routes/test_library.py tests/unit/backend/api/routes/test_profiles.py tests/unit/backend/api/routes/test_realtime_settings.py tests/unit/backend/api/routes/test_search.py tests/unit/backend/api/routes/test_shortcuts.py tests/unit/backend/services/test_audit_logger.py tests/ci/test_route_registry_parity.py tests/unit/test_synthesis_policy.py tests/ci/test_golden_loop_smoke.py -q --tb=line` | **PASS** — on **`main`** **2026-03-27**: **74** passed, **2** skipped (record **exact** counts in **§8** at closure if they differ). **Indirect:** **`script_store`** exercised via **`search`** (`get_scripts_for_search`); **`audio_path_resolver`** has **no** dedicated test file on **`main`** — reliance on **Quick** + absence of new **`pytest`** failures. |
| 3 | `python scripts/run_verification.py` | **ALL PASS**; if updating **STATE** / registry proof lines, copy **`timestamp_short`** **verbatim** from `.buildlogs/verification/last_run.json` |
| 4 | `.\scripts\verify.ps1 -Quick` | **PASS** — **mandatory** for **closure**; advances **`artifacts/verify/latest_pointer.json`** only on **PASSED** **Quick** for the **implementation** commit tree |

### 5.1 Proof-only tests (**Option A**)

- **`tests/unit/test_synthesis_policy.py`** and **`tests/ci/test_golden_loop_smoke.py`** are **not** in **§4** and are **not** checked out from **`stash@{0}`** for **R1A**.
- They **are** **required** **§5** regression smoke on the **implementation** tree (same discipline as **T1-S1**).
- **§6** forbids **stash checkout** of those test files under **R1A** unless **§4** expands under **new** **§1**.

### 5.2 Coverage honesty (**helpers**)

- **`backend/services/script_store.py`** and **`backend/services/audio_path_resolver.py`** have **no** `tests/**` files named for them on **`main`** today. **§5** #2 still **must** **PASS**; closure **§8** should note **indirect** vs **direct** coverage. Adding **`test_script_store.py`** / **`test_audio_path_resolver.py`** is **OUT** of **R1A** unless **§4** expands under **new** **§1**.

---

## 6. OUT (strict)

| # | OUT |
|---|-----|
| 1 | **`backend/api/v3/models.py`**, **`backend/api/v3/projects.py`** — **T3**-coupled; **Option B** only under **new** preflight + sign-off. |
| 2 | **T1-C3:** `auth_middleware.py`, `rate_limiting.py`, `rate_limiting_enhanced.py`, `models_additional.py`, related tests — unless **new** row. |
| 3 | **T1-C4 stash test edits** — no **`stash@{0}`** checkout of `test_synthesis_policy.py` / `test_golden_loop_smoke.py`; **run** only as **§5** proof. |
| 4 | **`docs/api/openapi.json`**, **`shared/schemas/**`**, **`tests/contract/**`** — **T3**; joint row only. |
| 5 | **`app/core/**`** (no engine protocol changes in **R1A**). |
| 6 | **`src/VoiceStudio.App/**`**, **T5** `LibraryView.xaml.cs`, **T4** sweeps except minimal **STATE**/registry **after** proof. |
| 7 | **`backend/mcp_bridge/README.md`**, **`backend/plugins/sandbox/resource_monitor.py`** — **OUT** (R1 **§4.2** default). |
| 8 | Mining **`stash@{0}`** outside **§4**; **`git stash pop`**. |
| 9 | Reopening **W8 / W7 / Product Trust / A4 / Pass 06** under this banner. |

---

## 7. Execution sequence (extraction — **after** implementation **§1** dated)

1. **`git stash list`** — confirm **`stash@{0}`** matches [`.cursor/STATE.md`](../../.cursor/STATE.md).
2. **`git switch -c <branch-name> main`** (e.g. `stash0-t1-r1a-01`).
3. **`git checkout 'stash@{0}' --`** the **ten** paths from **§4** (PowerShell-quoted stash ref).

**Do not** use **`git stash pop`**.

**Reconcile** each file: **Keep** / **Partial** / **Drop** (see **§3**).

---

## 8. Closure record (fill after proof only)

| Field | Value |
|-------|--------|
| **Quick** | *(TBD — `verify.ps1 -Quick` artifact dir on implementation tree)* |
| **`latest_pointer.json` `commit_hash`** | *(TBD)* |
| **`run_verification.py` `timestamp_short`** | *(TBD — verbatim from `.buildlogs` after the run that justifies **STATE**/registry edits)* |
| **Notes** | *(TBD)* |

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-27 | **Created** — **`GOV-STASH0-T1-R1A-EXEC-01`**; **§4** ten-path **Option A** lock; **§1** implementation **Pending**. |
| 2026-03-27 | **Governance sync** — [`.cursor/STATE.md`](../../.cursor/STATE.md) **ACTIVE WINDOW** + [`CANONICAL_REGISTRY.md`](../governance/CANONICAL_REGISTRY.md) row; **§5** pytest subset on **`main`** (**74** passed, **2** skipped) — counts **re-verified** same day. |
| 2026-03-27 | **Plan completion** — **§3** **`main`** tip refreshed (**`8d4a2cc6`**); **sign-off readiness gates** table; **§5.2** coverage honesty for **`script_store`** / **`audio_path_resolver`**; **§5** expected counts aligned to observed **`pytest`** run. |
