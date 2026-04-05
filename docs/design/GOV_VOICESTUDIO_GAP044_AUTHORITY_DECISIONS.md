# GOV-VOICESTUDIO-GAP044 — Authority decisions (design tokens + NuGet alignment)

**Lane:** [GOV_VOICESTUDIO_GAP044_DESIGN_TOKENS_NUGET_ALIGNMENT_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP044_DESIGN_TOKENS_NUGET_ALIGNMENT_01_EXECUTION_ROW.md)  
**Date:** 2026-04-04  

## 1. StackPanel `Spacing` on Model Manager

**Decision:** Use **`x:Double`** tokens under `VSQ.Spacing.Value.*` for `Spacing` (double), not `Thickness` resources.

- **12px** vertical stack gap → new token **`VSQ.Spacing.Value.Relaxed`** = `12` (between **Medium** `8` and **Large** `16`).
- **8px** horizontal stack gaps → existing **`VSQ.Spacing.Value.Medium`** = `8`.

**Rationale:** WinUI `StackPanel.Spacing` is a `double`; thickness tokens are for `Padding`/`Margin`.

## 2. Package version authority

**Decision:** Author package **version numbers once** in `Directory.Build.props` as MSBuild properties:

- `VoiceStudioGraphicsWin2DVersion`
- `VoiceStudioCommunityToolkitWinUIVersion`
- `VoiceStudioCommunityToolkitMvvmVersion`
- `VoiceStudioNAudioVersion`
- `VoiceStudioWindowsSdkBuildToolsPackageVersion`

`VoiceStudio.App.csproj` references these via `Version="$(VoiceStudio…)"` on the corresponding `PackageReference` entries.

**Rationale:** The repo already centralizes `MicrosoftWindowsAppSDKVersion` in props; duplicating literals in `.csproj` drifts from `PackageVersion` items in the same props file. Properties unify **restore graph** and **documentation** without adopting full CPVM in this lane.

**Deferred:** `Directory.Packages.props` + `ManagePackageVersionsCentrally` — evaluate when touching **all** projects’ package graphs; out of scope for GAP-044.

## 3. Non-goals

- Changing Win2D usage, shaders, or waveform GPU path (GAP-038).
- Renaming or removing existing `VSQ.*` keys.
