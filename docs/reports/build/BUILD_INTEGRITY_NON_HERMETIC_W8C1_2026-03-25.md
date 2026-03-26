# Build integrity: non-hermetic W8-C1 proof (defect inventory and resolution)

**Date:** 2026-03-25  
**Closure commit referenced by proof:** `bcd6d4e52e0b2a7763f0baaa261e7cdac7f8a665`  
**Repro:** Clean git worktree at that commit; `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` **failed**.

## Root causes (classified)

| Bucket | Evidence | Resolution |
|--------|-----------|------------|
| **`.gitignore` overly broad** | `git check-ignore -v` reported `.gitignore:152:models/` for `src/VoiceStudio.Core/Models/*.cs` and `src/VoiceStudio.App/Core/Models/*.cs`. Pattern `models/` matches directory name `Models` on case-insensitive filesystems. | Change to root-only **`/models/`** so downloaded ML trees under repo root stay ignored; **C# `Models` projects remain tracked**. |
| **Untracked client extraction surface** | ~70 files under `src/VoiceStudio.App/Core/Services/` showed `??` (never committed). ~120+ untracked `*Client.cs`, `*Models.cs`, `BackendClientHttpPipeline.cs`, etc. under `src/VoiceStudio.App/Services/`. | **`git add`** those paths so committed tree includes interfaces and HTTP client implementations `AppServices` / `BackendClient` require. |
| **Untracked Core helpers** | `??` under `src/VoiceStudio.Core/Panels/` (search navigation types) and `src/VoiceStudio.Core/Services/` (small seam interfaces). | **Stage** same. |

## Sample compiler errors (verbatim class)

First failures on clean tree included:

- `IEmotionControlClient.cs`: CS0246 `EmotionApplyExtendedRequest`, `EmotionPreviewResponse` — types live under `VoiceStudio.Core.Models` in `.cs` files previously invisible to git due to ignore rules.
- `AppServices.cs`: CS0246 `ISearchClient`, `IBackupRestoreClient`, … — **interface files were untracked** (`ISearchClient.cs` etc.).
- `BackendClient.cs`: CS0535 / CS0246 — **implementation drift** vs `IBackendClient` until pipeline types exist; `BackendClientHttpPipeline` **untracked**.

Full log (machine-local): `E:\VoiceStudio-integrity-bcd6\build-clean-commit.log` (optional; not committed).

## Hermetic proof expectation

After fix: **clean** `git status`, same `dotnet build`, targeted seam tests, `verify.ps1 -Quick`; `latest_pointer.json` `commit_hash` must equal the commit that contains this repair.

## Related governance

- [WORKFLOW_COHERENCE_PASS_08_QUALITY_BENCHMARK_PROFILE_COMPARISON.md](../../design/WORKFLOW_COHERENCE_PASS_08_QUALITY_BENCHMARK_PROFILE_COMPARISON.md) — Pass 08 changelog entry when hermetic run exists.
