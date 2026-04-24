# VoiceStudio — Production Build Plan (Windows)

**Last updated:** 2026-01-11  
**Non‑negotiable packaging lane:** **unpackaged apphost EXE + installer only** (**no MSIX**)

This document is a **single entry point** that stitches together the repo’s existing Gate C/Gate H scripts and evidence requirements into a step‑by‑step, reproducible production path.

Canonical context / governance:

- **Living progression log:** `docs/governance/overseer/PROJECT_PROGRESSION_LOG.md`
- **Ledger (source of truth):** `docs/archive/Recovery_Plan/QUALITY_LEDGER.md`
- **Evidence packets (“handoffs”):** `docs/governance/overseer/handoffs/VS-*.md`
- **ADR (MSIX rejected):** `docs/architecture/ADR_GATE_C_ARTIFACT_CHOICE.md`

---

## 0) Reality check: how this repo actually boots WinUI today

This repo is **currently designed** to use the **system‑installed Windows App SDK runtime packages** (unpackaged app), not self-contained WinAppSDK:

- `src/VoiceStudio.App/Program.cs`:
  - Calls `Bootstrap.Initialize(0x00010008)` (Windows App SDK 1.8) for unpackaged runs
  - Writes deterministic pre‑App crash artifacts to `%LOCALAPPDATA%\VoiceStudio\crashes\*`
- `src/VoiceStudio.App/VoiceStudio.App.csproj`:
  - Sets `<WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>`
  - Deletes OS-adjacent runtime DLLs from publish output when `WindowsAppSDKSelfContained != true` (to avoid CoreMessagingXP.dll fail-fast / dependency issues)
- `scripts/gatec-publish-launch.ps1`:
  - Publishes with `-p:SelfContained=true` **and** `-p:WindowsAppSDKSelfContained=false` (overrides csproj defaults during Gate C proof runs)

**Implication:** any plan that says “just flip WinAppSDK self-contained” is a _real lane change_ (it requires changing Gate C script + re-validating publish output behavior and Gate C/H proofs).

---

## 1) Critical Path “Now”: close Gate C (VS-0012) with deterministic proof

### 1.1 Gate C required artifacts (what to attach as proof)

**Build + publish proof artifacts (repo‑generated):**

- `.buildlogs\gatec-latest.txt`
- `.buildlogs\gatec-publish-<yyyyMMdd-HHmmss>-<pid>.binlog`
- `.buildlogs\x64\Release\gatec-publish\gatec-launch.log` (default publish dir; may vary if you override `-PublishDir`)

**Crash + UI smoke artifacts (app‑generated):**

- `%LOCALAPPDATA%\VoiceStudio\crashes\boot_latest.json`
- `%LOCALAPPDATA%\VoiceStudio\crashes\latest_startup_exception.log` (if managed startup throws)
- `%LOCALAPPDATA%\VoiceStudio\crashes\latest.log` (if App.xaml.cs exception handler ran)
- `%LOCALAPPDATA%\VoiceStudio\crashes\ui_smoke_summary.json` (**Gate C UI smoke summary**)
- `%LOCALAPPDATA%\VoiceStudio\crashes\binding_failures_latest.log` (**binding failure list; should be empty**)
- `%LOCALAPPDATA%\VoiceStudio\crashes\ui_smoke_exception.log` (if the smoke runner throws)

**Optional (highly recommended) native dumps:**

- `%LOCALAPPDATA%\VoiceStudio\dumps\*.dmp` (enabled via WER LocalDumps)

### 1.2 Enable deterministic crash dumps (optional but recommended)

This config is **per-user** (HKCU) and targets `VoiceStudio.App.exe` by default:

```powershell
cd E:\VoiceStudio
.\scripts\enable-wer-localdumps.ps1 -Mode Enable
.\scripts\enable-wer-localdumps.ps1 -Mode Status
```

Expected dump folder:
`%LOCALAPPDATA%\VoiceStudio\dumps`

### 1.3 Publish the Gate C artifact (unpackaged apphost EXE)

Use the repo’s Gate C publish script (this is the canonical Gate C artifact production flow):

```powershell
cd E:\VoiceStudio

# Publish only (recommended when you plan to run UI smoke afterwards)
.\scripts\gatec-publish-launch.ps1 -Configuration Release -SmokeSeconds 10 -NoLaunch
```

