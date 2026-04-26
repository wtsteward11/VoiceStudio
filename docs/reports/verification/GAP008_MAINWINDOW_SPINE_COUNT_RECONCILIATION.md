# GAP-008 MainWindow regression spine — count reconciliation (Tasks 418–419)

**Date:** 2026-04-25  
**Scope:** Explain why `dotnet test --list-tests` and **`Passed: N`** were **118** when the filter file contained a **`#` header line**, versus **122** when only the long `FullyQualifiedName~...|...` line is passed to `--filter`.

## Commands (repro)

From repo root, `VoiceStudio.App.Tests`, `Debug`, `x64`:

```powershell
$line2 = (Get-Content tools\gap008_mainwindow_regression_filter.txt)[1]
$raw   = (Get-Content tools\gap008_mainwindow_regression_filter.txt -Raw).Trim()
dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --list-tests --filter $line2
dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --list-tests --filter $raw
```

## Observed counts (pre–Slice 17 filter extension; repo state 2026-04-25)

| Filter source | `--list-tests` count (indented test lines) |
|---------------|--------------------------------------------|
| **Line 2 only** (single OR expression, no `#`; tokens before Slice **17** only) | **122** |
| **Raw file** (`Trim()` on full two-line file: `#` comment + newline + line 2) | **118** |
| **Line 2** with an appended suffix of space + `#` + `tail` on the same string | **118** |

After Slice **17** tokens (`Gap008Slice17Tests`, `MainWindowStatusStripClockShellBridgeTests`) were prepended to the OR line, **`--list-tests`** and **`dotnet test`** with the script’s **effective** filter (no `#` lines) report **130** tests — see **`.buildlogs/gap008_spine/last_run_summary.json`** on a green run.

## The four tests dropped (122 → 118)

Diffing listed tests shows the four missing entries are all methods on **`VoiceStudio.App.Tests.Views.Gap008Slice5Tests`** (recent-project mutation seam):

- `MainWindow_recent_mutation_handlers_delegate_to_mutation_bridge`
- `MainWindow_still_owns_PopulateRecentProjectsMenu_and_workflow_bridge_Slice4`
- `MainWindowRecentProjectsMutationBridge_excludes_menu_population_and_project_workflow`
- `MainWindowProjectWorkflowBridge_unchanged_for_coordinator_only`

They are the only tests in the assembly whose **fully qualified name** contains the substring **`Gap008Slice5Tests`** (the last `|` clause is `FullyQualifiedName~Gap008Slice5Tests`).

## Root cause (engineering)

**Control experiment:** append a space + `#` + arbitrary text to the **end** of the same OR-only string (`$line2 + " # tail"`). The listed set drops from **122** to **118** and loses exactly the four **`Gap008Slice5Tests`** methods above. So a **`#` inside the `--filter` value** alters how the **last** `FullyQualifiedName~…` clause is interpreted (trailing token corrupted / truncated), not merely “ignored documentation.”

Passing **`Get-Content -Raw`** on the two-line filter file feeds **`#` + first-line text + newline + OR line** into `--filter`, which reproduces the **118** set. **Operational fix:** never pass header lines into `--filter`; **strip lines whose trimmed text starts with `#`** and use the remaining OR line only → **122** listed tests match the filter file’s intended membership.

Exact tokenizer rules are implementation details of the test platform; the reconciliation above is **empirical** and reproducible with the commands in §Commands.

After **Task 419**, `scripts/Run-Gap008MainWindowRegressionTests.ps1` does that and writes discovery to `.buildlogs/gap008_spine/`.

## “114 + 8 = 122” vs “Passed: 118”

- **114** was the spine **before** Slice 16 tokens were added (see `.cursor/STATE.md` history: **Tasks 399–408** row).
- **+8** is misleading as “eight new spine tests”: Slice 16 adds **four** tests in **`Gap008Slice16Tests`** and **four** in **`MainWindowNotificationCenterShellBridgeTests`**, but the spine was already mis-counted at **118** because **`Gap008Slice5Tests`** was **accidentally excluded** whenever the filter string included the **`#`** header.
- **122** is the **true** spine size for the current OR line **without** a `#` prefix in the `--filter` value.

## Authority

- **Membership** of the spine: [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt) (extend-only).
- **Auditable count + run:** [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1) + `.buildlogs/gap008_spine/` (Task 419).

## `last_run_summary.json` contract (Tasks 433–438)

**Policy:** `.buildlogs/` is **gitignored** (local-only). Artifacts are **not** canonical in git; regenerate with [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1). STATE and briefs may cite **paths + last green counts** from a developer machine; do not treat committed TRX/JSON under `.buildlogs/` as repo SSOT.

