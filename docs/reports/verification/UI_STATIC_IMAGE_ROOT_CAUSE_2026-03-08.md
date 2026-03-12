# VoiceStudio "Static Image" UI Bug — Root Cause Report

**Date**: 2026-03-08
**Commit**: HEAD of `main` branch
**Reporter**: Automated diagnostic analysis
**Severity**: P0 — App boots but 0% of panel content renders

---

## 1. Symptom

The WinUI 3 app launches and renders the **shell** correctly:
- Menu bar (File, Edit, View, Modules, Playback, Tools, AI, Help) — visible and clickable
- Toolbar (Loop, Project, Import, Engine, Undo, Redo, Workspace) — visible
- NavRail (8 icon buttons on the left) — visible
- Status bar (Ready, Job: Idle, CPU/GPU/Latency) — visible
- Toast notifications — working ("Settings loaded successfully", etc.)
- Four PanelHost **headers** — visible ("Voice Profiles", "Timeline", "Effects & Mixer", "Macros")

**But all four panel CONTENT areas are completely empty/black.** No panel UI renders inside the PanelHost bodies. Clicking nav buttons does nothing because every panel creation fails silently.

---

## 2. Root Cause (Confirmed via Diagnostic Logs)

**Every ViewModel fails to resolve from DI because ViewModels are not registered in the service container.**

### Diagnostic evidence from `%LOCALAPPDATA%\VoiceStudio\crashes\startup_diag.txt`:

```
PanelHost.CreatePanel failed: panelId=Profiles
  System.InvalidOperationException: No service for type 'VoiceStudio.App.Views.Panels.ProfilesViewModel' has been registered.

PanelHost.CreatePanel failed: panelId=Timeline
  System.InvalidOperationException: No service for type 'VoiceStudio.App.Views.Panels.TimelineViewModel' has been registered.

PanelHost.CreatePanel failed: panelId=EffectsMixer
  System.InvalidOperationException: No service for type 'VoiceStudio.App.Views.Panels.EffectsMixerViewModel' has been registered.

PanelHost.CreatePanel failed: panelId=Macro
  System.InvalidOperationException: No service for type 'VoiceStudio.App.Views.Panels.MacroViewModel' has been registered.
```

Also fails for nav-clicked panels:
```
PanelHost.CreatePanel failed: panelId=Analyzer
  No service for type 'VoiceStudio.App.Views.Panels.AnalyzerViewModel'

PanelHost.CreatePanel failed: panelId=Settings
  No service for type 'VoiceStudio.App.ViewModels.SettingsViewModel'
```

### From `startup_failure.txt`:
```
Zero-panel startup detected. LoadedCount=0. Left=False, Center=False, Right=False, Bottom=False.
```

---

## 3. Failure Chain (Step by Step)

### Step 1: Panel descriptors are registered correctly

In `AppServices.RegisterAllPanels()` (called from `AppServices.Initialize()`), three registration services run:

```csharp
AdvancedPanelRegistrationService.RegisterAdvancedPanels(registry);
CorePanelRegistrationService.RegisterCorePanels(registry);
ModulePanelRegistrationService.RegisterModulePanels(registry);
```

Each registers `PanelDescriptor` objects with `ViewType` and `ViewModelType`:

```csharp
// From CorePanelRegistrationService.cs
new PanelDescriptor {
    PanelId = "Profiles",
    ViewType = typeof(ProfilesView),
    ViewModelType = typeof(ProfilesViewModel),  // <-- THIS TYPE
    ...
}
```

This part works fine. The registry knows about ~50+ panels.

### Step 2: Panel creation triggers ViewModel resolution

When a panel needs to load, `PanelRegistry.CreatePanel(panelId)` is called:

```csharp
// PanelRegistry.cs line 80-90
var view = Activator.CreateInstance(descriptor.ViewType);  // Creates the View -- OK
if (descriptor.ViewModelType != null)
{
    var viewModel = _viewModelFactory.Create(descriptor.ViewModelType);  // <-- FAILS HERE
    userControl.DataContext = viewModel;
}
```

### Step 3: ViewModelFactory calls GetRequiredService — which throws

```csharp
// ViewModelFactory.cs line 34
return _serviceProvider.GetRequiredService(viewModelType);
```

`GetRequiredService` throws `InvalidOperationException` because **no ViewModel types are registered in the DI container**.

### Step 4: Exception is caught and swallowed

In `PanelHost.LoadPanelAsync()`:

