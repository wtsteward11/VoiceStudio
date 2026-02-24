# Build Restoration Report — 2026-02-23

## Executive Summary

VoiceStudio's WinUI 3 desktop application stopped building and launching due to two root causes:

1. **NuGet cache pollution**: A newer Windows App SDK (`1.8.260209005`) was pulled into the local NuGet cache, and the XAML compiler wrapper (`tools/xaml-compiler-wrapper.cmd`) always selects the *latest* cached version — not the version pinned in the project. This caused an incompatible XAML compiler to silently produce failures.

2. **Build file mutations**: Previous AI debugging sessions modified `Directory.Build.props`, `Directory.Build.targets`, `VoiceStudio.App.csproj`, `App.xaml`, `App.xaml.cs`, and `Program.cs` away from their known-working state. These changes included disabling XAML compilation, stripping resource dictionaries, disabling the app constructor logic, and bumping the SDK version pin.

The fix was to restore the exact build configuration files from the verified-working February 2, 2026 build (confirmed by Gate C smoke test with 11 successful navigation steps and zero binding failures) and remove the incompatible NuGet packages from the local cache.

---

## Timeline

| Date | Event |
|---|---|
| **2026-01-30** | `v1.0.0-baseline` tag created. App builds and runs. SDK pinned to `1.8.251106002`. |
| **2026-02-02 08:07** | Gate C UI smoke test **PASSES**: exit code 0, 11 nav steps, 0 binding failures. Working build artifacts saved to `.buildlogs/VoiceStudio.App.csproj.feb2_working` and `.buildlogs/Directory.Build.targets.feb2_working`. |
| **2026-02-02** | Build installed to `C:\Program Files\VoiceStudio\App\`. |
| **2026-02-05** | `v1.0.1` tag released. |
| **2026-02-13** | Original NuGet SDK packages (`1.8.251106002`) re-downloaded (cache refresh). |
| **2026-02-18** | `v1.0.2-rc1` tag. |
| **2026-02-22** | AI debugging sessions begin. `Directory.Build.props` SDK version changed from `1.8.251106002` to `1.8.260209005`. NuGet pulls newer packages into cache. |
| **2026-02-22 – 02-23** | Multiple AI sessions modify build files, strip `App.xaml` resource dictionaries, disable app constructor, toggle XBF generation, add/remove MSBuild targets. Each change compounds the problem. |
| **2026-02-23 18:49** | Build restored. App launches with visible UI (`MainWindowTitle: 'VoiceStudio Quantum+'`). |

---

## Root Cause Analysis

### Root Cause 1: NuGet Cache Pollution

**What happened**: `Directory.Build.props` was changed to pin SDK version `1.8.260209005` (February 2026 release) instead of `1.8.251106002` (November 2025 release). NuGet `restore` downloaded the newer packages into the global cache at `C:\Users\Tyler\.nuget\packages\`.

**Why it broke things**: The XAML compiler wrapper (`tools/xaml-compiler-wrapper.cmd`) selects the compiler by iterating over ALL versions in the NuGet cache and picking the **last one** (alphabetically latest):

```cmd
rem Line 58 of tools/xaml-compiler-wrapper.cmd
for /f "delims=" %%v in ('dir /b /ad "%NUGET_ROOT%\microsoft.windowsappsdk.winui" 2^>nul ^| sort') do (
  ...
  set "COMPILER=..."
)
```

Once `1.8.260204000` existed alongside `1.8.251105000`, the wrapper always used the newer, incompatible compiler — even when building code pinned to the older version.

**Why it was hard to diagnose**: The build output showed the correct targets file (`1.8.251105000`) but the wrong compiler (`1.8.260204000`). The version mismatch was subtle and not immediately visible in error messages. The compiler simply failed with exit code 1 and no output.

### Root Cause 2: Build File Mutations

Previous debugging sessions modified these files away from their working state:

| File | What Changed | Impact |
|---|---|---|
| `Directory.Build.props` | SDK version `1.8.251106002` → `1.8.260209005`; `UseXamlCompilerExecutable` changed; wrapper path removed; duplicate comment blocks added | Wrong SDK version pulled; compiler routing broken |
| `Directory.Build.targets` | `EnsureXamlInObj` targets DISABLED (renamed to `_DISABLED`, `Condition="false"`); `MarkupCompilePass2` skip added; new targets added (`DetectNestedViewsXaml`, `RemoveXbfFromCopyList`, etc.) | XAML files not copied to obj; build pipeline broken |
| `VoiceStudio.App.csproj` | `SelfContained` made conditional on Configuration; `EnsureRuntimeIdentifierForWin2D` made conditional on Release; `EnableUIXamlCompilation` uncommented | Build behavior different between Debug/Release; XAML compilation inconsistent |
| `App.xaml` | Resource dictionaries stripped to only `XamlControlsResources` | `VSQ.Window.Background` and all custom design tokens missing at runtime |
| `App.xaml.cs` | Constructor stripped with `// TEMP DIAG` comments; `if (false && ...)` guards; `WriteDiagMarker` calls added; `ServiceProvider.Initialize()` disabled | App constructor did nothing; no services initialized |
| `Program.cs` | Bootstrap logic modified with conditional `IsSelfContained()` checks; diagnostic markers added | Bootstrap initialization inconsistent |

