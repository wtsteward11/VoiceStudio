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

---

## Changelog — Hermetic compile closure (2026-03-26)

**Baseline commit (isolated repro):** `c7c40a6bd8c95f4e459ad4cb8803d48587f2ed48`  
**Worktree:** `E:\VoiceStudio-integrity-hermetic` (detached at baseline → apply closure → `dotnet clean` + `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` **0 errors**)

### Error inventory (representative)

| Error / symptom | Failing location | Cause | In committed tree at baseline? |
|-----------------|------------------|-------|--------------------------------|
| CS0103 `BackendPlaybackBaseUrl` | `RecordingViewModel.cs`, `ScriptEditorViewModel.cs` | Callers referenced utility; files were **untracked** on working tree | No — add `Utilities/BackendPlaybackBaseUrl.cs` |
| CS0103 `ScriptEditorSynthesisRequestBuilder` | `ScriptEditorViewModel.cs` | Same | No — add `Utilities/ScriptEditorSynthesisRequestBuilder.cs` |
| CS0426 embedding model aliases | `EmbeddingExplorerViewModelTests.cs` | Tests still aliased nested VM types; production moved types to `VoiceStudio.App.Services` | Tracked file out of date vs App |
| CS0539 `MockBackendClient.SearchAsync` | `MockBackendClient.cs` | `IBackendClient` no longer exposes search; stub still implemented removed member | Tracked test out of date |
| CS0246 `VoiceProfileSummary` in tests | `VoiceBrowserViewModelTests.cs` | Test harness not updated for `IVoiceBrowserClient` seam | Tracked test out of date |
| Cascading test compile failures | Multiple `VoiceStudio.App.Tests` | Partial sync left tests behind App | Resolved by mirroring **entire** `src/VoiceStudio.App.Tests` from closure source |

### Manifest — A (required compile-closure scope)

**Measured deltas vs `c7c40a6b` (main working tree):**

- `git diff --name-only c7c40a6b -- src/VoiceStudio.App src/VoiceStudio.Core` → **230** paths  
- `git ls-files --others --exclude-standard src/VoiceStudio.App src/VoiceStudio.Core` → **4** paths (`GlobalTransportControl.xaml`, `.xaml.cs`, `BackendPlaybackBaseUrl.cs`, `ScriptEditorSynthesisRequestBuilder.cs`)
- `git diff --name-only c7c40a6b -- src/VoiceStudio.App.Tests` → **66** paths  
- `git ls-files --others --exclude-standard src/VoiceStudio.App.Tests` → **102** paths (new seam/persistence/utility tests, `MockSearchClient.cs`, etc.)

**Reason:** Baseline `c7c40a6b` **without** the above **does not** compile: App references types and seams that existed only in the dirty working tree (untracked or uncommitted). Tests must match the App surface (`IBackendClient` extraction, `IVoiceBrowserClient`, embedding service models, etc.).

### Manifest — B (explicit exclusions from this closure commit)

**Not** part of the bounded C# closure staged with `fix(build): …` (remain uncommitted elsewhere):

- `backend/`, `app/core/`, `docs/`, `scripts/` (except future gated edits), `.cursor/`, `tests/` (pytest tree root), and other repo paths outside `src/VoiceStudio.{App,Core,App.Tests}`.

### Manifest — C (suspect / challenged)

- **Large** App/Core diff (**230** files): primarily **client extraction / transport / context / panel wiring** already underway on the branch — not introduced solely for two utility files; the utilities were the **last missing** pieces once tests and App were aligned.
- **+102** new test files: broaden **seam and persistence** coverage; behavior is **test and seam surface**, not backend protocol changes.

### Commands (post-commit proof)

```powershell
git checkout <hardening_commit>
# clean tree
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
```

**Seam rerun:** This commit **does not** target Pass 08 `QualityBenchmarkViewModel` files; **seam filter rerun not required** for W8-C1 proof (compile + Quick only per bounded hardening).