```csharp
catch (Exception ex)
{
    Debug.WriteLine($"[PanelHost] Error creating panel {panelId}: {ex.Message}");
    // Writes to startup_diag.txt in DEBUG builds
    return null;  // Panel silently fails to load
}
```

The null return propagates back. `OpenPanelByIdAsync` returns false. The PanelHost body stays empty.

### Step 5: Zero panels loaded, user sees empty shell

`InitializePanelsAsync` tries all four defaults, all fail, writes `startup_failure.txt`.

---

## 4. The Missing Registration

In `AppServices.Initialize()` (`src/VoiceStudio.App/Services/AppServices.cs`), **services** like `PanelStateService`, `NavigationService`, `CommandRouter`, etc. are all registered. But **zero ViewModels** are registered.

The file has `services.AddSingleton<IViewModelFactory>(sp => new ViewModelFactory(sp))` (line 215) — so the factory exists, but there's nothing for it to resolve.

### What's needed

Every ViewModel type referenced in any `PanelDescriptor.ViewModelType` must be registered. The existing extension method `AddViewModel<T>()` (defined in `ViewModelFactory.cs`) is designed for this but never called.

### ViewModels that must be registered (minimum for startup defaults)

| ViewModel Class | Namespace | Used By Panel |
|---|---|---|
| `ProfilesViewModel` | `VoiceStudio.App.Views.Panels` | Profiles (Left) |
| `TimelineViewModel` | `VoiceStudio.App.Views.Panels` | Timeline (Center) |
| `EffectsMixerViewModel` | `VoiceStudio.App.Views.Panels` | EffectsMixer (Right) |
| `MacroViewModel` | `VoiceStudio.App.Views.Panels` | Macro (Bottom) |

### ViewModels needed for nav button panels

| ViewModel Class | Namespace | Used By Panel |
|---|---|---|
| `LibraryViewModel` | `VoiceStudio.App.Views.Panels` | Library |
| `AnalyzerViewModel` | `VoiceStudio.App.Views.Panels` | Analyzer |
| `SettingsViewModel` | `VoiceStudio.App.ViewModels` | Settings |
| `DiagnosticsViewModel` | `VoiceStudio.App.Views.Panels` | Diagnostics |
| `TrainingViewModel` | `VoiceStudio.App.Views.Panels` | Training |

### Full list: every `ViewModelType` from all three registration services

All ViewModels referenced in `CorePanelRegistrationService`, `AdvancedPanelRegistrationService`, and `ModulePanelRegistrationService` must be registered. There are approximately 50+ panel descriptors.

---

## 5. Key Files

| File | Role |
|---|---|
| `src/VoiceStudio.App/Services/AppServices.cs` | DI container setup; `Initialize()` builds ServiceCollection; `RegisterAllPanels()` registers descriptors but NOT ViewModels |
| `src/VoiceStudio.App/Services/ViewModelFactory.cs` | Calls `GetRequiredService(viewModelType)` — throws when type not registered |
| `src/VoiceStudio.App/Services/PanelRegistry.cs` | `CreatePanel()` creates View via `Activator`, then asks ViewModelFactory for ViewModel |
| `src/VoiceStudio.App/Services/CorePanelRegistrationService.cs` | Registers ~20 core panel descriptors (Profiles, Timeline, EffectsMixer, Macro, etc.) |
| `src/VoiceStudio.App/Services/AdvancedPanelRegistrationService.cs` | Registers ~15 advanced panel descriptors |
| `src/VoiceStudio.App/Services/ModulePanelRegistrationService.cs` | Registers ~15 module panel descriptors |
| `src/VoiceStudio.App/Controls/PanelHost.xaml.cs` | `LoadPanelAsync()` — catches CreatePanel exceptions, returns null |
| `src/VoiceStudio.App/MainWindow.Workspaces.cs` | `InitializePanelsAsync()` — calls OpenPanelByIdAsync for 4 defaults |
| `src/VoiceStudio.App/Controls/PanelHost.xaml` | XAML for PanelHost — `ContentPresenter Content="{x:Bind Content, Mode=OneWay}"` |

---

## 6. The Fix

### Option A: Register all ViewModels explicitly (safest, most verbose)

In `AppServices.Initialize()`, before `_provider = services.BuildServiceProvider()`, add:

```csharp
// Register all panel ViewModels
services.AddTransient<VoiceStudio.App.ViewModels.ProfilesViewModel>();
services.AddTransient<VoiceStudio.App.ViewModels.TimelineViewModel>();
services.AddTransient<VoiceStudio.App.ViewModels.EffectsMixerViewModel>();
services.AddTransient<VoiceStudio.App.ViewModels.MacroViewModel>();
// ... every ViewModel type from every PanelDescriptor
```

