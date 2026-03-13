# EffectsMixer Domain Split Analysis

> **Purpose:** Design-before-implementation analysis for EffectsMixerViewModel seam migration. No migration starts until lifecycle hardening (Task 3.2) and this design is approved.  
> **Source:** Assessment Remediation Plan Task 3.1.  
> **Related:** [IBACKENDCLIENT_INSPECTION_TOP3.md](IBACKENDCLIENT_INSPECTION_TOP3.md), [RETAINED_ASYNC_RULE.md](RETAINED_ASYNC_RULE.md), [EffectChainActions.cs](../../src/VoiceStudio.App/Services/UndoableActions/EffectChainActions.cs)

---

## 1. Call-to-Domain Mapping

All `_backendClient.*` calls in `EffectsMixerViewModel.cs` mapped to domains:

| Domain | Backend Calls | Line refs | Purpose |
|--------|---------------|-----------|---------|
| **Meters** | `GetAudioMetersAsync` | 536 | Real-time meter values; used by `PollMetersAsync` loop |
| **Effect chains** | `GetEffectChainsAsync`, `CreateEffectChainAsync`, `DeleteEffectChainAsync`, `UpdateEffectChainAsync`, `ProcessAudioWithChainAsync`, `GetEffectPresetsAsync` | 618, 660, 713, 769, 839, 1136 | Effect chain CRUD, presets, audio processing |
| **Mixer state** | `GetMixerStateAsync`, `UpdateMixerStateAsync`, `ResetMixerStateAsync` | 1337, 1435, 1471 | Master mixer state (channels, sends, returns, subgroups) |
| **Mixer presets** | `GetMixerPresetsAsync`, `CreateMixerPresetAsync`, `ApplyMixerPresetAsync` | 1525, 1570, 1600 | Save/load mixer snapshots |
| **Mixer routing** | `CreateMixerSendAsync`, `CreateMixerReturnAsync`, `CreateMixerSubGroupAsync`, `DeleteMixerSubGroupAsync`, `UpdateMixerSubGroupAsync`, `UpdateMixerSendAsync`, `UpdateMixerReturnAsync`, `DeleteMixerSendAsync`, `DeleteMixerReturnAsync` | 1663, 1704, 1747, 1778, 1820, 1855, 1890, 1925, 1964 | Sends, returns, subgroups CRUD |

---

## 2. Domain Cohesion Assessment

| Domain | Cohesion | Coupling to others | Mutation surface |
|--------|----------|--------------------|------------------|
| Meters | High — read-only, polling loop | None (SelectedAudioId only) | None |
| Effect chains | High — CRUD + process | SelectedProjectId, SelectedAudioId | Create, Delete, Update, Process |
| Mixer state | High — get/update/reset | SelectedProjectId | Update, Reset |
| Mixer presets | Medium — depends on mixer state shape | SelectedProjectId, mixer state | Create, Apply |
| Mixer routing | High — CRUD for sends/returns/subgroups | SelectedProjectId | Create, Delete, Update |

**Observation:** Mixer state, presets, and routing are tightly related (all operate on the same mixer graph). Effect chains are a separate concern (per-project effect processing). Meters are independent (real-time visualization).

---

## 3. Options Considered

### Option A: Single seam — `IEffectsMixerClient`

One interface aggregating all 5 domains.

**Pros:**
- Fewer new types; simpler DI registration
- Single migration pass
- Matches current `_backendClient` usage pattern

**Cons:**
- Recreates a "smaller mega-client" — assessment verdict: "may be trash"
- No policy depth; thin rename only
- UndoRedo (`EffectChainActions`) would need the full interface for effect-chain ops only
- Testing: must mock entire surface for any EffectsMixer test

### Option B: Multiple seams — `IEffectsMeterClient`, `IEffectChainClient`, `IMixerClient`

Split by domain.

**Pros:**
- ISP: EffectChainActions gets only `IEffectChainClient`
- Clear boundaries; each seam has one reason to change
- Testability: mock only the domain under test

**Cons:**
- More interfaces, more DI wiring
- Mixer state + presets + routing could argue for one `IMixerClient` (they share project context and mixer graph)

### Option C: Three seams — `IEffectsMeterClient`, `IEffectChainClient`, `IMixerStateClient`

- **IEffectsMeterClient:** GetAudioMetersAsync (meters only)
- **IEffectChainClient:** All effect chain + preset calls
- **IMixerStateClient:** Mixer state, presets, sends/returns/subgroups (unified mixer domain)

**Pros:**
- Meters isolated (polling, read-only)
- Effect chains isolated (undo/redo, processing)
- Mixer as one cohesive unit (state + presets + routing share the same graph)
- EffectChainActions needs only IEffectChainClient

**Cons:**
- IMixerStateClient is still broad (state + presets + routing) — but they are logically one "mixer" domain

---

## 4. Decision

**Recommend Option C: Three seams.**

| Seam | Methods | Rationale |
|------|---------|-----------|
| `IEffectsMeterClient` | GetAudioMetersAsync | Read-only, polling; no mutation; independent |
| `IEffectChainClient` | GetEffectChainsAsync, CreateEffectChainAsync, DeleteEffectChainAsync, UpdateEffectChainAsync, ProcessAudioWithChainAsync, GetEffectPresetsAsync | Effect chain CRUD + processing; UndoRedo coupling |
| `IMixerStateClient` | GetMixerStateAsync, UpdateMixerStateAsync, ResetMixerStateAsync, GetMixerPresetsAsync, CreateMixerPresetAsync, ApplyMixerPresetAsync, Create/Delete/Update Send/Return/SubGroup | Mixer graph as one domain; state, presets, routing are cohesive |

**Why not Option A:** A single `IEffectsMixerClient` would be a thin rename with no architectural benefit. Assessment: "recreates the problem with a new name."

**Why not Option B with 5 seams:** Mixer state, presets, and routing operate on the same conceptual mixer graph. Splitting them adds complexity without clear boundary benefit.

---

## 5. EffectChainActions Dependency

`EffectChainActions.cs` holds `IBackendClient` in:
- `CreateEffectChainAction`
- `DeleteEffectChainAction`

**Migration requirement:** Both must accept `IEffectChainClient` instead of `IBackendClient`. The actions only need effect-chain operations; they do not need meter or mixer APIs. This satisfies ISP and completes the EffectsMixer migration.

---

## 6. Prerequisites (No Migration Before These)

1. **Lifecycle hardening** (Task 3.2): Replace `ContinueWith` with proper async + CTS + staleness guard; add `IDisposable`; cancel `_pollingCts` in `Dispose`.
2. **EffectChainActions update:** Add `IEffectChainClient`; refactor CreateEffectChainAction and DeleteEffectChainAction to accept it.
3. **Seam tests:** EffectsMixerViewModelSeamTests, lifecycle tests.

---

## Changelog

- 2026-03-13: Initial analysis. Option C (three seams) recommended. EffectChainActions must accept IEffectChainClient.
