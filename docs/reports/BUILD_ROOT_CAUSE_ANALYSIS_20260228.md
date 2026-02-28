# VoiceStudio Build Failure — Root Cause Analysis

**Date**: 2026-02-28  
**Working Commit**: `b661e694` (Feb 13, 2026)  
**Working Worktree**: `E:\VoiceStudio-feb13`  
**Working Baseline**: `E:\VoiceStudio-baseline`  
**Status**: UI loads and runs from worktree build

---

## 1. Executive Summary

The VoiceStudio UI stopped building and launching due to a single root cause: **NuGet packages targeting .NET 9.0 were added to the project, and the WinUI 3 XAML compiler (which is a .NET Framework 4.7.2 process) silently crashes when it encounters .NET 9.0 assembly metadata in its reference list.**

The fix was to restore from the known-good Feb 13 commit (`b661e694`) which never had those packages.

---

## 2. Root Cause: .NET 9.0 Packages Crash the XAML Compiler

### What Happens

The WinUI 3 build pipeline includes `XamlCompiler.exe` located at:
```
C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk.winui\1.8.251105000\tools\net472\XamlCompiler.exe
```

This compiler is a **.NET Framework 4.7.2** executable. During **Pass 2** (MarkupCompilePass2), it:
1. Reads `input.json` containing all reference assembly paths
2. Loads metadata from each reference assembly
3. Generates XBF files and `XamlTypeInfo.g.cs`

When any reference assembly targets **.NET 9.0**, the compiler's metadata reader encounters assembly format features it cannot parse. It **silently crashes with exit code 1 and zero output** — no stdout, no stderr, no output.json, no error message.

### Proof (Verified During This Session)

| Test | Result |
|------|--------|
| Feb 13 `input.json` (257 refs, all ≤8.0) → compiler | **Exit 0, output.json generated** |
| Current `input.json` (282 refs, includes 9.0) → compiler | **Exit 1, no output** |
| Current input minus 29 new refs → compiler | **Exit 0, output.json generated** |
| Current input plus just `Microsoft.Extensions.Configuration.Abstractions` 9.0.0 → compiler | **Exit 1, no output** |

### The Specific Culprit Package

`Microsoft.Extensions.Configuration.Abstractions` version **9.0.0** was the first confirmed crash trigger. It was pulled in transitively by:

```
Microsoft.Extensions.Hosting 9.0.0
  └─ Microsoft.Extensions.Configuration 9.0.0
       └─ Microsoft.Extensions.Configuration.Abstractions 9.0.0  ← CRASHES COMPILER
```

### The Commit That Introduced It

**`df3873a7` — "feat(di): add Generic Host bootstrap, appsettings.json, and build hardening"**

This commit added these packages to `VoiceStudio.App.csproj`:
- `Microsoft.Extensions.Hosting` 9.0.0
- `Microsoft.Extensions.Logging` 9.0.0
- `Microsoft.Extensions.Configuration.Json` 9.0.0
- `Microsoft.Extensions.DependencyInjection` 10.0.2

These brought in 29 new transitive reference assemblies, all targeting .NET 9.0+.

---

## 3. Cascade of Misdiagnoses

Because the compiler crashes **silently** (no error output), the actual cause was obscured. This led to a chain of incorrect fixes that each made things worse:

| Attempt | What Was Tried | Why It Failed |
|---------|---------------|---------------|
| 1 | Set `DisableXbfGeneration=true` | Masks compiler crash but produces empty `XamlTypeInfo.g.cs` → runtime `XamlParseException` ("CustomizableToolbar not found") |
| 2 | Add `xaml-compiler-wrapper.cmd` via `XamlCompilerExePath` | Wrapper generates synthetic `output.json` with empty type info → same runtime crash |
| 3 | Set `EnableTypeInfoReflection=true` | NuGet targets override it back to `false`; even when forced, empty `XamlTypeInfo.g.cs` has no types to reflect |
| 4 | Set `WindowsAppSDKSelfContained=true` | Missing VC++ runtime DLLs → `STATUS_DLL_INIT_FAILED` native crash |
| 5 | Add `CopyVCRuntimeDlls` target | Fixed DLL crash but underlying XAML type info still empty |
| 6 | Override `GenXbfPath` to `tools\x64\` | GenXbf.dll found but compiler still crashes (wrong root cause) |
| 7 | Change `EnsureRuntimeIdentifierForWin2D` condition | Removed `win-x64` from path but compiler still crashes (wrong root cause) |
| 8 | Add `AppendRuntimeIdentifierToOutputPath=false` | Cleaner path but compiler still crashes (wrong root cause) |
| 9 | Downgrade packages to 8.0.x | Correct direction but incomplete — transitive dependencies, NuGet cache locks |

**Every attempt except #9 was treating symptoms, not the root cause.**

---

## 4. The Working Build Configuration

### Golden Commit: `b661e694`

This commit has:
- **0 build errors**
- **XAML compiler Pass 1 and Pass 2 succeed** (exit code 0)
- **XBF files generated** properly
- **`XamlTypeInfo.g.cs` populated** with all custom types
- **UI launches** and displays MainWindow

### Key Settings (DO NOT CHANGE)

**`VoiceStudio.App.csproj`**:
- `DisableXbfGeneration` — **commented out** (XBF generation ENABLED)
- `UseXamlCompilerExecutable` — `false` in csproj (overridden to `true` by `Directory.Build.targets`)
- `WindowsAppSDKSelfContained` — `false`
- `RuntimeIdentifier` — NOT set explicitly (only set for Release via `EnsureRuntimeIdentifierForWin2D` target)
- `EnableDefaultRuntimeIdentifier` — `true`
- **NO** `XamlCompilerExePath` — compiler runs directly, NOT through wrapper
- **NO** `Microsoft.Extensions.Hosting` package
- **NO** `Microsoft.Extensions.Configuration.Json` package
- Packages: `Microsoft.Extensions.DependencyInjection` 10.0.2, `Microsoft.Extensions.Logging.Abstractions` 9.0.0

**`Directory.Build.props`**:
- `MicrosoftWindowsAppSDKVersion` — `1.8.251106002`
- `UseXamlCompilerExecutable` — `true` (for wrapper routing, but NO `XamlCompilerExePath`)
- **NO** `XamlCompilerExePath` line

**`Directory.Build.targets`**:
- `UseXamlCompilerExecutable` — `true`
- `XAMLFingerprint` — `false`
- `EnableWin32Codegen` — `false`
- `UseVCMetaManaged` — `false`
- **NO** `GenXbfPath` override
- **NO** `EnableTypeInfoReflection` override
- Contains `EnsureXamlInObj`, `EnsureXamlInObjBeforeCopyToOutput`, `ValidateXamlFilesPresent`, `DetectNestedViewsXaml` targets

**`global.json`**:
- SDK version `8.0.417`

**`VoiceStudio.Core.csproj`**:
- Only package: `Microsoft.Extensions.Logging.Abstractions` 9.0.0
- **NO** `JsonSchema.Net`, **NO** `Microsoft.Extensions.Hosting`

---

## 5. What Must NEVER Be Done Again

1. **NEVER add .NET 9.0+ NuGet packages** to any project in the solution until WinUI upgrades its XAML compiler from net472. The compiler WILL silently crash.

2. **NEVER set `DisableXbfGeneration=true`** — it produces an empty type registry that crashes the app at runtime.

3. **NEVER add `XamlCompilerExePath`** pointing to the wrapper — it masks compiler failures and produces empty type info.

4. **NEVER change `WindowsAppSDKSelfContained`** without verifying VC++ runtime DLLs are present.

5. **NEVER change `RuntimeIdentifier`** for Debug builds — it adds `win-x64` to the intermediate path.

6. **NEVER upgrade `MicrosoftWindowsAppSDKVersion`** without testing that `XamlCompiler.exe` Pass 2 exits 0.

---

## 6. How to Verify a Build Is Healthy

```powershell
# 1. Build
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64

# 2. Check XamlTypeInfo.g.cs is NOT empty
$typeInfo = Get-ChildItem "src\VoiceStudio.App\obj" -Recurse -Filter "XamlTypeInfo.g.cs"
$typeInfo | ForEach-Object { Write-Host "$($_.Length) bytes: $($_.FullName)" }
# MUST be > 0 bytes

# 3. Check XBF files exist
$xbf = Get-ChildItem "src\VoiceStudio.App\obj" -Recurse -Filter "*.xbf"
Write-Host "$($xbf.Count) XBF files"
# MUST be > 0

# 4. Launch
$exe = Get-ChildItem ".buildlogs" -Recurse -Filter "VoiceStudio.App.exe" | Select-Object -First 1
Start-Process $exe.FullName -WorkingDirectory $exe.DirectoryName
```

---

## 7. Recovery Locations

| Location | What It Is | Status |
|----------|-----------|--------|
| `E:\VoiceStudio-feb13` | Git worktree at commit `b661e694` | **BUILT & LAUNCHES** |
| `E:\VoiceStudio-baseline` | Installed baseline copy | Working |
| Git commit `b661e694` | Golden commit hash | Immutable in git history |
| `docs\archive\pre_restore_20260228` | Backup of all post-Feb-13 work (40 new + 271 modified files) | Saved |
