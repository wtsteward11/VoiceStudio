# stash@{0} — T2 Verify / CI harness — execution row

**Row ID:** **GOV-STASH0-T2-VERIFY-01**  
**Purpose:** Execution-grade freeze for the **`stash@{0}`** **T2 — Verify / CI baselines** slice named in [`.cursor/STATE.md`](../../.cursor/STATE.md) (~ **2026-03-28** classification). Source stash message: *WIP: pre-Pass06-20260326 unclassified local and untracked*.  
**Date:** 2026-03-28 (created); **§1 signed:** 2026-03-26  
**Status:** **Closed (implementation + proof)** — **2026-03-26**. Locked paths merged to **`main`**; **§5** green. **No** bulk `stash pop`.

**Related:** [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md); [RETAINED_ASYNC_EXEMPTIONS.md](RETAINED_ASYNC_EXEMPTIONS.md); [STARTUP_ORCHESTRATION_HARDENING_PLAN.md](STARTUP_ORCHESTRATION_HARDENING_PLAN.md); [`scripts/verify.ps1`](../../scripts/verify.ps1); [`verification-harness.mdc`](../../.cursor/rules/workflows/verification-harness.mdc).

---

## 1. Sign-off

| Role | Decision | Date |
|------|----------|------|
| **Product / engineering** | **Accepted** — Six-path **file lock** only; **§2** primary objective (**`verify.ps1` harness operability**); supporting deltas in the five other paths per **§3**; **§5** proof set and **§6 OUT** are binding. Re-preflight **2026-03-26**: all six paths still materially differ vs **`main`** (`git diff --stat`; none empty). Sign-off readiness gates below — all satisfied. | **2026-03-26** |

---

## 2. Objective (single sentence)

Land the stashed **verification pipeline** improvements so developers can run **targeted** verify stages and avoid **piped-hang / DLL-lock** failure modes, while keeping **proof fingerprints** and **retained-async baseline** consistent with those scripts—**without** any backend synthesis, OpenAPI, or app `src/` change.

**Primary driver:** `scripts/verify.ps1` (stage documentation, `-OnlyStage`, unbuffered Python).  
**Supporting (same row, same PR after sign-off):** companion tooling in the **five other locked paths** per preflight below.

---

## 3. Preflight — `git diff main 'stash@{0}' -- <path>`

**Re-run 2026-03-26** on repo tip (`main` vs `stash@{0}`, PowerShell-quoted ref). **`git diff --stat`** line counts appended; narrative unchanged where still tallied.

| Path | Stat (`--stat`) | Material delta (vs `main`) |
|------|-----------------|------------------------------|
| `scripts/verify.ps1` | **1 file, +771 −123** | **Large:** `-OnlyStage` + `ValidateSet`; stage list / numbering; C# unit-test **shards** / docs; `PYTHONUNBUFFERED=1`; Quick Critical Gates header. |
| `scripts/run_verification.py` | **1 file, +66 −2** | **Medium:** testhost cleanup + build timeout path for `--build`; related `run_check` handling. |
| `scripts/ci/proof_fingerprint.py` | **1 file, +1** | **Small:** `tts_engine_name` in `EVIDENCE_FIELDS` (golden-loop proof). |
| `.ci/proof_schema.json` | **1 file, +3 −2** | **Small:** `tts_engine_name` in allowed fields + rule block. |
| `.ci/retained_async_baseline.txt` | **1 file, +4 −13** | **Medium:** baseline line-map refresh — **text only** (no `.cs` edits in this row). |
| `scripts/start_backend.ps1` | **1 file, +1 −1** | **Small:** project root = `Split-Path -Parent $PSScriptRoot` (fixes double-parent). |

**Coherence note:** The bundle is **one landing** with **verify.ps1** as primary; the other five are **supporting** deltas from the same pre–Pass 06 WIP. **None** of the six diffs are empty vs current **`main`** — nothing marked superseded for drop at preflight; per-file **Keep** for reconcile pending CI green.

### Sign-off readiness gates (2026-03-26)

| Gate | Result |
|------|--------|
| **Single slice?** | **Yes** — one narrative: verification pipeline / harness + aligned proof + baseline + backend script root. |
| **Obsolete vs `main`?** | **None empty** — all six paths still have material `git diff`; land and re-validate with **§5** (retained-async may require baseline truth on tip). |
| **One objective?** | **Yes** — **§2** primary driver is **`verify.ps1`**; others only support that row. |
| **OUT blocks T1/T3/T4?** | **Yes** — **§6** items **1–3** forbid backend, app product surfaces, OpenAPI/schemas/contracts by default, and non-T2 stash mining. |

### Extraction note (omnibus stash)