---

## Exact Fix Applied

### Step 1: Remove Polluting NuGet Packages

Removed newer SDK versions from the NuGet global cache. These are downloaded caches, not project files — NuGet re-downloads them on demand if needed.

```powershell
# Packages removed from C:\Users\Tyler\.nuget\packages\
Remove-Item -Recurse -Force "C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk\1.8.260209005"
Remove-Item -Recurse -Force "C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk\1.7.260208005"
Remove-Item -Recurse -Force "C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk.winui\1.8.260204000"
Remove-Item -Recurse -Force "C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk.foundation\1.8.260203002"
Remove-Item -Recurse -Force "C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk.base\1.8.251216001"
Remove-Item -Recurse -Force "C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk.runtime\1.8.260209005"
Remove-Item -Recurse -Force "C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk.ai\1.8.47"
Remove-Item -Recurse -Force "C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk.dwrite\1.8.25122902"
Remove-Item -Recurse -Force "C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk.interactiveexperiences\1.8.260125001"
Remove-Item -Recurse -Force "C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk.ml\1.8.2124"
Remove-Item -Recurse -Force "C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk.widgets\1.8.251231004"
```

**Remaining (correct) versions after cleanup:**

| Package | Version |
|---|---|
| `microsoft.windowsappsdk` | `1.8.251106002` |
| `microsoft.windowsappsdk.winui` | `1.8.251105000` |
| `microsoft.windowsappsdk.foundation` | `1.8.251104000` |
| `microsoft.windowsappsdk.base` | `1.8.250831001` |
| `microsoft.windowsappsdk.runtime` | `1.8.251106002` |
| `microsoft.windowsappsdk.ai` | `1.8.39` |
| `microsoft.windowsappsdk.dwrite` | `1.8.25090401` |
| `microsoft.windowsappsdk.interactiveexperiences` | `1.8.251104001` |
| `microsoft.windowsappsdk.ml` | `1.8.2109` |
| `microsoft.windowsappsdk.widgets` | `1.8.250904007` |

### Step 2: Restore Working Build Configuration Files

Source: Files saved during the verified-working Feb 2, 2026 Gate C smoke test, plus the Feb 2 commit `b62e27f8`.

| File | Source | Restored From |
|---|---|---|
| `Directory.Build.props` | Reverted SDK version to `1.8.251106002`; restored `UseXamlCompilerExecutable=false` and `XamlCompilerExePath` wrapper path | Manual edit (matching baseline) |
| `Directory.Build.targets` | `.buildlogs/Directory.Build.targets.feb2_working` | Gate C artifact |
| `VoiceStudio.App.csproj` | `.buildlogs/VoiceStudio.App.csproj.feb2_working` | Gate C artifact |
| `App.xaml` | `git show b62e27f8:src/VoiceStudio.App/App.xaml` | Git commit b62e27f8 |
| `App.xaml.cs` | `git show b62e27f8:src/VoiceStudio.App/App.xaml.cs` | Git commit b62e27f8 |
| `Program.cs` | `git show b62e27f8:src/VoiceStudio.App/Program.cs` | Git commit b62e27f8 |

### Step 3: Clean Build

```powershell
Remove-Item -Recurse -Force "E:\VoiceStudio\src\VoiceStudio.App\obj"
Remove-Item -Recurse -Force "E:\VoiceStudio\src\VoiceStudio.App\bin"
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
```

