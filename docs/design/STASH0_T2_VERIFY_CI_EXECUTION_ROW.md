# stash@{0} — T2 Verify / CI harness — execution row

**Row ID:** **GOV-STASH0-T2-VERIFY-01**  
**Purpose:** Execution-grade freeze for the **`stash@{0}`** **T2 — Verify / CI baselines** slice named in [`.cursor/STATE.md`](../../.cursor/STATE.md) (~ **2026-03-28** classification). Source stash message: *WIP: pre-Pass06-20260326 unclassified local and untracked*.  
**Date:** 2026-03-28  
**Status:** **Planning — §1 sign-off Pending.** **No** edits to locked files **until** §1 is dated. **No** bulk `stash pop`.

**Related:** [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md); [RETAINED_ASYNC_EXEMPTIONS.md](RETAINED_ASYNC_EXEMPTIONS.md); [STARTUP_ORCHESTRATION_HARDENING_PLAN.md](STARTUP_ORCHESTRATION_HARDENING_PLAN.md); [`scripts/verify.ps1`](../../scripts/verify.ps1); [`verification-harness.mdc`](../../.cursor/rules/workflows/verification-harness.mdc).

---

## 1. Sign-off

| Role | Decision | Date |
|------|----------|------|
| **Product / engineering** | **Pending** — Accept **GOV-STASH0-T2-VERIFY-01**: six-path **file lock** only; **primary** objective = **`verify.ps1` harness operability** (`-OnlyStage`, C# unit-test shard model, `PYTHONUNBUFFERED` for piped runs); **supporting** changes **only** in lock: `run_verification.py` (testhost cleanup, build timeout), `start_backend.ps1` project-root fix, proof **TTS** field alignment (`tts_engine_name`), `retained_async_baseline.txt` line-map refresh. **OUT** list is binding. | **Pending** |

---

## 2. Objective (single sentence)

Land the stashed **verification pipeline** improvements so developers can run **targeted** verify stages and avoid **piped-hang / DLL-lock** failure modes, while keeping **proof fingerprints** and **retained-async baseline** consistent with those scripts—**without** any backend synthesis, OpenAPI, or app `src/` change.

**Primary driver:** `scripts/verify.ps1` (stage documentation, `-OnlyStage`, unbuffered Python).  
**Supporting (same row, same PR after sign-off):** companion tooling in the **five other locked paths** per preflight below.

---

## 3. Preflight — `git diff main 'stash@{0}' -- <path>`

Executed on **2026-03-28** (representative; re-run before extraction). Summary only; **execution** must re-diff.

| Path | Material delta (vs `main`) |
|------|----------------------------|
| `scripts/verify.ps1` | **Large:** `-OnlyStage` parameter + `ValidateSet`; stage list / numbering; **11** C# unit-test shards documented; `PYTHONUNBUFFERED=1`; Quick Critical Gates in doc header. |
| `scripts/run_verification.py` | **Medium:** Startup docstring; **`testhost.exe`** probe + **`taskkill`** before build path; **`dotnet build`** timeout **90s** for `--build`; related `run_check` handling (diff truncated in preflight). |
| `scripts/ci/proof_fingerprint.py` | **Small:** `tts_engine_name` in `EVIDENCE_FIELDS` for golden-loop proof class. |
| `.ci/proof_schema.json` | **Small:** `tts_engine_name` in allowed fields + rule block. |
| `.ci/retained_async_baseline.txt` | **Medium:** Header comment; **removed** several ViewModel line entries; **adjusted** `TagManagerViewModel` lines; removes Help/KeyboardShortcuts/EffectsMixer lines — **baseline-only** (does not edit `.cs` in this row). |
| `scripts/start_backend.ps1` | **Small:** `$ProjectRoot = Split-Path -Parent $PSScriptRoot` (was **double** parent — fixes root resolution relative to `scripts/`). |

**Coherence note:** The bundle is **one landing** authorized **only** if sign-off accepts **verify.ps1** as primary and the other five as **necessary supporting** adjustments discovered in the same pre–Pass 06 WIP. If product wants **only** proof-schema or **only** baseline churn, **narrow** the lock in a **new** row—do **not** silently drop OUT discipline.

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

Run after code lands from **`git stash branch …`** + reconcile to **`main`**:

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
2. **`git stash branch <name> 'stash@{0}'`** (PowerShell quoting); inspect **only** the six paths; reconcile to **`main`**.
3. Implement **only** locked files; run **§5** proofs.
4. Update **STATE** **ACTIVE WINDOW**, **CANONICAL_REGISTRY** Session State / this doc **Status**, and **closure** paragraph below.

---

## 8. Closure record (fill after proof only)

| Field | Planned / actual |
|-------|------------------|
| **Quick** | `artifacts/verify/<timestamp>` — **Pending** |
| **`latest_pointer.json` `commit_hash`** | **Pending** |
| **Notes** | **Pending** |

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-28 | **Created** — **GOV-STASH0-T2-VERIFY-01**; preflight from `main` vs `stash@{0}`; §1 **Pending**. |
