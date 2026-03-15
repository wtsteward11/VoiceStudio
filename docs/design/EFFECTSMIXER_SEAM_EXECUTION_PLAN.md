# EffectsMixer Seam Execution Plan

> **Purpose:** Step-by-step execution plan for EffectsMixerViewModel domain split (Option C).  
> **Source:** [EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md](EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md) Option C.  
> **Related:** [IBACKENDCLIENT_UNRESOLVED_QUEUE.md](IBACKENDCLIENT_UNRESOLVED_QUEUE.md), [EffectChainActions.cs](../../src/VoiceStudio.App/Services/UndoableActions/EffectChainActions.cs)

---

## 1. Prerequisites (from EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md §6)

| Prerequisite | Status |
|--------------|--------|
| Lifecycle hardening (Task 3.2) | **DONE** — ContinueWith replaced with proper async + CTS + staleness guard; IDisposable; _pollingCts cancelled in Dispose |
| EffectChainActions update | **Required** — CreateEffectChainAction and DeleteEffectChainAction must accept `IEffectChainClient` before or during Slice 2 |
| Seam tests | **Required** — EffectsMixerViewModelSeamTests, lifecycle tests |

---

## 2. Execution Sequence (Option C — 3 Slices)

| Slice | Seam | Methods | Rationale |
|-------|------|---------|------------|
| 1 | IEffectsMeterClient | GetAudioMetersAsync | Read-only, independent, smallest blast radius |
| 2 | IEffectChainClient | GetEffectChainsAsync, CreateEffectChainAsync, DeleteEffectChainAsync, UpdateEffectChainAsync, ProcessAudioWithChainAsync, GetEffectPresetsAsync | Enables EffectChainActions migration |
| 3 | IMixerStateClient | GetMixerStateAsync, UpdateMixerStateAsync, ResetMixerStateAsync, GetMixerPresetsAsync, CreateMixerPresetAsync, ApplyMixerPresetAsync, Create/Delete/Update Send/Return/SubGroup | Mixer graph as one domain |

---

## 3. Slice 1: IEffectsMeterClient

### 3.1 Steps

1. Add `IEffectsMeterClient` in `src/VoiceStudio.Core/Services/` with `Task<AudioMetersResponse> GetAudioMetersAsync(string? projectId, string? audioId, CancellationToken ct = default)`.
2. Add `EffectsMeterClient` in `src/VoiceStudio.App/Services/` implementing `IEffectsMeterClient` (delegate to `IBackendClient.GetAudioMetersAsync`).
3. In `EffectsMixerViewModel`: inject `IEffectsMeterClient`; replace `_backendClient.GetAudioMetersAsync` with `_effectsMeterClient.GetAudioMetersAsync`.
4. Update View (EffectsMixerView or panel registration) to resolve `GetEffectsMeterClient()` for the ViewModel.
5. Register `IEffectsMeterClient` → `EffectsMeterClient` in AppServices, ServiceProvider.

### 3.2 Notes

- **Do not** add EffectsMixerViewModel to MIGRATED_NO_IBACKENDCLIENT after Slice 1 — it still uses IBackendClient for effect chains and mixer.
- Max 3 files per commit (non-negotiable). Slice 1 may require multiple commits: e.g., Commit 1: IEffectsMeterClient + EffectsMeterClient; Commit 2: EffectsMixerViewModel + View + registration.

### 3.3 Verification

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — PASS
- `python scripts/ci/check_ibackendclient_creep.py` — PASS
- EffectsMixerViewModelSeamTests (when added) — no regression

---

## 4. Slice 2: IEffectChainClient + EffectChainActions

### 4.1 EffectChainActions Migration (before or with Slice 2)

- [EffectChainActions.cs](../../src/VoiceStudio.App/Services/UndoableActions/EffectChainActions.cs): `CreateEffectChainAction` and `DeleteEffectChainAction` currently take `IBackendClient`. Change to `IEffectChainClient`.
- Call sites: EffectsMixerViewModel passes backend to these actions. After Slice 2, pass `_effectChainClient`.
- **No fake IEffectsMixerClient mega-facade.**

### 4.2 IEffectChainClient Steps

1. Add `IEffectChainClient` in `src/VoiceStudio.Core/Services/` with:
   - GetEffectChainsAsync
   - CreateEffectChainAsync
   - DeleteEffectChainAsync
   - UpdateEffectChainAsync
   - ProcessAudioWithChainAsync
   - GetEffectPresetsAsync
2. Add `EffectChainClient` in `src/VoiceStudio.App/Services/` implementing `IEffectChainClient`.
3. In `EffectsMixerViewModel`: inject `IEffectChainClient`; replace all effect-chain `_backendClient` calls with `_effectChainClient`.
4. Update CreateEffectChainAction and DeleteEffectChainAction to accept `IEffectChainClient`; update EffectsMixerViewModel call sites to pass `_effectChainClient`.
5. Register in AppServices, ServiceProvider.

### 4.3 Verification

- Build, creep check, seam tests. No regression.

---

## 5. Slice 3: IMixerStateClient

### 5.1 Steps

1. Add `IMixerStateClient` in `src/VoiceStudio.Core/Services/` with:
   - GetMixerStateAsync, UpdateMixerStateAsync, ResetMixerStateAsync
   - GetMixerPresetsAsync, CreateMixerPresetAsync, ApplyMixerPresetAsync
   - CreateMixerSendAsync, CreateMixerReturnAsync, CreateMixerSubGroupAsync
   - DeleteMixerSubGroupAsync, DeleteMixerSendAsync, DeleteMixerReturnAsync
   - UpdateMixerSubGroupAsync, UpdateMixerSendAsync, UpdateMixerReturnAsync
2. Add `MixerStateClient` in `src/VoiceStudio.App/Services/` implementing `IMixerStateClient`.
3. In `EffectsMixerViewModel`: inject `IMixerStateClient`; replace all mixer `_backendClient` calls with `_mixerStateClient`.
4. Remove `IBackendClient` from EffectsMixerViewModel constructor.
5. Register in AppServices, ServiceProvider.
6. Add EffectsMixerViewModel to MIGRATED_NO_IBACKENDCLIENT (baseline).

### 5.2 Verification

- Build, creep check, seam tests. No regression.
- `python scripts/ci/check_ibackendclient_creep.py` — PASS (EffectsMixer removed from unresolved).

---

## 6. Commit Discipline

- Max 3 files per commit. Split slices into multiple commits if needed. One logical slice can span 2–3 commits.
- Slice 1 commit split: Commit 1: `IEffectsMeterClient` (Core) + `EffectsMeterClient` (App); Commit 2: EffectsMixerViewModel injection + View/registration + AppServices.
- Message format: `feat(seam): EffectsMixer Slice N — IEffectsMeterClient | IEffectChainClient | IMixerStateClient`

---

## 7. Verification Per Slice

| Check | Command |
|-------|---------|
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` |
| Creep | `python scripts/ci/check_ibackendclient_creep.py` |
| Seam tests | `dotnet test ... --filter "FullyQualifiedName~EffectsMixerViewModelSeamTests"` |
| Full verification | `python scripts/run_verification.py` (after commit) |

---

## Changelog

- 2026-03-15: Slice 3 complete. EffectsMixerViewModel migrated to IEffectsMeterClient + IEffectChainClient + IMixerStateClient; IBackendClient removed. Added to MIGRATED_NO_IBACKENDCLIENT.
- 2026-03-14: Initial execution plan. Option C (3 slices). No IEffectsMixerClient mega-facade.
