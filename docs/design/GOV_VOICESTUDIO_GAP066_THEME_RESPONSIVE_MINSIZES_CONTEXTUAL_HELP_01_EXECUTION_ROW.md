# GOV-VOICESTUDIO-GAP066-THEME-RESPONSIVE-MINSIZES-CONTEXTUAL-HELP-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP066-THEME-RESPONSIVE-MINSIZES-CONTEXTUAL-HELP-01 |
| **GAP** | GAP-066 (Theme / responsive min sizes / contextual help) |
| **Status** | **Closed** 2026-04-09 — [VOICESTUDIO_GAP066_THEME_RESPONSIVE_MINSIZES_CONTEXTUAL_HELP_LANE_CLOSURE_2026-04-09.md](../../docs/reports/verification/VOICESTUDIO_GAP066_THEME_RESPONSIVE_MINSIZES_CONTEXTUAL_HELP_LANE_CLOSURE_2026-04-09.md) |
| **Phase** | Bounded execution row — WinUI shell + panels + design tokens |
| **Role** | UI Engineer |

## §1 Objective (frozen)

Apply **design-token**-based minimum layout sizes for the main shell and `PanelHost`, replace raw `HelpOverlay` background hex with a shared token, and add **contextual help** affordances on three high-friction surfaces: `FirstRunWizard` (steps 3–4) and `KeyboardCustomizationView`. Seal with seam tests and governance.

## §2 Hard IN

- New tokens: `VSQ.Shell.MinWidth`, `VSQ.Shell.MinHeight`, `VSQ.PanelHost.MinWidth`, `VSQ.HelpOverlay.BackgroundBrush` in [`DesignTokens.xaml`](../../src/VoiceStudio.App/Resources/DesignTokens.xaml).
- [`MainWindow.xaml`](../../src/VoiceStudio.App/MainWindow.xaml) root `RootGrid`: `MinWidth` / `MinHeight` bound to shell tokens.
- [`PanelHost.xaml`](../../src/VoiceStudio.App/Controls/PanelHost.xaml): `MinWidth` bound to panel-host token.
- [`HelpOverlay.xaml`](../../src/VoiceStudio.App/Controls/HelpOverlay.xaml): background uses `VSQ.HelpOverlay.BackgroundBrush` (no raw `#CC000000` in control XAML).
- `FirstRunWizard`: `HelpButton` + `HelpOverlay`; visibility on steps 3–4; step-specific copy in `UpdateStepUI`.
- `KeyboardCustomizationView`: `HelpButton` + `HelpOverlay` with shortcut-capture help text.
- AutomationIds: `FirstRunWizard_HelpButton`, `KeyboardCustomization_HelpButton` (+ optional `FirstRunWizard_HelpOverlay`).
- `Gap066Tests.cs`: 8 source seam checks; build + App.Tests + `verify.ps1 -Quick` GREEN.

## §3 Hard OUT

- No shell decomposition, no full layout rewrite, no global help portal, no content on every panel, no WCAG umbrella inside this lane.

## §4 Authority map

| Concern | Owner |
|--------|--------|
| Shell min size | `MainWindow.xaml` `RootGrid` + `VSQ.Shell.*` tokens |
| Panel host floor | `PanelHost.xaml` + `VSQ.PanelHost.MinWidth` |
| Help overlay chrome | `HelpOverlay` + `VSQ.HelpOverlay.BackgroundBrush` |
| Wizard step help | `FirstRunWizard.xaml` / `.xaml.cs` |
| Keyboard help | `KeyboardCustomizationView` |
| Tests | `Gap066Tests.cs` |

## §5 Acceptance criteria

- [x] Design tokens added; `HelpOverlay` uses token background.
- [x] `MainWindow` + `PanelHost` reference min-size tokens.
- [x] Wizard + keyboard panels expose contextual help with registered AutomationIds where specified.
- [x] `Gap066Tests` (8) + `FirstRunWizardTests` (11) + full App.Tests + `verify.ps1 -Quick` PASS.
- [x] Closure report + tracker + STATE + CANONICAL_REGISTRY + openmemory updated. **Done 2026-04-09.**

## §6 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap066Tests"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FirstRunWizardTests"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
```

## §7 Risk register

| Risk | Mitigation |
|------|------------|
| XAML compiler / overlay layering | Keep `HelpOverlay` as direct child of root grid; last in tree for z-order |

## §8 Rollback

Revert token + XAML + test + governance commits in order; no persistent data migration.
