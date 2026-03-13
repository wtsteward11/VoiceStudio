# IBackendClient Inspection — Top 3 Unresolved Targets

> **Purpose:** File-level inspection for the top 3 true unresolved IBackendClient consumers. No migration starts without this sheet.  
> **Source:** Generated from `python scripts/ci/generate_ibackendclient_queue.py` (2026-03-13).  
> **Related:** [IBACKENDCLIENT_UNRESOLVED_QUEUE.md](IBACKENDCLIENT_UNRESOLVED_QUEUE.md), [RETAINED_ASYNC_RULE.md](RETAINED_ASYNC_RULE.md)

---

## True Top 3 (by rank: daily-use, lifecycle, mutation)

1. **EffectsMixerViewModel** — Rank 1, DEFERRED until lifecycle hardened
2. **TemplateLibraryViewModel** — Rank 7, recommended next
3. **VoiceMorphViewModel** — Rank 8

---

## Inspection Table

| Field | EffectsMixer | TemplateLibrary | VoiceMorph |
|-------|--------------|-----------------|------------|
| **Backend call clusters** | GetAudioMetersAsync, GetEffectChainsAsync, GetEffectPresetsAsync, CreateEffectChainAsync, DeleteEffectChainAsync, ProcessAudioWithChainAsync, UpdateEffectChainAsync, GetMixerStateAsync, UpdateMixerStateAsync, ResetMixerStateAsync, GetMixerPresetsAsync, CreateMixerPresetAsync, ApplyMixerPresetAsync, Create/Delete/Update Send/Return/SubGroup | SendRequestAsync (GetTemplates, CreateTemplate, UpdateTemplate, DeleteTemplate, ApplyTemplate, GetCategories) | SendRequestAsync (GetConfigs, CreateConfig, UpdateConfig, DeleteConfig, ApplyMorph), ListProjectAudioAsync |
| **Lifecycle patterns** | ContinueWith, _pollingCts, no IDisposable, no staleness guard | Constructor FAF: _ = LoadCategoriesAsync/LoadTemplatesAsync(CancellationToken.None); _searchDebounceCts for debounce | No constructor FAF; command-driven loads |
| **Destructive operations** | Create/Delete/Update effect chains, mixer state, presets, sends/returns/subgroups | CreateTemplate, UpdateTemplate, DeleteTemplate, ApplyTemplate | CreateConfig, UpdateConfig, DeleteConfig, ApplyMorph |
| **Undo/redo coupling** | EffectChainActions holds _backendClient | TemplateActions (CreateTemplateAction, UpdateTemplateAction) hold _backendClient | None (no UndoRedo) |
| **UndoableActions dependency** | EffectChainActions needs IEffectChainClient (or equivalent) | TemplateActions needs ITemplateLibraryClient | N/A |
| **Seam split recommendation** | Consider: IEffectsMeterClient, IEffectChainClient, IMixerStateClient | Single ITemplateLibraryClient (cohesive template CRUD) | IVoiceMorphClient; ListProjectAudioAsync → IProjectsClient (already has it, but uses _backendClient for it) |
| **Test status** | No ViewModel seam tests | TemplateLibraryModelTests (model only); no ViewModel seam tests | VoiceMorphViewModelTests (transport-mock or legacy) |
| **Migration proof requirement** | Seam tests, lifecycle tests, EffectChainActions update | Seam tests, fix constructor FAF, TemplateActions update | Seam tests |

---

## Rank 1: EffectsMixerViewModel — DEFER

**File:** `src/VoiceStudio.App/Views/Panels/EffectsMixerViewModel.cs`

**Lifecycle risks:** OnSelectedProjectIdChanged, OnSelectedAudioIdChanged use `ContinueWith` — no `_disposalCts`, no staleness guard. No IDisposable. UndoRedo actions hold `_backendClient`.

**Recommendation:** Defer until lifecycle hardened (CTS ownership, disposal, staleness guard). See [IBACKENDCLIENT_UNRESOLVED_QUEUE.md](IBACKENDCLIENT_UNRESOLVED_QUEUE.md) § File-Level Inspection.

---

## Rank 2: TemplateLibraryViewModel — Next Target

**File:** `src/VoiceStudio.App/ViewModels/TemplateLibraryViewModel.cs`

**Blockers before migration:**
- Constructor fire-and-forget: `_ = LoadCategoriesAsync(CancellationToken.None); _ = LoadTemplatesAsync(CancellationToken.None);` — must move to IPanelLifecycle.OnActivatedAsync
- TemplateActions (UndoableActions) holds IBackendClient — must accept ITemplateLibraryClient

**Seam:** ITemplateLibraryClient — GetTemplatesAsync, CreateTemplateAsync, UpdateTemplateAsync, DeleteTemplateAsync, ApplyTemplateAsync, GetCategoriesAsync.

---

## Rank 3: VoiceMorphViewModel

**File:** `src/VoiceStudio.App/ViewModels/VoiceMorphViewModel.cs`

**Notes:** Already has IProjectsClient, IProfilesClient. IBackendClient used for morph config CRUD and ListProjectAudioAsync. ListProjectAudioAsync should use IProjectsClient or IProjectAudioClient. Single IVoiceMorphClient for config CRUD + ApplyMorph.

---

## Changelog

- 2026-03-13: Initial inspection sheet. Top 3 from generate_ibackendclient_queue.py. EffectsMixer deferred; TemplateLibrary recommended next.