Default publish directory if you don’t override it:
`E:\VoiceStudio\.buildlogs\x64\Release\gatec-publish`

### 1.4 Run deterministic UI smoke on the published EXE (no manual clicking required)

VoiceStudio has a built‑in Gate C UI smoke mode:

- Trigger via argument `--smoke-ui` (or `--ui-smoke`)
- Captures binding failures deterministically to `%LOCALAPPDATA%\VoiceStudio\crashes\binding_failures_latest.log`
- Writes summary to `%LOCALAPPDATA%\VoiceStudio\crashes\ui_smoke_summary.json`
- Exits deterministically:
  - `0` = PASS (no binding failures)
  - `1` = FAIL (binding failures detected)
  - `2/3/4` = infra / exception cases (see code for meanings)

Recommended (single command; runs the published EXE with correct WorkingDirectory and copies artifacts into the publish dir):

```powershell
cd E:\VoiceStudio
.\scripts\gatec-publish-launch.ps1 -Configuration Release -RuntimeIdentifier win-x64 -UiSmoke
```

Manual run against the **published artifact** (ensure WorkingDirectory is the publish dir):

```powershell
$publishDir = "E:\VoiceStudio\.buildlogs\x64\Release\gatec-publish"
$exe = Join-Path $publishDir "VoiceStudio.App.exe"

$proc = Start-Process -FilePath $exe -WorkingDirectory $publishDir -ArgumentList "--smoke-ui" -Wait -PassThru
$proc.ExitCode
```

### 1.5 Record the Gate C evidence packet

Update / create the relevant evidence packet:

- `docs/governance/overseer/handoffs/VS-0012.md`

Attach:

- the `.buildlogs\*` artifacts from the publish run
- the `%LOCALAPPDATA%\VoiceStudio\crashes\*` smoke + crash artifacts
- any `%LOCALAPPDATA%\VoiceStudio\dumps\*.dmp` (if present)

Supporting repo docs (useful checklists/specs):

- `docs/governance/overseer/GATE_C_CRASH_LOG_INSTRUMENTATION.md`
- `docs/governance/overseer/GATE_C_UI_SMOKE_TEST.md`
- `docs/governance/overseer/GATE_C_UI_SMOKE_EXECUTION_PLAN.md`

---

## 2) “Next”: close Gate H (VS-0003) with installer lifecycle proof

Gate H is downstream of Gate C. Do not “declare installer readiness” until Gate C is green with evidence.

### 2.1 Build the installer (repo scripts)

The repo supports **Inno Setup** (default) and **WiX**:

```powershell
cd E:\VoiceStudio

# Build installer directly (defaults: InnoSetup, Release)
.\installer\build-installer.ps1 -InstallerType InnoSetup -Configuration Release -Version 1.0.0

# Optional: quick verification (file exists, readable, size sanity)
.\installer\verify-installer.ps1 -ExpectedVersion 1.0.0

# Or run the release prep wrapper (also creates dist package under release\dist)
.\scripts\prepare-release.ps1 -Version 1.0.0
```

Expected outputs:

- `installer\Output\VoiceStudio-Setup-v1.0.0.exe` (Inno Setup)
- `installer\Output\VoiceStudio-Setup-v1.0.0.msi` (WiX)

Reference doc:

- `docs/release/INSTALLER_PREPARATION.md`

### 2.2 Clean VM lifecycle proof matrix (required for VS-0003)

Run on clean VMs (snapshots recommended):

- Windows 11 stable
- Windows 10 stable (if supported)
- Optional: Windows 11 Insider (to mirror dev OS), but **do not** treat Insider-only success as “production proof”

#### A) Install (with deterministic logs)

**If using Inno Setup** (`*.exe`):

```powershell
.\VoiceStudio-Setup-v1.0.0.exe /VERYSILENT /LOG="C:\logs\voicestudio_install.log"
```

**If using WiX/MSI** (`*.msi`):

```powershell
msiexec /i "VoiceStudio-Setup-v1.0.0.msi" /qn /l*v "C:\logs\voicestudio_install.log"
```

#### B) Launch + basic UI/Backend connectivity smoke

Launch from Start Menu or installed path and confirm:

