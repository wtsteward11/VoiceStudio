# VoiceStudio UI Launch Diagnostic Report

**Date:** 2026-02-27
**Issue:** App builds (0 errors) but no UI window appears at runtime

## Proven Facts (with evidence)

### 1. Installed version at `C:\Program Files\VoiceStudio\App\` WORKS

- **333 XBF files**, 0 XAML files, 4 PRI files (including 615KB VoiceStudio.App.pri)
- Compiled **Feb 2, 2026** from commit `b62e27f8` with **XBF generation ENABLED**
- Copied to `E:\VoiceStudio\.buildlogs\installed_copy\` and launched:
  - **Smoke mode** (`--smoke-ui`): exit code 0 (PASS)
  - **Normal mode**: white screen appears, then crashes in `MainWindow_Activated` at `ApplicationData.Current` (HResult 0x80073D54 = APPMODEL_ERROR_NO_PACKAGE)
- The `ApplicationData.Current` crash is a **known bug in Feb 2 code**, already fixed in HEAD

### 2. Current HEAD build output does NOT work

- **0 XBF files**, 190 XAML files, 1 PRI file (362KB VoiceStudio.App.pri)
- `DisableXbfGeneration=true` in csproj -- XAML compiler Pass 2 skipped
- Crash: `XamlParseException: The type 'XamlControlsResources' was not found`
- Adding WinUI PRI files from installed copy changes error to: `XamlParseException: The text associated with this error code could not be found`

### 3. Current DLL in installed directory does NOT work

- Copied current VoiceStudio.App.dll + deps into installed copy (which has XBF + PRI)
- Crashes with `System.Text.Json 9.0.0 not found` (fixed by copying deps)
- Then crashes with `XamlParseException: The text associated with this error code could not be found`

### 4. XAML compiler cannot produce XBF files

- Pinned to correct version (`1.8.251105000`)
- Produces no `output.json` at all -- exits with code 1
- `LocalAssembly` DLL exists (7.9MB) so that's not the issue
- This is a **known limitation** documented since Dec 2025 (see `XAML_COMPILATION_ISSUE_SUMMARY.md`)

### 5. NuGet cache pollution was a real issue (now fixed)

- `Directory.Build.props` at HEAD had SDK version `1.8.260209005` (wrong)
- Fixed to `1.8.251106002` (correct)
- Polluting packages removed from NuGet cache
- XAML compiler wrapper pinned to `1.8.251105000`

## Root Cause Theory

The DLL compiled with `DisableXbfGeneration=true` generates different `InitializeComponent()` code via `.g.i.cs` (Pass 1) than the DLL compiled with XBF enabled (`.g.cs` from Pass 2). The Pass 1 code uses runtime XAML parsing (`Application.LoadComponent` with `ms-appx:///` URI), which requires `XamlTypeInfo.g.cs` to provide type metadata. But `XamlTypeInfo.g.cs` is 0 bytes when XBF is disabled.

The Feb 2 DLL works because it was compiled with XBF enabled, so its `XamlTypeInfo.g.cs` has the full type metadata baked into the DLL.

## What Needs to Happen

**Option A: Get the XAML compiler to produce XBF files again**
- The compiler worked on Feb 23 (the XBF files in the source tree are dated Feb 26 00:11)
- It currently exits with code 1 and no output
- Need to investigate WHY it fails -- possibly a .NET SDK version issue (8.0.416 vs 8.0.418)

**Option B: Make DisableXbfGeneration=true work at runtime**
- The DLL needs `XamlTypeInfo.g.cs` to have type metadata
- Currently 0 bytes -- Pass 2 doesn't run when XBF is disabled
- Need to generate XamlTypeInfo without XBF, OR use `EnableTypeInfoReflection=true`

**Option C: Use the installed version + cherry-pick the ApplicationData.Current fix**
- The installed Feb 2 DLL works (UI loads in smoke mode)
- Only crashes because `MainWindow_Activated` calls `ApplicationData.Current`
- Current HEAD code already fixes this (`UnpackagedSettingsHelper`)
- Need to compile current code with XBF enabled -- circular dependency with Option A

## Key File Locations

- Working installed copy: `E:\VoiceStudio\.buildlogs\installed_copy\` (906 files)
- Current build output: `E:\VoiceStudio\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\win-x64\`
- XBF files in source tree: `E:\VoiceStudio\src\VoiceStudio.App\**\*.xbf` (190 files, dated Feb 26)
- Session backup (all work): `E:\VoiceStudio\.buildlogs\session_backup_20260227\`
- Orchestrator work backup: `E:\VoiceStudio\.buildlogs\session_backup_20260227\orchestrator_work\` (57 files)

## Critical Config Values (working state)

```
Directory.Build.props:  MicrosoftWindowsAppSDKVersion = 1.8.251106002
VoiceStudio.App.csproj: WindowsAppSDKSelfContained = false
VoiceStudio.App.csproj: DisableXbfGeneration = true (but needs to be false for working DLL)
xaml-compiler-wrapper:  PINNED_VER = 1.8.251105000
NuGet cache:            Only 1.8.251106002 (SDK) and 1.8.251105000 (WinUI)
```

## Build / Launch Commands

```powershell
# Build (succeeds with 0 errors)
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64

# Launch from build output (crashes - no XBF/PRI)
Start-Process "E:\VoiceStudio\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\win-x64\VoiceStudio.App.exe"

# Launch installed copy in smoke mode (WORKS)
Start-Process "E:\VoiceStudio\.buildlogs\installed_copy\VoiceStudio.App.exe" -ArgumentList "--smoke-ui" -WorkingDirectory "E:\VoiceStudio\.buildlogs\installed_copy"
```

## Post-Mortem Reports

- `docs/reports/post_mortem/BUILD_RESTORATION_REPORT_2026-02-23.md` (exact same failure, fixed by restoring Feb 2 gold copies)
- `docs/reports/post_mortem/ERROR_PATTERN_RETROSPECTIVE_2026-02-04.md` (systemic patterns)
- `docs/reports/build/xaml/XAML_COMPILATION_ISSUE_SUMMARY.md` (XAML compiler Pass 2 failure)
- `docs/reports/build/xaml/XAML_COMPILER_ROOT_CAUSE_AND_SOLUTION_2025-01-28.md` (LocalAssembly theory)
