# GOV-VOICESTUDIO-GAP010-MICA-TITLEBAR-INTEGRATION-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP010-MICA-TITLEBAR-INTEGRATION-01 |
| **GAP** | GAP-010 (Mica / SystemBackdrop + custom title bar) |
| **Status** | **Closed** (2026-04-09) |
| **Phase** | Independent of GAP-008 (advisory ordering only) |
| **Role** | UI Engineer |
| **Dependency** | GAP-008 MainWindow decomposition — **advisory**; lane bounded to shell row + Loaded wiring only |

## §1 Objective (frozen)

Wire **`MaterialsHelper`** (Mica / Desktop Acrylic controllers) to **`MainWindow`** from a **single Loaded-path** (ADR-047), add **custom title bar** (`ExtendsContentIntoTitleBar`, `SetTitleBar`, `AppWindow.TitleBar` colors), **transparent `RootGrid`** when material applies, **gradient fallback** when unsupported, and **theme sync** via **`IUnifiedThemeService.ThemeChanged`**.

## §2 Hard IN

- **`MaterialsHelper.ApplyMaterial`** from **`MainWindow` content `Loaded`** only (not constructor).
- Custom title bar: **`ExtendsContentIntoTitleBar = true`**, **`SetTitleBar(AppTitleBar)`** (drag region).
- XAML: first row **~32px** with app chrome (icon, title, drag **`Border`** `AppTitleBar`); existing rows shifted **+1**.
- **`RootGrid.Background`** → **Transparent** when Mica/Acrylic applies; retain **`VSQ.Window.Background`** when **`MaterialType.None`** or apply fails.
- Fallback matrix: Win11 22H2+ Mica; Win10 21H1+ Desktop Acrylic where supported; older = gradient + custom title bar still on.
- **`ThemeChanged`:** refresh **`AppWindow.TitleBar`** button colors; **`MaterialsHelper.RefreshSystemBackdropTheme()`** when dictionary swap may not raise **`ActualThemeChanged`**.
- Tests: **`ShellBackdropTitleBarSeamTests`** (capability, cleanup, constructor-safety scan).

## §3 Hard OUT

- MainWindow decomposition (GAP-008), workspace persistence refactor, panel lifecycle, navigation rail surgery.
- Settings UI for backdrop material selection.
- Acrylic for dialogs / in-app panels (separate lanes).

## §4 Authority map

| Concern | Owner |
|--------|--------|
| Backdrop controllers | `MaterialsHelper` (no router edits) |
| Init timing | `MainWindow` content **`Loaded`** only |
| Title bar chrome | `MainWindow.xaml` row 0 + code-behind |
| Theme / title bar colors | `IUnifiedThemeService` + `AppWindow.TitleBar` |

## §5 Acceptance criteria

- [x] `ApplyMicaBackdrop` + `InitializeCustomTitleBar` run only from **`Loaded`** (verified by test + code review).
- [x] `RootGrid` transparent when material succeeds; gradient retained otherwise.
- [x] `SetTitleBar` + `AppWindow.TitleBar` colors coherent for light/dark; updated on **`ThemeChanged`**.
- [x] `MaterialsHelper.RefreshSystemBackdropTheme()` available for explicit theme sync after resource swap.
- [x] MSTest **`ShellBackdropTitleBarSeamTests`** present and passing.
- [x] Build + App.Tests + `verify.ps1 -Quick` GREEN; closure report + tracker + STATE + registry.

## §6 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ShellBackdropTitleBarSeamTests"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
```

## §7 Risk register (summary)

| Risk | Mitigation |
|------|------------|
| `ActualThemeChanged` not firing on merged-dictionary swap | Explicit **`RefreshSystemBackdropTheme`** from **`ThemeChanged`** |
| Transparent root without material | Only set transparent on successful Mica/Acrylic apply |

## §8 Rollback order

1. `MainWindow.xaml` row indices + title bar row  
2. `MainWindow` code-behind / partial shell init  
3. `MaterialsHelper` public API additions  
4. Tests + governance artifacts  
