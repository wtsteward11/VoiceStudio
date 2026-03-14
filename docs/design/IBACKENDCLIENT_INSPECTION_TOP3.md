# IBackendClient Inspection — Top 3 Unresolved Targets

> **Purpose:** File-level inspection for the top 3 true unresolved IBackendClient consumers. No migration starts without this sheet.  
> **Source:** Generated from `python scripts/ci/generate_ibackendclient_queue.py` (2026-03-13).  
> **Related:** [IBACKENDCLIENT_UNRESOLVED_QUEUE.md](IBACKENDCLIENT_UNRESOLVED_QUEUE.md), [RETAINED_ASYNC_RULE.md](RETAINED_ASYNC_RULE.md)

---

## True Top 3 (by rank: daily-use, lifecycle, mutation)

1. **EffectsMixerViewModel** — Rank 1, lifecycle hardened (2026-03-13); seam migration deferred per domain split
2. **TemplateLibraryViewModel** — Rank 7, MIGRATED (2026-03-13)
3. **VoiceMorphViewModel** — Rank 8, recommended next

---

## Inspection Table

| Field | EffectsMixer | TemplateLibrary | VoiceMorph |
|-------|--------------|-----------------|------------|
| **Backend call clusters** | GetAudioMetersAsync, GetEffectChainsAsync, GetEffectPresetsAsync, CreateEffectChainAsync, DeleteEffectChainAsync, ProcessAudioWithChainAsync, UpdateEffectChainAsync, GetMixerStateAsync, UpdateMixerStateAsync, ResetMixerStateAsync, GetMixerPresetsAsync, CreateMixerPresetAsync, ApplyMixerPresetAsync, Create/Delete/Update Send/Return/SubGroup | SendRequestAsync (GetTemplates, CreateTemplate, UpdateTemplate, DeleteTemplate, ApplyTemplate, GetCategories) | SendRequestAsync (GetConfigs, CreateConfig, UpdateConfig, DeleteConfig, ApplyMorph), ListProjectAudioAsync |
| **Lifecycle patterns** | Lifecycle hardened (IPanelLifecycle, IDisposable, _disposalCts, _selectionLoadCts, staleness guard) | MIGRATED: IPanelLifecycle, OnActivatedAsync, IDispatcherTimer debounce | No constructor FAF; command-driven loads |
| **Destructive operations** | Create/Delete/Update effect chains, mixer state, presets, sends/returns/subgroups | CreateTemplate, UpdateTemplate, DeleteTemplate, ApplyTemplate | CreateConfig, UpdateConfig, DeleteConfig, ApplyMorph |
| **Undo/redo coupling** | EffectChainActions holds _backendClient | TemplateActions hold ITemplateLibraryClient (migrated) | None (no UndoRedo) |
| **UndoableActions dependency** | EffectChainActions needs IEffectChainClient (or equivalent) | TemplateActions needs ITemplateLibraryClient | N/A |
| **Seam split recommendation** | Consider: IEffectsMeterClient, IEffectChainClient, IMixerStateClient | Single ITemplateLibraryClient (cohesive template CRUD) | IVoiceMorphClient; ListProjectAudioAsync → IProjectsClient (already has it, but uses _backendClient for it) |
| **Test status** | No ViewModel seam tests | Model + seam tests (TemplateLibraryViewModelSeamTests.cs) | VoiceMorphViewModelTests (transport-mock or legacy) |
| **Migration proof requirement** | Seam tests, lifecycle tests, EffectChainActions update | Seam tests, fix constructor FAF, TemplateActions update | Seam tests |

---

## Rank 1: EffectsMixerViewModel — Seam Migration Deferred

**File:** `src/VoiceStudio.App/Views/Panels/EffectsMixerViewModel.cs`

**Lifecycle hardened (2026-03-13).** Still uses IBackendClient; seam migration deferred per domain split (Option C: IEffectsMeterClient, IEffectChainClient, IMixerStateClient). See [IBACKENDCLIENT_UNRESOLVED_QUEUE.md](IBACKENDCLIENT_UNRESOLVED_QUEUE.md) § File-Level Inspection.

---

## Rank 2: TemplateLibraryViewModel — DONE (2026-03-13)

**File:** `src/VoiceStudio.App/ViewModels/TemplateLibraryViewModel.cs`

**Status:** **MIGRATED** to `ITemplateLibraryClient`. IPanelLifecycle implemented; OnActivatedAsync for initial load; constructor fire-and-forget removed. Debounce uses IDispatcherTimer. TemplateActions updated to ITemplateLibraryClient. Seam tests in `TemplateLibraryViewModelSeamTests.cs`.

---

## Rank 3: VoiceMorphViewModel

**File:** `src/VoiceStudio.App/ViewModels/VoiceMorphViewModel.cs`

**Notes:** Already has IProjectsClient, IProfilesClient. IBackendClient used for morph config CRUD and ListProjectAudioAsync. ListProjectAudioAsync should use IProjectsClient or IProjectAudioClient. Single IVoiceMorphClient for config CRUD + ApplyMorph.

---

## Changelog

- 2026-03-13: Initial inspection sheet. Top 3 from generate_ibackendclient_queue.py. EffectsMixer deferred; TemplateLibrary recommended next.
- 2026-03-13: Doc sync. EffectsMixer lifecycle hardened; seam migration deferred per domain split (Option C).
