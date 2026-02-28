# DO NOT CHANGE — Build Configuration Reference

**Date captured**: 2026-02-28
**State**: Build succeeds (0 errors), UI launches

Any AI agent or developer that modifies these values will break the build and/or UI launch.

---

## global.json

```json
{
  "sdk": {
    "version": "8.0.417"
  }
}
```

- Do NOT add `rollForward`, `allowPrerelease`, or change the version
- SDK 8.0.418 is installed; the default rollForward policy resolves 8.0.417 → 8.0.418
- Adding `"rollForward": "disable"` WILL BREAK THE BUILD

---

## VoiceStudio.sln

- 3 projects ONLY: `VoiceStudio.Core`, `VoiceStudio.App`, `VoiceStudio.App.Tests`
- Do NOT add or remove projects (UITests was removed intentionally)

---

## Directory.Build.props

| Property | Value | DO NOT |
|---|---|---|
| `UseXamlCompilerExecutable` | `true` (for VoiceStudio.App) | Set to `false` |
| `MicrosoftWindowsAppSDKVersion` | `1.8.251106002` | Change version |

### Pinned package versions (in Directory.Build.props)

| Package | Version |
|---|---|
| `Microsoft.WindowsAppSDK` | `1.8.251106002` |
| `CommunityToolkit.WinUI.UI.Controls` | `7.1.2` |
| `CommunityToolkit.Mvvm` | `8.2.2` |
| `NAudio` | `2.2.1` |
| `Microsoft.Windows.SDK.BuildTools` | `10.0.26100.4654` |

### DO NOT add to this file

- `XamlCompilerExePath` — this routed the compiler through a wrapper that masked failures
- `DisableXbfGeneration` — belongs nowhere; must stay commented out in csproj
- `EnableTypeInfoReflection` — has no effect due to NuGet target override order

---

## Directory.Build.targets

| Property | Value | DO NOT |
|---|---|---|
| `UseXamlCompilerExecutable` | `true` | Set to `false` |
| `XAMLFingerprint` | `false` | Change |
| `EnableWin32Codegen` | `false` | Change |
| `UseVCMetaManaged` | `false` | Change |

### DO NOT add to this file

- `GenXbfPath` — causes path resolution failures
- `EnableTypeInfoReflection` — overridden by NuGet targets anyway
- `DisableXbfGeneration` — kills XBF and type registry

---

## VoiceStudio.App.csproj

### Critical properties (DO NOT CHANGE)

| Property | Value | Why |
|---|---|---|
| `TargetFramework` | `net8.0-windows10.0.19041.0` | WinUI 3 requirement |
| `UseWinUI` | `true` | WinUI 3 requirement |
| `WindowsAppSDKSelfContained` | `false` | Uses system runtime; avoids CoreMessagingXP conflicts |
| `SelfContained` (Debug) | `false` | Avoids needing runtime packages during dev |
| `SelfContained` (Release) | `true` | Standalone deployment |
| `UseXamlCompilerExecutable` | `false` | Overridden to `true` by Directory.Build.targets |
| `EnableWin32Codegen` | `false` | Avoids VC/meta dependency |
| `UseVCMetaManaged` | `false` | Avoids VC/meta dependency |
| `EnableDefaultRuntimeIdentifier` | `true` | Win2D needs this |
| `WindowsPackageType` | `None` | Unpackaged app |
| `BaseOutputPath` | `$(SolutionDir).buildlogs\` | Output goes to .buildlogs/ |
| `DISABLE_XAML_GENERATED_MAIN` | defined | Custom Program.cs entry point |
| `WindowsSdkPackageVersion` | `10.0.26100.81` | WinRT reference assemblies |

### COMMENTED OUT (must stay commented out)

```xml
<!-- <DisableXbfGeneration>true</DisableXbfGeneration> -->
<!-- <EnableUIXamlCompilation>false</EnableUIXamlCompilation> -->
<!-- <RuntimeIdentifier>win-x64</RuntimeIdentifier> -->
<!-- <RuntimeIdentifiers>win-x64</RuntimeIdentifiers> -->
```

Uncommenting ANY of these will break the build or UI launch.

### EnsureRuntimeIdentifierForWin2D target

- Condition: `'$(RuntimeIdentifier)' == '' And '$(Configuration)' == 'Release'`
- ONLY applies to Release builds
- Do NOT change the condition to apply to Debug builds

### Package references (exact versions, DO NOT CHANGE)

| Package | Version |
|---|---|
| `JsonSchema.Net` | `8.0.5` |
| `MessagePack` | `3.1.4` |
| `Microsoft.Extensions.DependencyInjection` | `10.0.2` |
| `Microsoft.Extensions.Logging.Abstractions` | `9.0.0` |
| `Microsoft.Graphics.Win2D` | `1.3.2` |
| `Microsoft.WindowsAppSDK` | `$(MicrosoftWindowsAppSDKVersion)` |
| `Microsoft.Windows.SDK.BuildTools` | `10.0.26100.4654` |
| `System.Security.Permissions` | `6.0.0` |
| `CommunityToolkit.WinUI.UI.Controls` | `7.1.2` |
| `CommunityToolkit.Mvvm` | `8.2.2` |
| `NAudio` | `2.2.1` |
| `Microsoft.Data.Sqlite` | `9.0.1` |
| `Roslynator.Analyzers` | `4.11.0` |

---

## VoiceStudio.Core.csproj

| Property/Package | Value |
|---|---|
| `TargetFramework` | `net8.0` |
| `Microsoft.Extensions.Logging.Abstractions` | `9.0.0` |

---

## Build Command

```
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
```

Must exit with 0 errors.

## EXE Location

```
E:\VoiceStudio\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe
```

Must launch and show the UI window.

---

## Summary of things that HAVE broken the build in the past

| Change | Result |
|---|---|
| Adding `rollForward: disable` to global.json | SDK not found, nothing builds |
| Uncommenting `DisableXbfGeneration=true` | Empty XamlTypeInfo.g.cs, runtime XamlParseException |
| Adding `XamlCompilerExePath` to Directory.Build.props | Routed compiler through wrapper that masked crashes |
| Adding `GenXbfPath` override to Directory.Build.targets | Wrong path, compiler crashes silently |
| Upgrading Microsoft.Extensions.* to 9.0+ | net472 XAML compiler crashes (already present, don't add MORE) |
| Changing EnsureRuntimeIdentifierForWin2D to all configs | win-x64 in Debug output path breaks XAML compiler |
| Adding UITests project to solution | Appium API incompatibility errors |
| Removing backend/services/*.py files | Python tests fail with ModuleNotFoundError |
