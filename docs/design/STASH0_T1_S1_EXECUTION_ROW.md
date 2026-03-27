# stash@{0} — T1-S1 Synthesis vertical — execution row

**Row ID:** **GOV-STASH0-T1-S1-01**  
**Purpose:** Execution-grade freeze for **T1-S1** — reconcile the **synthesis vertical** (**T1-C1** from [STASH0_T1_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_PREFLIGHT_EXECUTION_ROW.md)) from **`stash@{0}`** against current **`main`**, with **no** ancillary route sweep, **no** contract/OpenAPI/schema work, and **no** bulk stash pop.  
**Source stash:** *WIP: pre-Pass06-20260326 unclassified local and untracked*. Derived from preflight **§5** (**T1-S1** = **T1-C1**).  
**Date (row drafted):** 2026-03-24  
**§1 authorization:** **Engineering preflight** recorded **2026-03-24** (§3 diff table filled; **Option A** proof policy). **Product / engineering** — **implementation go** dated **2026-03-26** (selective extract **§4** / **§7** only; **§6 OUT** binding; **§5** + **Quick** for closure).

**Related:** [STASH0_T1_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_PREFLIGHT_EXECUTION_ROW.md) (**GOV-STASH0-T1-PREFLIGHT-01**); [STASH0_T2_VERIFY_CI_EXECUTION_ROW.md](STASH0_T2_VERIFY_CI_EXECUTION_ROW.md) (mirrors **OUT** / proof discipline); [`.cursor/STATE.md`](../../.cursor/STATE.md) **T1/T3** rows.

---

## 1. Sign-off

| Role | Decision | Date |
|------|----------|------|
| **Engineering (preflight)** | **Preflight finished** — **§3** filled from **`main`** vs **`stash@{0}`** (`git diff --stat` per path **2026-03-24**). All **eight** paths show **material** delta (no empty diffs); **§4** unchanged (no drops). **§5.1 Option A** (proof-only pytest; test files **not** in **§4**). **No** `openapi`/schema/contract edits under this row. | **2026-03-24** |
| **Product / engineering (implementation)** | **Implementation authorized** — Binding **go** to selective **`git checkout 'stash@{0}' --`** for **§4** only (**§7**); edit **`backend/**` / `app/core/**` under that lock; **§6 OUT** accepted; **§5** proof with **§5.1 Option A** (no stash checkout of pytest paths); **Quick** mandatory for closure. **§3** preflight not re-run: **`main`** / **`stash@{0}`** unchanged since **§3** fill (verify before any future extract if tips move). | **2026-03-26** |

---

## 2. Objective (single sentence)

Reconcile and land **only** the **synthesis vertical** (legacy voice routes, `SynthesisService`, v3 synthesis/engines/voices, engine base protocol, and backend settings touchpoints **as required by that vertical**) from **`stash@{0}`** onto **`main`**, keeping **one** coherent backend narrative—**without** sweeping ancillary HTTP routes, **without** auth/rate-limit/model-adjunct refactors, and **without** OpenAPI or shared-schema churn.

**Narrowing rule:** If **§3** preflight shows any locked path **empty** vs **`main`** or **not** justified by the synthesis narrative, **drop** it from this row (requires **§1** re-acknowledgment if the lock list changes).

---

## 3. Preflight — `git diff main 'stash@{0}' -- <path>`

**Before dating §1:** On repo tip, for **each** path in **§4**, run (PowerShell — quote the stash ref):

`git diff main 'stash@{0}' -- <path>`

Record `git diff --stat` (or confirm **empty**) and a one-line **Keep / Partial / Drop** decision. **Do not** assume the stashed version is correct.