Result: **0 errors**, 410 warnings. App launches with visible UI.

---

## Working Configuration (Critical Values)

### Directory.Build.props

```xml
<MicrosoftWindowsAppSDKVersion>1.8.251106002</MicrosoftWindowsAppSDKVersion>

<PropertyGroup Condition="'$(MSBuildProjectName)' == 'VoiceStudio.App'">
  <UseXamlCompilerExecutable>false</UseXamlCompilerExecutable>
  <XamlCompilerExePath>$(MSBuildThisFileDirectory)tools\xaml-compiler-wrapper.cmd</XamlCompilerExePath>
</PropertyGroup>
```

### Directory.Build.targets

```xml
<PropertyGroup Condition="'$(MSBuildProjectName)' == 'VoiceStudio.App'">
  <UseXamlCompilerExecutable>true</UseXamlCompilerExecutable>
  <XAMLFingerprint>false</XAMLFingerprint>
  <EnableWin32Codegen>false</EnableWin32Codegen>
  <UseVCMetaManaged>false</UseVCMetaManaged>
  <MarkupCompilePass1DependsOn>ResolveReferences;$(MarkupCompilePass1DependsOn)</MarkupCompilePass1DependsOn>
</PropertyGroup>
```

Key targets that MUST be active (not disabled):
- `EnsureXamlInObj` — copies XAML files to obj before MarkupCompilePass1
- `EnsureXamlInObjBeforeCopyToOutput` — re-copies before output copy
- `ValidateXamlFilesPresent` — validates XAML files exist in obj

### VoiceStudio.App.csproj

```xml
<WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
<SelfContained>true</SelfContained>
<DisableXbfGeneration> <!-- COMMENTED OUT (XBF enabled) -->
<EnableUIXamlCompilation> <!-- COMMENTED OUT (compilation enabled) -->
<UseXamlCompilerExecutable>false</UseXamlCompilerExecutable>
```

### App.xaml

Must include all resource dictionaries:

```xml
<ResourceDictionary.MergedDictionaries>
  <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
  <ResourceDictionary Source="ms-appx:///Resources/DesignTokens.xaml" />
  <ResourceDictionary Source="ms-appx:///Resources/Styles/Controls.xaml" />
  <ResourceDictionary Source="ms-appx:///Resources/Styles/Text.xaml" />
  <ResourceDictionary Source="ms-appx:///Resources/Styles/Panels.xaml" />
</ResourceDictionary.MergedDictionaries>
```

---

## How to Restore From Scratch

If the build ever breaks again, follow these steps in order:

### 1. Verify NuGet Cache

```powershell
# Only ONE version of each package should exist
Get-ChildItem "C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk*" -Directory |
  ForEach-Object { "$($_.Name): $((Get-ChildItem $_.FullName -Directory).Name -join ', ')" }
```

Expected output — one version per package. If multiple versions exist, remove all except:
- `microsoft.windowsappsdk`: `1.8.251106002`
- `microsoft.windowsappsdk.winui`: `1.8.251105000`
- `microsoft.windowsappsdk.foundation`: `1.8.251104000`
- `microsoft.windowsappsdk.runtime`: `1.8.251106002`

### 2. Restore Working Build Files

```powershell
# From saved Gate C artifacts
Copy-Item ".buildlogs\VoiceStudio.App.csproj.feb2_working" "src\VoiceStudio.App\VoiceStudio.App.csproj"
Copy-Item ".buildlogs\Directory.Build.targets.feb2_working" "Directory.Build.targets"

# From git commit b62e27f8 (Feb 2, 2026 — last verified-working state)
git show b62e27f8:src/VoiceStudio.App/App.xaml > "src\VoiceStudio.App\App.xaml"
git show b62e27f8:src/VoiceStudio.App/App.xaml.cs > "src\VoiceStudio.App\App.xaml.cs"
git show b62e27f8:src/VoiceStudio.App/Program.cs > "src\VoiceStudio.App\Program.cs"

# Directory.Build.props — ensure SDK version is correct
# MicrosoftWindowsAppSDKVersion must be 1.8.251106002
```

### 3. Clean and Build

```powershell
Remove-Item -Recurse -Force "src\VoiceStudio.App\obj"
Remove-Item -Recurse -Force "src\VoiceStudio.App\bin"
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
```

