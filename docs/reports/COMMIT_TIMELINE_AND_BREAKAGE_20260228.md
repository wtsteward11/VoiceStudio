# Commit Timeline: What Broke and When

**Golden Working Commit**: `b661e694` — Feb 13, 2026  
**First Breaking Commit**: `ab19887a` — Feb 23, 2026  
**Fatal Commit**: `df3873a7` — Feb 25, 2026

---

## Phase 1: Working State (Jan 29 – Feb 15)

| Date | Commit | Description | Build Status |
|------|--------|-------------|-------------|
| Feb 13 | `b661e694` | Audio format expansion | **BUILDS & LAUNCHES** |
| Feb 15 | `02a2efac` | Test fixtures and config updates | Builds |
| Feb 16 | `99f47e29` | Gap Resolution Sprint 3 | Builds |

These commits all use the original package set with NO `Microsoft.Extensions.Hosting`. The XAML compiler succeeds on Pass 1 and Pass 2. XBF files and `XamlTypeInfo.g.cs` are generated properly.

---

## Phase 2: Plugin/Architecture Work (Feb 17 – Feb 18)

| Date | Commit | Description | Build Status |
|------|--------|-------------|-------------|
| Feb 17 | `4e6d2d39` | Plugin manifest enforcement | Builds |
| Feb 18 | `a8829124` | Plugin empty catch fixes | Builds |
| Feb 18 | `54a4df3a` | Plugin system infrastructure | Builds |
| Feb 18 | `d3919efb` | Core systems update v1.0.2-rc1 | Builds |

Still working. No XAML compiler changes.

---

## Phase 3: CI/Testing/Type Safety (Feb 20 – Feb 22)

| Date | Commit | Description | Build Status |
|------|--------|-------------|-------------|
| Feb 20 | `f17e8a3f` | Remove installer/runtime from tracking | Builds |
| Feb 21 | `838ff88a` | Engine init fix | Builds |
| Feb 21 | `481528e1` | Phase 2 Frontend | Builds |
| Feb 21 | `3187c18f` | Phase 3 Panels | Builds |
| Feb 21 | `71de29b6` | Phase 7-8 GA prep | Builds |
| Feb 21 | `1258173e` | Mypy type errors (1215 fixes) | Builds |
| Feb 22 | `b8b8858a` | Functional baseline, launch fix | Builds |

Still working. Build config untouched.

---

## Phase 4: Reintegration — THE BREAK BEGINS (Feb 23)

| Date | Commit | Description | Build Status |
|------|--------|-------------|-------------|
| Feb 23 | `4eb80d43` | Restore functional build baseline | **BUILDS (with wrapper masking exit 1)** |
| Feb 23 | `ab19887a` | Reconcile C# app files with origin/main | **XAML COMPILER STARTS FAILING** |
| Feb 23 | `b9c3a018` | Revert XAML changes that broke launch | Attempted fix |
| Feb 24 | `1a1cd1b5` | Reintegrate v1.0.2 from baseline | Wrapper masks failure |

**What happened at `ab19887a`**: Five XAML panel files (EngineParameterTuningView, EngineRecommendationView, PronunciationLexiconView, UltimateDashboardView, VoiceBrowserView) were expanded from stubs to full implementations. The XAML compiler started returning exit code 1 on Pass 2. The wrapper (`xaml-compiler-wrapper.cmd`) generated synthetic `output.json`, masking the failure. Build "succeeded" but produced empty `XamlTypeInfo.g.cs`.

---

## Phase 5: Gap Remediation (Feb 24)

| Date | Commit | Description | Build Status |
|------|--------|-------------|-------------|
| Feb 24 | `55b3d298` | Codebase audit remediation | Wrapper masks failure |
| Feb 24 | `478aafd9` | Architecture review remediation | Wrapper masks failure |
| Feb 24 | `ae45519d` | 8-phase gap analysis | Wrapper masks failure |
| Feb 25 | `01294339` | Comprehensive roadmap execution | Wrapper masks failure |

All these builds "succeed" because the wrapper handles exit code 1. But `XamlTypeInfo.g.cs` is empty in all of them. The app would crash at runtime with `XamlParseException`.

---

## Phase 6: THE FATAL COMMITS (Feb 25)

| Date | Commit | Description | Build Status |
|------|--------|-------------|-------------|
| Feb 25 | `f0758e05` | Eliminate 674 CS2002 warnings | Wrapper masks failure |
| Feb 25 | `dfc49947` | **Simplify XAML pipeline, upgrade SDK** | Multiple config changes |
| Feb 25 | `df3873a7` | **Add Generic Host bootstrap** | **FATAL: .NET 9.0 packages added** |

### Commit `dfc49947` — "Simplify XAML pipeline"
- Removed `EnsureRuntimeIdentifierForWin2D` Release-only condition → RID set for all configs
- Added explicit `RuntimeIdentifier=win-x64` → adds `win-x64` to intermediate paths
- Set `WindowsAppSDKSelfContained=true` for Debug
- Set `DisableXbfGeneration=true` explicitly
- Removed CaptureXamlErrors and IncludeXbfInPublishOutput targets

### Commit `df3873a7` — "Generic Host bootstrap" (THE KILLER)
- Added `Microsoft.Extensions.Hosting` 9.0.0
- Added `Microsoft.Extensions.Logging` 9.0.0
- Added `Microsoft.Extensions.Configuration.Json` 9.0.0
- Added `Microsoft.Extensions.DependencyInjection` 10.0.2 (upgrade from existing)
- These packages brought in 29 transitive .NET 9.0 assemblies
- **The XAML compiler (net472) cannot process .NET 9.0 assembly metadata**
- **Result: XamlCompiler.exe crashes silently with exit code 1, zero output**

---

## Summary

```
Feb 13 ──── WORKING (b661e694) ────────────────────────────────►
                                                                │
Feb 23 ──── XAML compiler starts failing (ab19887a) ───────────►
            (masked by wrapper — builds "succeed" with empty    │
             XamlTypeInfo.g.cs — app would crash at runtime)    │
                                                                │
Feb 25 ──── FATAL: .NET 9.0 packages added (df3873a7) ────────►
            (XAML compiler crashes on ANY build attempt)        │
                                                                │
Feb 25-28 ─ Attempts to fix by changing DisableXbfGeneration,  │
            GenXbfPath, EnableTypeInfoReflection, etc.         │
            (all treating symptoms, not root cause)             │
                                                                │
Feb 28 ──── Root cause identified: .NET 9.0 packages ──────────►
            Restored from b661e694 worktree. UI LAUNCHES.
```

---

## Lessons

1. The wrapper (`xaml-compiler-wrapper.cmd`) masked the real failure for 10+ commits. Without it, the build would have failed immediately and the XAML issue would have been caught at `ab19887a`.

2. The .NET 9.0 package addition was the point of no return. Even with the wrapper removed, the compiler crashes on any build.

3. Every "fix" attempt after Feb 25 was chasing symptoms because the root cause (9.0 packages) was never identified until the bisect test on Feb 28.