| Path | Stat (`git diff main 'stash@{0}' --stat`) | Decision |
|------|-------------------------------------------|----------|
| `backend/api/routes/voice/synthesis.py` | **1 file, +510 −10** (~520 lines touched) | **Keep** — primary synthesis route surface; core **§2** narrative. |
| `backend/api/routes/voice/_shared.py` | **1 file, 22 deletions** (stash removes lines vs `main`) | **Keep** — reconcile deletion-heavy delta so voice routes stay coherent (may be **Partial** merge during implementation). |
| `backend/services/synthesis_service.py` | **1 file, +41 −31** | **Keep** — service layer for synthesis vertical. |
| `backend/api/v3/synthesis.py` | **1 file, +7 −5** | **Keep** — v3 synthesis API alignment. |
| `backend/api/v3/engines.py` | **1 file, +4 −3** | **Keep** — engine listing/metadata for synthesis path. |
| `backend/api/v3/voices.py` | **1 file, +4 −3** | **Keep** — voice listing for synthesis path. |
| `app/core/engines/base.py` | **1 file, +1** | **Keep** — engine protocol seam (small but synthesis-coupled). |
| `backend/core/settings.py` | **1 file, +19 −5** | **Keep** — settings touchpoints for synthesis/engine config. |

**Narrowing note (Phase B):** **No** paths dropped — every file has a **non-empty** diff vs **`main`**. **§4** remains **eight** paths.

**Sign-off readiness gates** (same style as **T2**):

| Gate | Must be |
|------|---------|
| **Single slice?** | **Yes** — only **synthesis vertical + engine/settings seam**; not T1-C2/T1-C3. |
| **Obsolete vs `main`?** | Each row in the table **resolved** (no “unknown”). |
| **One objective?** | **Yes** — synthesis path coherence only. |
| **OUT blocks T3/T4 bleed?** | **Yes** — **§6** is binding. |

---

## 4. File lock (IN)

**Exactly** these **eight** paths (from [STASH0_T1_PREFLIGHT_EXECUTION_ROW.md](STASH0_T1_PREFLIGHT_EXECUTION_ROW.md) **T1-C1**). **No** additions without a **new** row ID.

| Path |
|------|
| `backend/api/routes/voice/synthesis.py` |
| `backend/api/routes/voice/_shared.py` |
| `backend/services/synthesis_service.py` |
| `backend/api/v3/synthesis.py` |
| `backend/api/v3/engines.py` |
| `backend/api/v3/voices.py` |
| `app/core/engines/base.py` |
| `backend/core/settings.py` |

---

## 5. Proof commands (post-implementation on `main`)

Run **after** the reconciled diff is merged to **`main`** (or on the PR verification tree that will become **`main`**).

| # | Command | Expected |
|---|---------|----------|
| 1 | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0** errors |
| 2 | `python -m pytest tests/unit/test_synthesis_policy.py tests/ci/test_golden_loop_smoke.py -q --tb=line` | **PASS** — see **§5.1** (proof-only; **no** stash checkout of these paths unless **§4** expanded under a **new** sign-off) |
| 3 | `python scripts/run_verification.py` | **ALL PASS**; if updating **STATE** proof lines, copy **`timestamp_short`** **verbatim** from `.buildlogs/verification/last_run.json` |
| 4 | `.\scripts\verify.ps1 -Quick` | **PASS** — **mandatory** for **closure** of this implementation row; advance **`artifacts/verify/latest_pointer.json`** **only** on **PASSED** **Quick** for the **implementation** commit tree |

### 5.1 Proof-only tests (**Option A** — locked for this row)

- **`tests/unit/test_synthesis_policy.py`** and **`tests/ci/test_golden_loop_smoke.py`** are **not** in **§4** and are **not** checked out from **`stash@{0}`** for **T1-S1**.
- They **are** **required** **§5** smoke on the **implementation** tree **unless** **§1** documents an explicit carve-out (equivalent to plan **Option C**) **before** extract.
- **§6** still forbids **editing** those files from stash under **T1-S1**; running them as **read-only** proof is **not** a contradiction.

**Historical vs global proof:** Repo-global **Quick** is authoritative for **pointer** and **`commit_hash`** in **Truth Sync**. **`python scripts/run_verification.py`** is the **gate/ledger** track (**`.buildlogs/verification/last_run.json`**). **Do not** equate **`git rev-parse HEAD`** with **`latest_pointer.json`** after doc-only follow-ups.

---

## 6. OUT (strict)

