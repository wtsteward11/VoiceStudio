# Feature Reintegration Backlog

## Source

- Archive: `docs/archive/pre_restore_20260228/`
- Reference tag: `v1.0.2-rc1` (commit `64311693`, Feb 18 2026)
- Prior restoration: 39 files already restored (commit `0c662c54`)

## Remaining Files Not Yet Restored (13 files)

These files exist in the archive but are NOT in current `src/`. Each has a reason for exclusion and a reintegration plan.

### Slice A: Blocked by Missing Dependency (3 files)

These require `JsonSchema.Net` which is not in the protected csproj. Cannot restore without a build config change (which requires Tyler's explicit approval).

| File | Reason Blocked | Risk |
|------|---------------|------|
| `src/VoiceStudio.Core/Plugins/PluginSchemaValidator.cs` | Requires `JsonSchema.Net` NuGet package | HIGH (csproj change) |
| `src/VoiceStudio.App.Tests/Plugins/PluginSchemaValidatorTests.cs` | Depends on PluginSchemaValidator | HIGH |
| `src/VoiceStudio.App.Tests/Services/CorePanelRegistrationServiceTests.cs` | May depend on removed types | MEDIUM |

### Slice B: Generic Host / DI Bootstrap (3 files)

These were part of the `df3873a7` commit that introduced `Microsoft.Extensions.Hosting` and broke the build. They are intentionally excluded.

| File | Reason Excluded | Risk |
|------|----------------|------|
| `src/VoiceStudio.App/appsettings.json` | Part of Generic Host bootstrap (reverted) | HIGH |
| `src/VoiceStudio.App/Bootstrap/HostFactory.cs` | Generic Host factory (requires Hosting package) | HIGH |
| `src/VoiceStudio.App/Configuration/AudioOptions.cs` | Options pattern (requires Hosting) | HIGH |
| `src/VoiceStudio.App/Configuration/BackendOptions.cs` | Options pattern (requires Hosting) | HIGH |

### Slice C: UI Panels (Low Risk, 4 files)

These are view/viewmodel files that could be restored if they compile without new dependencies.

| File | Description | Risk |
|------|------------|------|
| `src/VoiceStudio.App/Views/Panels/AdvancedSpectrogramVisualizationView.xaml` | Spectrogram panel | LOW |
| `src/VoiceStudio.App/Views/Panels/AdvancedWaveformVisualizationView.xaml` | Waveform panel | LOW |
| `src/VoiceStudio.App/Views/Panels/EngineSetupWizardView.xaml` + `.xaml.cs` | Engine wizard | LOW |
| `src/VoiceStudio.App/Views/Panels/EngineSetupWizardViewModel.cs` | Engine wizard VM | LOW |

### Slice D: StatusBar (1 file)

| File | Description | Risk |
|------|------------|------|
| `src/VoiceStudio.App/ViewModels/StatusBarViewModel.cs` | Status bar VM | LOW |

## Already Restored (39 files, commit `0c662c54`)

Plugin infrastructure, command system, plugin UI, test files, high-impact services, UI/ViewModels, and remaining modified files were all restored from `v1.0.2-rc1` in the prior session.

## Reintegration Rules

1. **One slice at a time.** Build + XAML health + UI launch after each slice.
2. **NEVER touch protected build config files.** If a feature requires changing `global.json`, `VoiceStudio.sln`, `Directory.Build.props`, `Directory.Build.targets`, `VoiceStudio.App.csproj`, or `VoiceStudio.Core.csproj` -- stop and get Tyler's explicit approval.
3. **Use `git checkout v1.0.2-rc1 -- <file>` for restoration** (same pattern as prior session).
4. **If build breaks, revert immediately:** `git checkout HEAD -- <files>`. No fix-forward.
5. **Commit each slice separately** with a descriptive message referencing this backlog.
6. **Run golden path verification** after each slice: `scripts/verify.ps1 -Quick`.

## Recommended Order

1. Slice C (UI panels) -- lowest risk, immediate visual value
2. Slice D (StatusBar) -- trivial, low risk
3. Slice A -- requires Tyler's decision on `JsonSchema.Net` package
4. Slice B -- intentionally excluded; requires architectural decision on Generic Host