**Required keys** (PowerShell `ConvertTo-Json` from [`Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1)):

| Key | Meaning |
|-----|---------|
| `timestampUtc` | Run end timestamp (ISO-8601). |
| `filterPath` | Absolute path to `tools/gap008_mainwindow_regression_filter.txt`. |
| `effectiveFilter` | OR expression passed to `dotnet test --filter` (after stripping `#` lines). |
| `discoveryPath` | `last_discovery.txt` from `--list-tests`. |
| `listedTestCount` | Count of indented test lines after the “available tests” marker. |
| `trxPath` | Timestamped `.trx` under `.buildlogs/gap008_spine/`. |
| `passed` / `failed` | Parsed from TRX Counters (nullable if TRX missing). |
| `skippedApprox` | From TRX `notExecuted` when present. |
| `dotnetExitCode` | `dotnet test` exit code. |

**CI fixture:** [`tests/fixtures/gap008_spine/last_run_summary_example.json`](../../tests/fixtures/gap008_spine/last_run_summary_example.json) — validated by [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py). Update the fixture only when the script’s summary shape **intentionally** changes.

**Spine size after Slice 18:** extend-only filter adds **`Gap008Slice18Tests`** + **`MainWindowStatusStripMetricsShellBridgeTests`**; a green run reported **139** listed / passed (see local `last_run_summary.json` after Task 434).

## Spine size after Slice 19 (Tasks 439–448)

**Delta:** **`Gap008Slice19Tests`** (**4** methods) + **`MainWindowStatusBarCoordinatorShellBridgeTests`** (**3** methods) = **+7** tests prepended to the canonical OR filter in [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**Arithmetic:** **139** (post–Slice 18 green) **+ 7** = **146** listed / passed on a green run (`listedTestCount` / `passed` in **`.buildlogs/gap008_spine/last_run_summary.json`**; TRX **`gap008_spine_*.trx`** from [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1)).

## Spine size after Slice 20 (Tasks 449–458)

**Delta:** **`Gap008Slice20Tests`** (**7** methods) + **`MainWindowMenuToolActivationShellBridgeTests`** (**8** methods) = **+15** tests prepended to the canonical OR filter in [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**Arithmetic:** **146** (post–Slice 19 green) **+ 15** = **161** listed / passed on a green run (`listedTestCount` / `passed` in **`.buildlogs/gap008_spine/last_run_summary.json`**). **CI:** under `dotnetExitCode == 0` and `failed == 0`, [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) asserts **`listedTestCount == passed`** (Task 453; example fixture [`tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json`](../../tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json)).

## Spine size after Slice 21 (keyboard shortcuts shell)

**Delta:** **`Gap008Slice21Tests`** (**4** methods) + **`MainWindowKeyboardShortcutsShellBridgeTests`** (**5** methods) = **+9** tests prepended to the canonical OR filter in [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**Arithmetic:** **161** (post–Slice 20 green) **+ 9** = **170** listed / passed on a green run (`listedTestCount` / `passed` in **`.buildlogs/gap008_spine/last_run_summary.json`** from [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1); local proof **2026-04-25** / **2026-04-26** UTC). Green coherence fixture updated to **170** / **170** with the extended `effectiveFilter` (no `#` lines in the value passed to `--filter`).

## Spine size after Slice 22 (recent projects menu population shell)

**Delta:** **`Gap008Slice22Tests`** (**4** methods) + **`MainWindowRecentProjectsMenuPopulationShellBridgeTests`** (**3** methods) = **+7** tests prepended to the canonical OR filter in [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**Arithmetic:** **170** (post–Slice 21 green) **+ 7** = **177** listed / passed on a green run (`listedTestCount` / `passed` in **`.buildlogs/gap008_spine/last_run_summary.json`**; TRX from [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1)). Green coherence fixture [`tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json`](../../tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json) updated to **177** / **177** (Tasks **39–45**, **2026-04-26**).

## Spine size after Slice 23 (nav panel preview shell)

**Delta:** **`Gap008Slice23Tests`** (**4** methods) + **`MainWindowPanelPreviewShellBridgeTests`** (**4** methods) = **+8** tests prepended to the canonical OR filter in [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**Arithmetic:** **177** (post–Slice 22 green) **+ 8** = **185** listed / passed on a green run (`listedTestCount` / `passed` in **`.buildlogs/gap008_spine/last_run_summary.json`**). Update [`tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json`](../../tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json) to **185** / **185** when the extended `effectiveFilter` is committed.

## Spine size after Slice 24 (panel quick-switch indicator shell)

**Delta:** **`Gap008Slice24Tests`** (**6** methods) + **`MainWindowPanelQuickSwitchShellBridgeTests`** (**1** method) = **+7** tests prepended to the canonical OR filter in [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt). (**`MainWindowLifetimeCleanupCoreChannels`** gained required **`DisposeQuickSwitchHideTimer`**; existing **`MainWindowLifetimeCleanupShellBridgeTests`** updated — not a new spine test count.)

**Arithmetic:** **185** (post–Slice 23 green) **+ 7** = **192** listed / passed on a green run. Green coherence fixture [`tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json`](../../tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json) updated to **192** / **192** (2026-04-26).

## Spine size after Slice 25 (panel docking / cross-region swap shell)

**Delta:** **`Gap008Slice25Tests`** (**6** methods) + **`MainWindowPanelDockShellBridgeTests`** (**1** method) = **+7** tests prepended to the canonical OR filter in [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**Arithmetic:** **192** (post–Slice 24 green) **+ 7** = **199** listed / passed on a green run (`listedTestCount` / `passed` in **`.buildlogs/gap008_spine/last_run_summary.json`**; TRX **`gap008_spine_20260426_104323.trx`** from [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1)). Green coherence fixture updated to **199** / **199** with the extended `effectiveFilter`.

## Spine size after Slice 26 (startup backend overlay shell)

**Delta:** **`Gap008Slice26Tests`** (**6** methods) + **`MainWindowStartupOverlayShellBridgeTests`** (**1** method) = **+7** tests prepended to the canonical OR filter in [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**Arithmetic:** **199** (post–Slice 25 green) **+ 7** = **206** listed / passed on a green run (`listedTestCount` / `passed` in **`.buildlogs/gap008_spine/last_run_summary.json`**; TRX **`gap008_spine_20260426_110349.trx`** from [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1)). Green coherence fixture [`tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json`](../../tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json) updated to **206** / **206** with the extended `effectiveFilter`.

## Spine size after Slice 27 (panel region focus + cycling shell)

**Delta:** **`Gap008Slice27Tests`** (**5** methods) + **`MainWindowPanelRegionFocusShellBridgeTests`** (**1** method) = **+6** tests prepended to the canonical OR filter in [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt). (**`Gap008Slice24Tests`** updated for Slice 27 handoff — same method count; not a spine **+N** change.)

**Arithmetic:** **206** (post–Slice 26 green) **+ 6** = **212** listed / passed on a green run (`listedTestCount` / `passed` in **`.buildlogs/gap008_spine/last_run_summary.json`**; TRX **`gap008_spine_20260426_113330.trx`** from [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1)). Green coherence fixture [`tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json`](../../tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json) updated to **212** / **212** with the extended `effectiveFilter` (Task 85 — 2026-04-26).

## Spine size after Slice 28 (Help / About shell)

**Delta:** **`Gap008Slice28Tests`** (**3** methods) + **`MainWindowHelpAboutShellBridgeTests`** (**2** methods) = **+5** tests prepended to the canonical OR filter in [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**Arithmetic:** **212** (post–Slice 27 green) **+ 5** = **217** listed / passed on a green run (`listedTestCount` / `passed` in **`.buildlogs/gap008_spine/last_run_summary.json`**; TRX **`gap008_spine_20260426_121432.trx`** from [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1)). Green coherence fixture updated to **217** / **217** with the extended `effectiveFilter` (Tasks 97–102 — 2026-04-26).

## Spine size after Slice 29 (Edit — Undo / Redo shell)

**Delta:** **`Gap008Slice29Tests`** (**3** methods) + **`MainWindowEditUndoRedoShellBridgeTests`** (**4** methods) = **+7** tests prepended to the canonical OR filter in [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**Arithmetic:** **217** (post–Slice 28 green) **+ 7** = **224** listed / passed on a green run (TRX **`gap008_spine_20260426_123447.trx`**; Tasks **105–113** — 2026-04-26). Green coherence fixture updated to **224** / **224** with the extended `effectiveFilter`.

## Spine size after Slice 30 (global transport + timeline shell)

**Delta:** **`Gap008Slice30Tests`** (**4** methods) + **`MainWindowGlobalTransportShellBridgeTests`** (**3** methods) = **+7** tests prepended to the canonical OR filter in [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**Arithmetic:** **224** (post–Slice 29 green) **+ 7** = **231** listed / passed on a green run (TRX **`gap008_spine_20260426_125336.trx`**; Tasks **114–123** — 2026-04-26). Green coherence fixture [`tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json`](../../tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json) updated to **231** / **231** with the extended `effectiveFilter`.

## Spine size after Slice 31 (import-audio / import workflow shell)

**Delta:** **`Gap008Slice31Tests`** (**2** methods) + **`MainWindowImportWorkflowShellBridgeTests`** (**4** methods) = **+6** tests prepended to the canonical OR filter in [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**Arithmetic:** **231** (post–Slice 30 green) **+ 6** = **237** listed / passed on a green run (TRX **`gap008_spine_20260426_133328.trx`**; Tasks **124–133** — 2026-04-26). Green coherence fixture updated to **237** / **237** with the extended `effectiveFilter`. **Task 134** (optional WinUI appendix) is **not** a spine gate.