| # | OUT |
|---|-----|
| 1 | **`docs/api/openapi.json`**, **`shared/schemas/**`**, **`tests/contract/**`** — **T3**; joint scope only under a **separate** signed row. |
| 2 | **T1-C2** ancillary routes/registry/helpers (e.g. `route_registry.py`, `backend/api/routes/` outside **`routes/voice/`** lock, `backend/api/v3/projects.py`, `backend/api/v3/models.py`, `audio_path_resolver`, `audit_logger`, `script_store`, …). |
| 3 | **T1-C3** cross-cutting: `rate_limiting*.py`, `auth_middleware.py`, `models_additional.py`, related unit tests — unless a **new** row expands scope. |
| 4 | **Editing** or **stash checkout** of **T1-C4** test **files** (`test_golden_loop_smoke.py`, `test_synthesis_policy.py`) — **OUT** for **T1-S1** (**Option A**): **run** them in **§5** only on **post-merge tree**; no lock, no stash apply. Carve-out = **§1** explicit deferral (**Option C**). |
| 5 | **`src/VoiceStudio.App/**`**, **T5** `LibraryView.xaml.cs`, **T4** docs/governance sweeps (`docs/design/*` churn, `.cursor/*`, `tools/context/*`) except **STATE**/registry lines required to record **§8**. |
| 6 | Mining any **`stash@{0}`** path **outside §4**. |
| 7 | Bulk **`stash pop`**. |
| 8 | Reopening **Workflow 8 / 7 / Product Trust**, **A4**, or **Pass 06** slice narratives under this banner. |

---

## 7. Execution sequence (extraction — **after** §1 dated)

**Frozen method (same family as [STASH0_T2_VERIFY_CI_EXECUTION_ROW.md](STASH0_T2_VERIFY_CI_EXECUTION_ROW.md) §7):**

1. **`git stash list`** — confirm **`stash@{0}`** message matches **STATE**.
2. **`git switch -c <branch-name> main`** (branch name team-defined, e.g. `stash0-t1-s1-01`).
3. **`git checkout 'stash@{0}' -- <eight paths from §4>`** (PowerShell-quoted stash ref) — apply **only** the lock; **`stash@{0}`** remains for **T1** remainder / **T3** / **T4** unless product later chooses **`git stash branch`** **knowing** the stash entry may be consumed.

**Do not** use **`git stash pop`**.

**Reconcile** each checked-out file against **`main`** intent: **Keep** / **Partial** / **Drop** (see **§3**).

---

## 8. Closure record (fill after proof only)

| Field | Value |
|-------|--------|
| **Quick** | **`artifacts/verify/20260326_230934`** — **PASSED** (`verify.ps1 -Quick` on implementation tree) |
| **`latest_pointer.json` `commit_hash`** | **`f60477978e72ad3bdbcfb2f2ba7e56c50ebc76c3`** |
| **`run_verification.py` `timestamp_short`** | **`20260327-145802`** (verbatim from `.buildlogs/verification/last_run.json` after **2026-03-27** governance sync — matches **STATE** / registry; closure-era run was **`20260326-232319`**) |
| **Notes** | Selective **`git checkout 'stash@{0}' --`** eight **§4** paths on branch **`stash0-t1-s1-01`**; no paths dropped; **`stash@{0}`** remains **parked**; **§5** pytest pair **PASS** (5 tests); **`dotnet build`** **0** errors. **Merge truth:** confirm **`main`** vs branch with **`git merge-base main stash0-t1-s1-01`** and **`git log main..stash0-t1-s1-01`** — **`main`** does not carry **`f6047797`** / **`3127094d`** until merged. |

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-24 | **Created** — **GOV-STASH0-T1-S1-01**; **§1 Pending**; eight-path lock aligned with preflight **T1-C1**. |
| 2026-03-24 | **Sign-off readiness** — **§3** preflight table filled (`main` vs **`stash@{0}`**); all paths **Keep**; **§5.1** **Option A**; **§1** engineering row dated; **product** implementation row **Pending**. |
| 2026-03-26 | **Implementation go** — **§1** product/engineering row dated; selective extract **§7** authorized. |
| 2026-03-26 | **Closure** — **§8** filled; **`verify.ps1 -Quick`** **`20260326_230934`**; pointer **`f6047797`**; **`run_verification`** **`20260326-232319`**. |
| 2026-03-27 | **Governance sync** — **`run_verification`** ledger stamp reconciled to **`20260327-145802`**; registry banner = repo-global **T1-S1** Quick (not **T2**). |