- app reaches UI
- backend starts/contacts as expected (whatever the current product behavior is)
- no crash artifacts are produced unexpectedly

Capture:

- `%LOCALAPPDATA%\VoiceStudio\crashes\*` (if any)
- backend logs (whatever your current backend logging path is)

#### C) Upgrade proof

Install an older version snapshot first, then run the new installer over it.
Capture upgrade logs (`voicestudio_upgrade.log`) and verify post-upgrade launch.

#### D) Rollback proof

Use VM snapshots for rollback (preferred). If you have an installer-level rollback mechanism, capture its logs too.

#### E) Uninstall proof

For Inno Setup installs, the uninstaller is typically `unins*.exe` under the install directory.
Locate it and run with logging (example):

```powershell
$unins = Get-ChildItem "$env:ProgramFiles\VoiceStudio" -Filter "unins*.exe" | Select-Object -First 1 -ExpandProperty FullName
& $unins /VERYSILENT /LOG="C:\logs\voicestudio_uninstall.log"
```

For MSI, uninstall via product code or `msiexec /x` with logging.

### 2.3 Record the Gate H evidence packet

Update / create:

- `docs/governance/overseer/handoffs/VS-0003.md`

Attach:

- installer build artifacts (hashes + output names)
- VM lifecycle logs (`install`, `upgrade`, `uninstall`)
- app crash artifacts (if any)
- notes on OS builds + VM specs used

---

## 3) Compatibility alignment plan (docs vs reality)

Today, there is known doc drift:

- `docs/design/COMPATIBILITY_MATRIX.md` vs pinned reality in:
  - `global.json`
  - `Directory.Build.props`
  - `requirements_engines.txt`
  - `version_lock.json`

Recommended plan:

- **Option A (recommended):** update docs to match pinned reality and ship; perform upgrades later with proof.
- **Option B (high risk):** upgrade pinned reality to match docs; requires full regression + re-proof Gate C/H.

This alignment work should be tracked in the ledger and validated by Build & Tooling + Engine + System Architect.

---

## 4) Role breakdown (7 roles) — what each role delivers

This build plan does not replace role task files; it sequences them.

- **Overseer (Role 0)**:
  - Keep Gate C and Gate H moving; block unrelated work
  - Ensure every claim of “done” has evidence attached (handoffs + logs)
- **System Architect (Role 1)**:
  - Own the compatibility story + what “production ready” means across OS/.NET/WinAppSDK/Python/CUDA
  - Own the “upgrade policy”: what proof is required before changing pins
- **Build & Tooling (Role 2)**:
  - Own deterministic build/publish scripts and proof artifact generation
  - Ensure Gate scripts remain the canonical path (no drift)
- **UI Engineer (Role 3)**:
  - Own Gate C UI smoke pass (including `--smoke-ui` results + binding failures)
  - Fix any remaining binding failures surfaced by smoke
- **Core Platform (Role 4)**:
  - Own app↔backend wiring, diagnostics/logging, and local-first constraints
- **Engine Engineer (Role 5)**:
  - Own engine stability and “quality and functions” upgrades (voice cloning quality, model handling, inference reliability)
- **Release Engineer (Role 6)**:
  - Own installer build + clean VM lifecycle proof (Gate H)

---

## 5) Definition of Done (production ready)

**Gate C (VS-0012) DONE means:**

- Gate C publish script produces artifacts and the app starts reliably
- `--smoke-ui` exits `0` on published artifact (and `ui_smoke_summary.json` is captured)
- Crash/dump evidence is clean or fully explained with fixes

**Gate H (VS-0003) DONE means:**

- Installer lifecycle proof is complete on clean VMs:
  - install → launch → upgrade → rollback → uninstall
  - logs captured for each phase

**Plus:**

- Docs and pins are aligned enough that the team will not “follow two truths”
- Non-negotiables are honored (no MSIX lane, deterministic evidence, no placeholders)

---

## 6) External references (for background, not as a second lane)

- Microsoft Learn — Self-contained deployment (Windows App SDK): `https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps`
- Microsoft Learn — Windows App SDK downloads (runtime installer): `https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads`
- Inno Setup — Setup command line parameters: `https://jrsoftware.org/ishelp/topic_setupcmdline.htm`