### 4. Verify Launch

```powershell
$exe = ".buildlogs\x64\Debug\net8.0-windows10.0.19041.0\win-x64\VoiceStudio.App.exe"
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 10
Write-Host "Title: $($proc.MainWindowTitle)"  # Should be "VoiceStudio Quantum+"
Write-Host "Handle: $($proc.MainWindowHandle)" # Should be non-zero
Write-Host "Responding: $($proc.Responding)"    # Should be True
```

---

## Prevention Rules

### NEVER Do These

1. **NEVER change `MicrosoftWindowsAppSDKVersion` in `Directory.Build.props`** without rebuilding from clean and verifying launch. The SDK version is `1.8.251106002`. Do not bump it.

2. **NEVER run `dotnet restore` with a different SDK version** — it pollutes the NuGet cache and the XAML compiler wrapper picks up the wrong version.

3. **NEVER disable `EnsureXamlInObj` targets** in `Directory.Build.targets` — XAML compilation silently fails with 0 files.

4. **NEVER strip resource dictionaries from `App.xaml`** — the app launches but shows no UI (missing `VSQ.*` design tokens).

5. **NEVER disable the `App` constructor logic** (`ServiceProvider.Initialize()`, backend auto-start, binding failure logging) — the app runs but has no services.

6. **NEVER set `DisableXbfGeneration=true`** — the Feb 2 working build has it commented out (XBF generation enabled).

### ALWAYS Do These

1. **ALWAYS back up build files before modifying them** — copy to `.buildlogs/filename.backup_YYYYMMDD`.

2. **ALWAYS clean obj/bin after changing build configuration** — stale cached artifacts cause misleading failures.

3. **ALWAYS verify launch after build changes** — a successful `dotnet build` does NOT mean the app launches. Check for `MainWindowHandle != 0`.

4. **ALWAYS check NuGet cache if the XAML compiler fails** — run the verification command in section "How to Restore" step 1.

---

## Backup File Inventory

All current-state files were backed up before restoration:

| Backup File | Location |
|---|---|
| `VoiceStudio.App.csproj.backup_20260223` | `src/VoiceStudio.App/` |
| `App.xaml.backup_20260223` | `src/VoiceStudio.App/` |
| `App.xaml.cs.backup_20260223` | `src/VoiceStudio.App/` |
| `Program.cs.backup_20260223` | `src/VoiceStudio.App/` |
| `Directory.Build.targets.backup_20260223` | Repository root |
| `Directory.Build.props.backup_20260223` | Repository root |

## Working Build Artifacts (Gold Copies)

These are the verified-working files that should NEVER be deleted:

| File | Location | Source |
|---|---|---|
| `VoiceStudio.App.csproj.feb2_working` | `.buildlogs/` | Gate C — Feb 2, 2026 |
| `Directory.Build.targets.feb2_working` | `.buildlogs/` | Gate C — Feb 2, 2026 |
| `App.xaml.feb2_working` | `.buildlogs/` | Git commit `b62e27f8` |
| `App.xaml.cs.feb2_working` | `.buildlogs/` | Git commit `b62e27f8` |
| `Program.cs.feb2_working` | `.buildlogs/` | Git commit `b62e27f8` |
| `App.xaml.baseline` | `.buildlogs/` | Git tag `v1.0.0-baseline` |
| `MainWindow.xaml.baseline` | `.buildlogs/` | Git tag `v1.0.0-baseline` |
| `App.xaml.cs.baseline` | `.buildlogs/` | Git tag `v1.0.0-baseline` |

## Key Git References

| Reference | Hash | Date | Significance |
|---|---|---|---|
| `v1.0.0-baseline` tag | `4c607ce0` | 2026-01-30 | First stable baseline |
| Feb 2 working commit | `b62e27f8` | 2026-02-02 | Last verified-working launch (Gate C smoke pass) |
| `v1.0.1` tag | — | 2026-02-05 | Production release |

## Worktree

A git worktree of the `v1.0.0-baseline` was created at `E:\VoiceStudio-baseline\` for comparison. It can be removed with:

```powershell
git worktree remove E:\VoiceStudio-baseline
```

---

*Report generated: 2026-02-23. Build verified launching with visible UI at 18:49 CST.*