Where **`stash@{0}`** still holds **non-T2** topics, use **`git switch -c <branch> main`** then **`git checkout 'stash@{0}' -- <six paths>`** to apply **only** the file lock, leaving **`stash@{0}`** intact for **T1/T3/T4** (avoids `git stash branch` consuming the whole stash). Equivalent to **§7** intent: isolate T2 delta, reconcile to **`main`**.

---

## 4. File lock (IN)

**Exactly** these paths (from [`.cursor/STATE.md`](../../.cursor/STATE.md) **T2** row):

| Path |
|------|
| `scripts/verify.ps1` |
| `scripts/run_verification.py` |
| `scripts/ci/proof_fingerprint.py` |
| `.ci/proof_schema.json` |
| `.ci/retained_async_baseline.txt` |
| `scripts/start_backend.ps1` |

**Rule:** Any additional path requires **explicit** scope failure / new row—**no** creep via “small fix next file.”

---

## 5. Proof commands (post-implementation)

Run after code lands on **`main`** (from **§3** selective checkout or full stash-branch workflow — **§7**):

| # | Command | Expected |
|---|---------|----------|
| 1 | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0** errors |
| 2 | `python scripts/run_verification.py` | **ALL PASS**; capture `.buildlogs/verification/last_run.json` `timestamp_short` for **STATE** if updating governance |
| 3 | `.\scripts\verify.ps1 -Quick` | **PASS**; new **`artifacts/verify/<run>`**; advance **`artifacts/verify/latest_pointer.json`** only on **PASSED** tree for **implementation** commit |
| 4 | (Optional row-specific) If proof fingerprint gate runs in CI: run the project’s **documented** fingerprint validation against `.ci/proof_schema.json` | **PASS** per repo script |

**Honesty:** Update [.cursor/STATE.md](../../.cursor/STATE.md) **Truth Sync** / **LATEST PROOF INDEX** **after** green Quick—**not** before.

---

## 6. OUT (strict)

| # | OUT |
|---|-----|
| 1 | **`backend/**`**, **`app/core/**`**, product **`src/VoiceStudio.App/**`** (except what is **only** referenced inside `retained_async_baseline.txt` **text**—**no** `.cs` edits in this row). |
| 2 | **`docs/api/openapi.json`**, **`shared/schemas/**`**, **`tests/contract/**`** unless a **new** signed row explicitly expands scope (default: **OUT**). |
| 3 | Mining **`stash@{0}`** paths outside **T2** lock (**T1 / T3 / T4**); **T5** `LibraryView.xaml.cs` remains **Discard** vs closed **A4**. |
| 4 | Casual **Workflow 8 / 7 / Product Trust** `src/` without signed **§8**. |
| 5 | Bulk **`stash pop`**. |
| 6 | Rerouting drag/import/A4/Option A matrix—**closed**. |

---

## 7. Execution sequence

1. **Date §1** when **Objective**, **lock**, **OUT**, and **proof** are sufficient that another engineer needs **no improvisation**.
2. **Extract** the six paths onto a branch from **`main`**: prefer **`git switch -c <name> main`** + **`git checkout 'stash@{0}' -- <six paths>`** so **`stash@{0}`** remains for **T1/T3/T4**; optionally **`git stash branch`** if product accepts dropping the stash entry — inspect **only** the lock; reconcile per-file (**Keep** / **partial** / **drop**).
3. Merge to **`main`**; run **§5** proofs.
4. Update **STATE** **ACTIVE WINDOW**, **CANONICAL_REGISTRY** Session State / this doc **Status**, and **§8** closure.

---

## 8. Closure record (fill after proof only)

| Field | Planned / actual |
|-------|------------------|
| **Quick** | **`artifacts/verify/20260326_211554`** (**PASSED**; post-merge tree). |
| **`latest_pointer.json` `commit_hash`** | **`dc07e51597e42bac294899dcaf123339e63ccbbc`** (merge **T2** into **`main`**; pointer aligned by **`verify.ps1 -Quick`**). |
| **Notes** | **`python scripts/run_verification.py`** **ALL PASS** — **`20260326-212656`** (**post–§8** **STATE**/**registry** sync on machine that ran closure). T2 applied via **`stash0-t2-verify-01`** selective **`git checkout 'stash@{0}' -- <six paths>`**; **`stash@{0}`** retained for **T1/T3/T4**. |

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-28 | **Created** — **GOV-STASH0-T2-VERIFY-01**; preflight from `main` vs `stash@{0}`; §1 **Pending**. |
| 2026-03-26 | **§1 signed**; preflight **re-run** with `--stat`; sign-off gates table; extraction note (selective checkout); **§7** updated. |
| 2026-03-26 | **Closed** — merge **`stash0-t2-verify-01`** → **`main`**; **§5** proofs green; **§8** filled; **`latest_pointer.json`** **`dc07e515`**. |
