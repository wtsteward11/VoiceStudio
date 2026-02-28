# Build Protection Protocol

**Purpose**: Prevent any agent or process from breaking the VoiceStudio UI build again.  
**Authority**: Tyler (project owner) — only Tyler can authorize exceptions.

---

## 1. Immutable Golden References

| Reference | Location | Purpose |
|-----------|----------|---------|
| Golden commit | `b661e694` | Known-good source, builds and launches |
| Working worktree | `E:\VoiceStudio-feb13` | Built copy, ready to run |
| Installed baseline | `E:\VoiceStudio-baseline` | Separate installed copy |
| Working EXE | `E:\VoiceStudio-feb13\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe` | Launch directly |
| Post-Feb13 backup | `E:\VoiceStudio\docs\archive\pre_restore_20260228` | All work done after Feb 13 |

**These locations MUST NOT be modified or deleted.**

---

## 2. Forbidden Changes (Hard Block — No Exceptions Without Tyler's Approval)

### 2.1 Build Configuration Files

These files control whether the project builds. They MUST NOT be modified:

| File | Rule |
|------|------|
| `VoiceStudio.App.csproj` | No changes to PropertyGroup settings, no package additions |
| `Directory.Build.props` | No changes whatsoever |
| `Directory.Build.targets` | No changes whatsoever |
| `global.json` | No SDK version changes |
| `VoiceStudio.Core.csproj` | No package additions |

### 2.2 Specific Properties That Must NEVER Change

| Property | Required Value | Why |
|----------|---------------|-----|
| `DisableXbfGeneration` | **Commented out** (XBF enabled) | Setting to `true` produces empty `XamlTypeInfo.g.cs` → runtime crash |
| `XamlCompilerExePath` | **Must NOT exist** in `Directory.Build.props` | Wrapper masks compiler failures |
| `WindowsAppSDKSelfContained` | `false` | Setting to `true` without VC++ runtime DLLs → native crash |
| `RuntimeIdentifier` | **Not set** for Debug (Release-only via target) | Adding for Debug puts `win-x64` in paths |
| `MicrosoftWindowsAppSDKVersion` | `1.8.251106002` | Other versions untested |
| SDK version (global.json) | `8.0.417` | Must stay .NET 8 |

### 2.3 NuGet Packages That Must NEVER Be Added

Any package that brings in .NET 9.0+ transitive dependencies will crash the XAML compiler:

| Package | Why Forbidden |
|---------|--------------|
| `Microsoft.Extensions.Hosting` ≥9.0.0 | Pulls in `Configuration.Abstractions` 9.0.0 |
| `Microsoft.Extensions.Configuration.Json` ≥9.0.0 | Direct .NET 9.0 assembly |
| `Microsoft.Extensions.Logging` ≥9.0.0 | Direct .NET 9.0 assembly |
| Any `Microsoft.Extensions.*` ≥9.0.0 | All contain .NET 9.0 metadata |
| `System.Text.Json` ≥9.0.0 | .NET 9.0 metadata |

**Safe versions**: 8.0.x for all `Microsoft.Extensions.*` packages.

**Rule**: Before adding ANY new NuGet package, check its dependency tree. If ANY transitive dependency resolves to ≥9.0.0, it CANNOT be added.

```powershell
# Check before adding:
dotnet add package <PackageName> --version <Version>
dotnet list package --include-transitive | Select-String "9\.0\.|10\.0\."
# If any 9.0+ appear → REMOVE THE PACKAGE IMMEDIATELY
```

---

## 3. Pre-Change Verification (Mandatory Before ANY Code Change)

```powershell
# Step 1: Verify build works BEFORE making changes
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
# MUST show: Build succeeded. 0 Error(s)

# Step 2: Verify XAML compiler output exists
$typeInfo = Get-ChildItem "src\VoiceStudio.App\obj" -Recurse -Filter "XamlTypeInfo.g.cs"
if ($typeInfo.Length -eq 0) { Write-Error "STOP: XamlTypeInfo.g.cs is empty" }

# Step 3: Make your changes

# Step 4: Rebuild and verify
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
# MUST show: Build succeeded. 0 Error(s)

# Step 5: Verify XAML compiler output still exists
$typeInfo = Get-ChildItem "src\VoiceStudio.App\obj" -Recurse -Filter "XamlTypeInfo.g.cs"
if ($typeInfo.Length -eq 0) { Write-Error "REVERT: XamlTypeInfo.g.cs is empty after change" }
```

**If any step fails → REVERT IMMEDIATELY. Do not try to fix forward.**

---

## 4. Post-Change Verification (Mandatory After ANY Code Change)

```powershell
# Launch the app and confirm UI appears
$exe = Get-ChildItem ".buildlogs" -Recurse -Filter "VoiceStudio.App.exe" | Select-Object -First 1
Start-Process $exe.FullName -WorkingDirectory $exe.DirectoryName
# MUST show the MainWindow UI
```

---

## 5. Recovery Procedure (If Build Breaks)

**Do NOT attempt to fix. Restore immediately.**

```powershell
# Option A: Restore from golden commit
git checkout b661e694 -- src/ Directory.Build.props Directory.Build.targets global.json
git clean -fd -- src/
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64

# Option B: Copy from working worktree
Copy-Item "E:\VoiceStudio-feb13\src\*" "E:\VoiceStudio\src\" -Recurse -Force
Copy-Item "E:\VoiceStudio-feb13\Directory.Build.props" "E:\VoiceStudio\" -Force
Copy-Item "E:\VoiceStudio-feb13\Directory.Build.targets" "E:\VoiceStudio\" -Force
Copy-Item "E:\VoiceStudio-feb13\global.json" "E:\VoiceStudio\" -Force
git clean -fd -- src/
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64

# Option C: Just run from the worktree
Start-Process "E:\VoiceStudio-feb13\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"
```

---

## 6. Agent Instructions (Copy-Paste Into Any New Session)

```
MANDATORY BUILD RULES — READ BEFORE ANY ACTION:

1. The working build is at commit b661e694. Do NOT modify VoiceStudio.App.csproj,
   Directory.Build.props, Directory.Build.targets, or global.json without my explicit approval.

2. Do NOT add NuGet packages with .NET 9.0+ dependencies. The XAML compiler (net472)
   crashes silently on .NET 9.0 assembly metadata.

3. Do NOT set DisableXbfGeneration=true. It produces empty XamlTypeInfo.g.cs → runtime crash.

4. Do NOT add XamlCompilerExePath to Directory.Build.props. It masks compiler failures.

5. Build and verify BEFORE and AFTER every change:
   dotnet build VoiceStudio.sln -c Debug -p:Platform=x64

6. If the build breaks, REVERT IMMEDIATELY using:
   git checkout b661e694 -- src/ Directory.Build.props Directory.Build.targets global.json
   git clean -fd -- src/

7. Full documentation at:
   - docs/reports/BUILD_ROOT_CAUSE_ANALYSIS_20260228.md
   - docs/reports/WORKING_BUILD_CONFIG_REFERENCE_20260228.md
   - docs/reports/COMMIT_TIMELINE_AND_BREAKAGE_20260228.md
   - docs/reports/BUILD_PROTECTION_PROTOCOL_20260228.md
```
