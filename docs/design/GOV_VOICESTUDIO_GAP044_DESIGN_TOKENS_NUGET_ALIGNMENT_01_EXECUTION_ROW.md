# GOV-VOICESTUDIO-GAP044-DESIGN-TOKENS-NUGET-ALIGNMENT-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP044-DESIGN-TOKENS-NUGET-ALIGNMENT-01 |
| **GAP** | GAP-044 (design tokens `VSQ.*` + NuGet alignment) |
| **Status** | **Closed** (2026-04-04) — Model Manager spacing tokens + package version properties per authority memo |
| **Phase** | Professional Roadmap v3 — Phase 4 |
| **Role** | UI Engineer + Build & Tooling |
| **Dependency** | GAP-038 (partial); no GPU viewport work in this lane |
| **Authority memo** | [GOV_VOICESTUDIO_GAP044_AUTHORITY_DECISIONS.md](GOV_VOICESTUDIO_GAP044_AUTHORITY_DECISIONS.md) |

## §1 Objective (frozen)

- **Tokens:** Remove hard-coded `Spacing` literals on the **Model Manager** surface by mapping them to **existing or new `VSQ.*` resources** documented in the authority memo.
- **Packages:** Establish a **single MSBuild authority** for **Microsoft.Graphics.Win2D**, **CommunityToolkit.WinUI.UI.Controls**, and **CommunityToolkit.Mvvm** (and aligned **NAudio** / **Microsoft.Windows.SDK.BuildTools** pins already listed in repo props) so version literals are not duplicated without a named property.

## §2 Hard IN

- `src/VoiceStudio.App/Resources/DesignTokens.xaml` — additive tokens only; **no renames/deletes** of existing keys.
- `src/VoiceStudio.App/Views/Panels/ModelManagerView.xaml` — replace in-scope numeric `Spacing` literals with `StaticResource` to design tokens.
- `Directory.Build.props` — version **properties** (or `PackageVersion` entries) as the **authoritative numbers** for the aligned packages; `VoiceStudio.App.csproj` references them via `$(PropertyName)`.
- Preserve **automation IDs** and accessibility behavior; no unrelated panel edits.

## §3 Hard OUT

- App-wide theme redesign, Mica changes, or bulk XAML churn outside the files in §2.
- **Speculative** NuGet upgrades (no version bumps unless required to fix restore/build breakage).
- Introducing **Central Package Management** (`Directory.Packages.props`) or `ManagePackageVersionsCentrally` in this lane (out of scope; document as future option in authority memo).
- GAP-038 **GPU / Win2D rendering** behavior changes beyond package version property wiring.

## §4 Authority map

| Concern | Owner |
|---------|--------|
| `VSQ.*` resource keys + values | `DesignTokens.xaml` (+ `validate_xaml_resources.py` when touched) |
| Model Manager layout spacing | `ModelManagerView.xaml` |
| Win2D / CommunityToolkit / NAudio / SDK BuildTools version numbers | `Directory.Build.props` properties → `VoiceStudio.App.csproj` `PackageReference` |
| Proof + governance sync | Closure report + `.cursor/STATE.md` + `CANONICAL_REGISTRY.md` + tracker |

## §5 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ModelManagerViewModelSeamTests"
python -m pytest tests/ci/ -q --randomly-seed=12345
.\scripts\verify.ps1 -Quick
python scripts/run_verification.py
python scripts/validate_xaml_resources.py
```

## §6 Risk register

| Risk | Mitigation |
|------|------------|
| XAML resource key typo | Build + `validate_xaml_resources.py` |
| MSBuild property mismatch | Single property per package in `Directory.Build.props`; grep for duplicate literals |
| Scope creep into other panels | Code review against §3 |

## §7 Rollback order

1. `ModelManagerView.xaml` spacing tokens  
2. `DesignTokens.xaml` additive token(s)  
3. `VoiceStudio.App.csproj` / `Directory.Build.props` property wiring  
4. Governance docs / closure report  

## §8 Related

- Tracker: [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) — **GAP-044**  
- Prior closure chronology: [STATE.md](../../.cursor/STATE.md) ACTIVE WINDOW  
