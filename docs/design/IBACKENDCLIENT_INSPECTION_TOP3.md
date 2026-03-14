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
| **Lifecycle patterns** | Lifecycle hardened (IPanelLifecycle, IDisposable, _disposalCts, _selectionLoadCts, staleness guard) | MIGRATED: IPanelLifecycle, OnActivatedAsync, IDispatcherTimer debounce | Constructor FAF (L126-128); no IPanelLifecycle |
| **Destructive operations** | Create/Delete/Update effect chains, mixer state, presets, sends/returns/subgroups | CreateTemplate, UpdateTemplate, DeleteTemplate, ApplyTemplate | CreateConfig, UpdateConfig, DeleteConfig, ApplyMorph |
| **Undo/redo coupling** | EffectChainActions holds _backendClient | TemplateActions hold ITemplateLibraryClient (migrated) | None (no UndoRedo) |
| **UndoableActions dependency** | EffectChainActions needs IEffectChainClient (or equivalent) | TemplateActions needs ITemplateLibraryClient | N/A |
| **Seam split recommendation** | Consider: IEffectsMeterClient, IEffectChainClient, IMixerStateClient | Single ITemplateLibraryClient (cohesive template CRUD) | IVoiceMorphClient (config CRUD + ApplyMorph); ListProjectAudioAsync → IProjectAudioClient |
| **Test status** | No ViewModel seam tests | Model + seam tests (TemplateLibraryViewModelSeamTests.cs) | Model-only (VoiceMorphModelTests); no ViewModel seam tests |
| **Migration proof requirement** | Seam tests, lifecycle tests, EffectChainActions update | Seam tests, fix constructor FAF, TemplateActions update | Seam tests, fix constructor FAF, IPanelLifecycle, add IProjectAudioClient |

---

## Rank 1: EffectsMixerViewModel — Seam Migration Deferred

**File:** `src/VoiceStudio.App/Views/Panels/EffectsMixerViewModel.cs`

**Lifecycle hardened (2026-03-13).** Still uses IBackendClient; seam migration deferred per domain split (Option C: IEffectsMeterClient, IEffectChainClient, IMixerStateClient). See [IBACKENDCLIENT_UNRESOLVED_QUEUE.md](IBACKENDCLIENT_UNRESOLVED_QUEUE.md) § File-Level Inspection.

---

## Rank 2: TemplateLibraryViewModel — DONE (2026-03-13)

**File:** `src/VoiceStudio.App/ViewModels/TemplateLibraryViewModel.cs`

**Status:** **MIGRATED** to `ITemplateLibraryClient`. IPanelLifecycle implemented; OnActivatedAsync for initial load; constructor fire-and-forget removed. Debounce uses IDispatcherTimer. TemplateActions updated to ITemplateLibraryClient. Seam tests in `TemplateLibraryViewModelSeamTests.cs`.

---

## Rank 3: VoiceMorphViewModel — Phase 3 Inspection (2026-03-13)

**File:** `src/VoiceStudio.App/ViewModels/VoiceMorphViewModel.cs`

**IBackendClient call sites:**

| Method | Endpoint / Call | Destructive |
|--------|-----------------|-------------|
| LoadConfigsAsync | GET `/api/voice-morph/configs` | No |
| CreateConfigAsync | POST `/api/voice-morph/configs` | Yes |
| UpdateConfigAsync | PUT `/api/voice-morph/configs/{id}` | Yes |
| DeleteConfigAsync | DELETE `/api/voice-morph/configs/{id}` | Yes |
| ApplyMorphAsync | POST `/api/voice-morph/apply` | Yes |
| LoadAudioFilesAsync | `_backendClient.ListProjectAudioAsync(project.Id, ct)` | No |

**Constructor fire-and-forget (L126-128):**
```csharp
_ = LoadConfigsAsync(CancellationToken.None);
_ = LoadAudioFilesAsync(CancellationToken.None);
_ = LoadVoiceProfilesAsync(CancellationToken.None);
```
Must be removed. Move initial loads to `IPanelLifecycle.OnActivatedAsync`.

**Seam shape recommendation:**
- **IVoiceMorphClient:** GetConfigsAsync, CreateConfigAsync, UpdateConfigAsync, DeleteConfigAsync, ApplyMorphAsync (thin pass-through)
- **IProjectAudioClient:** Already exists; add to constructor; replace `_backendClient.ListProjectAudioAsync` in LoadAudioFilesAsync
- **IProjectsClient, IProfilesClient:** Keep (already used for GetProjectsAsync, GetProfilesAsync)

**Undo/redo:** None. No TemplateActions-style coupling.

**Tests:** `VoiceMorphViewModelTests.cs` → class `VoiceMorphModelTests` (model-only: VoiceBlendItem, MorphConfigItem, MorphConfig, VoiceBlend). No ViewModel seam tests. Add `VoiceMorphViewModelSeamTests.cs` with constructor no-call, IPanelLifecycle, OnActivatedAsync.

**View wiring:** `VoiceMorphView.xaml.cs` uses `ServiceProvider.GetBackendClient()`; will need `GetVoiceMorphClient()` and `GetProjectAudioClient()` after migration.

---

## Changelog

- 2026-03-13: Phase 3 VoiceMorph inspection. Corrected: constructor FAF at L126-128; ListProjectAudioAsync → IProjectAudioClient (not IProjectsClient); model-only tests; full call-site table.
- 2026-03-13: Initial inspection sheet. Top 3 from generate_ibackendclient_queue.py. EffectsMixer deferred; TemplateLibrary recommended next.
- 2026-03-13: Doc sync. EffectsMixer lifecycle hardened; seam migration deferred per domain split (Option C).
