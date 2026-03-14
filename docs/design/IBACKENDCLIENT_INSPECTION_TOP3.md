# IBackendClient Inspection — Top 3 Unresolved Targets

> **Purpose:** File-level inspection for the top 3 true unresolved IBackendClient consumers. No migration starts without this sheet.  
> **Source:** Regenerated from `python scripts/ci/generate_ibackendclient_queue.py` (2026-03-14).  
> **Related:** [IBACKENDCLIENT_UNRESOLVED_QUEUE.md](IBACKENDCLIENT_UNRESOLVED_QUEUE.md), [RETAINED_ASYNC_RULE.md](RETAINED_ASYNC_RULE.md)

---

## True Top 3 (by rank: daily-use, lifecycle, mutation)

1. **EffectsMixerViewModel** — Lifecycle hardened (2026-03-13); seam migration deferred per domain split (Option C). See Architecture Track in queue doc.
2. **VoiceQuickCloneViewModel** — Property-handler FAF (OnSelectedAudioFileChanged L101: AutoDetectSettingsAsync). CloneVoiceAsync. Core voice cloning workflow. Daily-use.
3. **WorkflowAutomationViewModel** — No constructor FAF. CreateWorkflowAsync, UpdateWorkflowAsync, ExecuteWorkflowAsync. Mutation: Create, Save, Run.

---

## Inspection Table

| Field | EffectsMixer | VoiceQuickCloneViewModel | WorkflowAutomationViewModel |
|-------|--------------|--------------------------|-----------------------------|
| **Backend call clusters** | GetAudioMetersAsync, GetEffectChainsAsync, GetEffectPresetsAsync, CreateEffectChainAsync, DeleteEffectChainAsync, ProcessAudioWithChainAsync, UpdateEffectChainAsync, GetMixerStateAsync, UpdateMixerStateAsync, ResetMixerStateAsync, GetMixerPresetsAsync, CreateMixerPresetAsync, ApplyMixerPresetAsync, Create/Delete/Update Send/Return/SubGroup | CloneVoiceAsync; AutoDetectSettingsAsync (local, no backend) | CreateWorkflowAsync, UpdateWorkflowAsync, ExecuteWorkflowAsync |
| **Lifecycle patterns** | Lifecycle hardened (IPanelLifecycle, IDisposable, _disposalCts, _selectionLoadCts, staleness guard) | Property-handler FAF (OnSelectedAudioFileChanged); no IPanelLifecycle | No constructor FAF; LoadTemplates sync; no IPanelLifecycle |
| **Destructive operations** | Create/Delete/Update effect chains, mixer state, presets, sends/returns/subgroups | CloneVoiceAsync (creates profile) | CreateWorkflow; UpdateWorkflow; ExecuteWorkflow |
| **Seam split recommendation** | IEffectsMeterClient, IEffectChainClient, IMixerStateClient (Option C) | IVoiceQuickCloneClient (CloneVoiceAsync) | IWorkflowAutomationClient (Create, Update, Execute) |

---

## Rank 1: EffectsMixerViewModel — Seam Migration Deferred

**File:** `src/VoiceStudio.App/Views/Panels/EffectsMixerViewModel.cs`

**Lifecycle hardened (2026-03-13).** Still uses IBackendClient; seam migration deferred per domain split (Option C: IEffectsMeterClient, IEffectChainClient, IMixerStateClient). See [IBACKENDCLIENT_UNRESOLVED_QUEUE.md](IBACKENDCLIENT_UNRESOLVED_QUEUE.md) § File-Level Inspection.

---

## Rank 2: VoiceQuickCloneViewModel — Inspection (2026-03-14)

**File:** `src/VoiceStudio.App/ViewModels/VoiceQuickCloneViewModel.cs`

**IBackendClient call sites:** CloneVoiceAsync (L257).

**Property-handler fire-and-forget (L101):**
```csharp
_ = AutoDetectSettingsAsync(CancellationToken.None);
```
In OnSelectedAudioFileChanged. AutoDetectSettingsAsync does not call backend (local file analysis). Per RETAINED_ASYNC_RULE: property-handler FAF requires cancellation ownership and staleness guard. Move to IPanelLifecycle or debounced command with CTS.

**Seam shape:** Single `IVoiceQuickCloneClient` (CloneVoiceAsync).

---

## Rank 3: WorkflowAutomationViewModel — Inspection (2026-03-14)

**File:** `src/VoiceStudio.App/Views/Panels/WorkflowAutomationViewModel.cs`

**IBackendClient call sites:** CreateWorkflowAsync (L205), UpdateWorkflowAsync (L200), ExecuteWorkflowAsync (L248, L298).

**No constructor fire-and-forget.** LoadTemplates() is sync (no backend). Commands invoke async methods.

**Seam shape:** `IWorkflowAutomationClient` (CreateWorkflowAsync, UpdateWorkflowAsync, ExecuteWorkflowAsync).

---

## Changelog

- 2026-03-14: Truth reset. Replaced HelpViewModel and EmotionStylePresetEditorViewModel (both MIGRATED) with VoiceQuickCloneViewModel and WorkflowAutomationViewModel. Top 3 from generator output. EffectsMixer remains Rank 1 (deferred).
- 2026-03-14: Queue regeneration (Task 2). Top 3: EffectsMixer (deferred), HelpViewModel, EmotionStylePresetEditorViewModel. File-level inspection added for HelpViewModel and EmotionStylePresetEditorViewModel.
- 2026-03-14: Truth reset. Removed VoiceMorphViewModel and TemplateLibraryViewModel (both MIGRATED). Ranks 2–3 set to TBD; full inspection deferred to Task 2 (queue regeneration).
- 2026-03-13: Phase 3 VoiceMorph inspection. Corrected: constructor FAF at L126-128; ListProjectAudioAsync → IProjectAudioClient (not IProjectsClient); model-only tests; full call-site table.
- 2026-03-13: Initial inspection sheet. Top 3 from generate_ibackendclient_queue.py. EffectsMixer deferred; TemplateLibrary recommended next.
- 2026-03-13: Doc sync. EffectsMixer lifecycle hardened; seam migration deferred per domain split (Option C).