### Option B: Auto-register from PanelDescriptors (DRY, requires careful ordering)

After `RegisterAllPanels()` is called (which populates `_descriptors`), iterate all descriptors and register their ViewModelTypes:

```csharp
private static void RegisterAllPanels()
{
    // ... existing registration code ...

    // Auto-register all ViewModelTypes from descriptors
    foreach (var descriptor in registry.GetAllDescriptors())
    {
        if (descriptor.ViewModelType != null)
        {
            // Check if not already registered to avoid duplicates
            services.AddTransient(descriptor.ViewModelType);
        }
    }
}
```

**Problem**: `RegisterAllPanels()` is called AFTER `services.BuildServiceProvider()`, so the ServiceCollection is already built. You'd need to restructure so VM registration happens BEFORE `BuildServiceProvider()`.

### Option C: Change ViewModelFactory to use ActivatorUtilities (quick fix, most pragmatic)

Replace `GetRequiredService` with `ActivatorUtilities.CreateInstance` which can construct types NOT registered in DI by resolving their constructor parameters from DI:

```csharp
// ViewModelFactory.cs line 34 — CHANGE FROM:
return _serviceProvider.GetRequiredService(viewModelType);

// TO:
return ActivatorUtilities.CreateInstance(_serviceProvider, viewModelType);
```

This uses the DI container to resolve constructor dependencies but does NOT require the ViewModel itself to be registered. It's the standard pattern for ViewModel-first creation in WinUI/WPF apps.

**This is the recommended fix** because:
1. Single line change
2. No need to enumerate and register 50+ ViewModel types
3. Constructor dependencies (IBackendClient, ISettingsService, etc.) are still resolved from DI
4. `CreateWithParameters` (line 42) already uses `ActivatorUtilities.CreateInstance` — so this makes the class consistent

---

## 7. Secondary Issue: `PanelHost.ContentProperty` shadows `UserControl.ContentProperty`

```csharp
// PanelHost.xaml.cs line 39
public static new readonly DependencyProperty ContentProperty =
    DependencyProperty.Register(nameof(Content), typeof(UIElement), typeof(PanelHost),
        new PropertyMetadata(null, OnContentChanged));
```

This shadows the base `UserControl.Content`. The XAML binds via `{x:Bind Content}` which resolves to the local `Content` property. This works but creates a split: the base `UserControl` visual tree (Grid with header/body) uses the base Content, while the loaded panel is set on the custom Content property and displayed via `ContentPresenter`.

**This is not the cause of the current bug** but is a latent risk. If any WinUI framework code internally accesses `UserControl.Content`, it won't see the panel.

---

## 8. Additional Context

### PanelHost.xaml structure
```xml
<UserControl>
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="32" />   <!-- Header (always visible) -->
      <RowDefinition Height="*" />     <!-- Body -->
    </Grid.RowDefinitions>
    <!-- Header with title, icons, buttons — renders correctly -->
    <Border Grid.Row="0">...</Border>
    <!-- Body with ContentPresenter — empty because Content is null -->
    <Border Grid.Row="1">
      <ContentPresenter Content="{x:Bind Content, Mode=OneWay}" />
    </Border>
  </Grid>
</UserControl>
```

When `PanelHost.Content` is null (because panel creation failed), the ContentPresenter renders nothing, explaining the empty black panel bodies.

### Workspace layout state
The saved workspace layout in `appsettings.json` has all `activePanelId = ""`, so workspace restore loads nothing. The app falls through to loading defaults (Profiles, Timeline, EffectsMixer, Macro), which also fail due to the ViewModel DI issue.

---

## 9. Reproduction

1. Build: `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
2. Launch: `Start-Process ".buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"`
3. Observe: Shell renders, panel headers visible, panel bodies empty
4. Click any nav button: nothing happens (panel creation fails)
5. Confirm: Check `%LOCALAPPDATA%\VoiceStudio\crashes\startup_diag.txt` for DI resolution errors

---

## 10. Verification After Fix

After applying the fix:
1. `startup_diag.txt` should have no new CreatePanel errors
2. `startup_failure.txt` should not be written (or should show `LoadedCount=4`)
3. All four default panels should render content inside their PanelHost bodies
4. Nav button clicks should switch panels successfully
5. Build: `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — 0 errors
6. Tests: `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` — all pass
