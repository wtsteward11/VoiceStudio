# Release XAML Smoke Gate

**Purpose:** Document where the Release XAML Smoke stage runs and whether it is a CI gate.

---

## Definition

The **Release XAML Smoke** stage in `scripts/verify.ps1` (lines 572–587):

1. Runs `dotnet build -c Release -p:Platform=x64`
2. If build succeeds, runs `tools/build/Check-XamlHealth.ps1` (if present)

This protects against Gate C historical crashes that were Release-only (XAML compiler differences between Debug and Release).

---

## Where It Runs

| Context | Runs Release XAML Smoke? | Notes |
|---------|--------------------------|-------|
| **verify.ps1** (full) | Yes | Stage 1 includes Release XAML Smoke |
| **verify.ps1 -Quick** | No | Quick skips Release build; Debug build only |
| **run_verification.py** | No | Does not invoke verify.ps1; runs Python checks only |
| **CI (build.yml)** | No | CI runs Release build + `scripts/proactive-xaml-check.ps1` (different script) |
| **CI (ci.yml)** | No | No verify.ps1 invocation |

---

## Conclusion

**Release XAML smoke is manual.** It runs only when someone executes `.\scripts\verify.ps1` (full mode, not -Quick). It is **not** a CI gate.

CI does run a Release build and XAML checks (`proactive-xaml-check.ps1`), but the verify.ps1 "Release XAML Smoke" stage uses `Check-XamlHealth.ps1`, which is a different script. To make Release XAML smoke a CI gate, add a CI step that runs `verify.ps1` or explicitly invokes the Release build + `Check-XamlHealth.ps1` sequence.

---

## Recommendation

- **Pre-release:** Run `.\scripts\verify.ps1` (full) before tagging a release to include Release XAML Smoke.
- **Future CI:** Add Release XAML Smoke to CI in a release-confidence phase if Gate C regressions recur.
