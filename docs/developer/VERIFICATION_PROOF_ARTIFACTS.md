# Verification and proof artifacts

**Status:** Canonical policy (Tasks 378–387)  
**Last updated:** 2026-04-25 (GAP-008 spine summary schema pointer)

## Markdown proof documents

- **`docs/reports/verification/PROOF_*.md`** files are the **default canonical** record for operator-facing verification (checklists, non-claims, coverage limits, links to code/tests).
- They are reviewable in diffs and do not require binary tooling to consume.

## Binary screenshots and images

- **Default:** Do **not** commit large screenshots to git unless there is an explicit repo/LFS policy and size budget.
- **Optional:** Operators may store screenshots under `%LOCALAPPDATA%\VoiceStudio\...` or attach them to PRs; the **markdown proof** must still state what was observed and what is **not** claimed (single session, no degraded-backend matrix, etc.).
- **When to commit a PNG:** Reserve for cases where the **visual state is the entire defect** and text cannot carry the evidence; prefer a stable name next to the sibling `PROOF_*.md` and link from that markdown file.

## GAP-008 MainWindow regression spine

- **Single source of truth** for MSTest `FullyQualifiedName` filter tokens: [`tools/gap008_mainwindow_regression_filter.txt`](../tools/gap008_mainwindow_regression_filter.txt).
- **Runner:** [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../scripts/Run-Gap008MainWindowRegressionTests.ps1) reads that file and invokes `dotnet test`. Extend tokens **only** in the `.txt` file (superset rule); do not hand-copy long filter strings into multiple briefs.
- **Local artifacts:** `.buildlogs/gap008_spine/last_run_summary.json` + `last_discovery.txt` + timestamped `.trx` — **ephemeral** (`.buildlogs/` gitignored). Schema + policy: [GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md § last_run_summary.json](../reports/verification/GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md); shape gate: `tests/ci/test_gap008_spine_summary_shape.py`.

## Related

- Example operator visual closure: [`docs/reports/verification/PROOF_GAP008_STARTUP_TRUTH_VISUAL_2026-04-25.md`](../reports/verification/PROOF_GAP008_STARTUP_TRUTH_VISUAL_2026-04-25.md)
- MainWindow decomposition: [`docs/design/MAINWINDOW_DECOMPOSITION_PLAN.md`](../design/MAINWINDOW_DECOMPOSITION_PLAN.md)
