# GOV-VOICESTUDIO-WORKFLOW-COHERENCE-ADVANCED-01 — Workflow Coherence (Advanced)

## 0. Status

- **State:** **Closed** (2026-03-28) — Slices 1–4 complete; lane closure [VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_LANE_CLOSURE_2026-03-28.md](../reports/verification/VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_LANE_CLOSURE_2026-03-28.md)
- **Opened:** 2026-03-28
- **Owner:** Tyler + agent execution support
- **Predecessor lane:** `GOV-VOICESTUDIO-UNIFIED-STARTUP-01` (closed)

---

## 1. Objective (Frozen)

Make **professional integrated workflows** credible by **proving** and, only where required, **hardening**:

1. **Workflow A:** Profile selection → synthesis target coherence → timeline insertion → continuity for playback-related state.
2. **Workflow B:** Search result → panel open → item focus/selection with **honest** partial-failure UX.

This lane **does not** claim full WinUI E2E in CI where the Premium audit only requires stronger **deterministic VM/service** proof.

---

## 2. In Scope

- Dedicated MSTest proof for Workflow A (profile event → synthesis VM; **`AddToTimelineEvent` → timeline clip + selection**; **`SynthesisCompletedEvent` does not insert timeline clips** — explicit handoff authority per **GAP-025** / [GOV_VOICESTUDIO_GAP025_SYNTHESIS_TIMELINE_HANDOFF_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP025_SYNTHESIS_TIMELINE_HANDOFF_01_EXECUTION_ROW.md)).
- Workflow B: rely on and **index** existing `SearchOverlayCoordinatorTests` (no false success on selection failure); optional additive test only if a gap is found.
- Targeted user-facing strings for Workflow A when profile is missing on timeline insert (actionable copy already present — verified by tests).
- Minimal shell extraction **only** if Slice 1/2 cannot be proven without it.
- Proof artifacts + mandatory verification on each slice and at closure.

---

## 3. Out of Scope (Hard)

- Reopening `GOV-VOICESTUDIO-UNIFIED-STARTUP-01`.
- New `IBackendClient` / pipeline extraction waves (`BACKENDCLIENT_REMAINDER_INVENTORY` is not a mandate).
- Installer, commercialization, stash mining.
- Broad `MainWindow.xaml.cs` decomposition.
- Role 8 / intelligence-analyst artifacts.
- New Pass 05 / 06 / 07 / 08 matrix rows without product sign-off (see [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md)).

---

## 4. Baseline References

- [PREMIUM_SOFTWARE_COHERENCE_AUDIT.md](PREMIUM_SOFTWARE_COHERENCE_AUDIT.md) §7, §8, §10 S1 gaps (workflow proof, shell, errors).
- [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md) Workflow 1 & 3 (known leftovers vs closed passes).
- [IBACKENDCLIENT_UNRESOLVED_QUEUE.md](IBACKENDCLIENT_UNRESOLVED_QUEUE.md) — routine queue closed.

---

## 5. Slice Map (Frozen)

| Slice | Intent | Proof doc |
| --- | --- | --- |
| **1** | Workflow A deterministic proof + path map | [VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_SLICE1_PROOF_2026-03-28.md](../reports/verification/VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_SLICE1_PROOF_2026-03-28.md) |
| **2** | Workflow B acceptance + proof index | [VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_SLICE2_PROOF_2026-03-28.md](../reports/verification/VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_SLICE2_PROOF_2026-03-28.md) |
| **3** | Gated shell extraction | [VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_SLICE3_DECISION_2026-03-28.md](../reports/verification/VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_SLICE3_DECISION_2026-03-28.md) |
| **4** | Lane closure + governance sync | [VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_LANE_CLOSURE_2026-03-28.md](../reports/verification/VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_LANE_CLOSURE_2026-03-28.md) |

---

## 6. Slice 1 — Binary Acceptance (Workflow A)

All must be **PASS** for Slice 1 close:

1. **A1:** When `VoiceSynthesisViewModel` is activated and `Profiles` contains profile `P`, publishing `ProfileSelectedEvent` with `P` results in `SelectedProfile.Id == P` (deterministic on test dispatcher).
2. **A2:** When `TimelineViewModel` has `SelectedProject` and a target `AudioTrack`, publishing `AddToTimelineEvent` with non-empty `ProfileId` and `AudioId` adds exactly one `AudioClip` with matching `ProfileId` and `AudioId`.
3. **A3 (superseded 2026-04-02, GAP-025):** Publishing `SynthesisCompletedEvent` with a live `TimelineViewModel` **does not** add clips; timeline insertion is **only** via explicit `AddToTimelineEvent`. Proof: `SynthesisCompletedEvent_DoesNotInsertClip_Gap025ExplicitHandoffOnly` in `WorkflowCoherenceAdvancedTests`.
4. **A4:** After clip add, multi-select state includes the new clip id when using production `MultiSelectService` from `AppServices` (selection continuity).
5. **A5:** Mandatory build / test / CI commands pass on claim state (see §10).

**Honesty:** Timeline **panel** WinUI E2E and full `PlayAudioAsync` HTTP path are **not** part of Slice 1 automated proof; clip + selection + URLs on clip are the continuity boundary for this slice.

---

## 7. Slice 2 — Binary Acceptance (Workflow B)

All must be **PASS** for Slice 2 close:

1. **B1:** `SearchOverlayCoordinatorTests` includes: open success + `NavigateToItemAsync` true → success toast; `NavigateToItemAsync` false → **no** success toast, warning toast (selection failure).
2. **B2:** Coordinator passes search metadata to `INavigatablePanel` when present (existing test).
3. **B3:** Mandatory build / test / CI commands pass on claim state.

---

## 8. Slice 3 — Shell Extraction Gate

**Entry:** Slice 1 and 2 closed.

**Binary decision:**

- **Extract** only if a documented blocker shows `MainWindow.xaml.cs` prevents proving or fixing A/B.
- **Else:** Record **NOT REQUIRED** with evidence.

---

## 9. Slice 4 — Lane Closure

Lane closes when:

1. Slices 1–3 records complete.
2. Consolidated closure report §6 all **PASS**.
3. `.\scripts\verify.ps1 -Quick` **PASS** on closure commit.
4. `.cursor/STATE.md` and `CANONICAL_REGISTRY.md` agree with this row §0.

---

## 10. Mandatory Verification (Per Slice and Closure)

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
python -m pytest tests/ci/ -q --randomly-seed=12345
.\scripts\verify.ps1 -Quick   # lane closure authoritative harness
```

---

## 11. Rollback Triggers

- Workflow A or B regressions (profile not applied, clip not inserted, false success toast on search selection failure).
- Scope creep into startup, extraction waves, or MainWindow mega-refactor without Slice 3 gate.

---

## 12. Execution Records

### 12.1 Slice 1 — Closed (2026-03-28)

- **Proof:** `docs/reports/verification/VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_SLICE1_PROOF_2026-03-28.md`
- **Tests:** `WorkflowCoherenceAdvancedTests` in `src/VoiceStudio.App.Tests/ViewModels/`

### 12.2 Slice 2 — Closed (2026-03-28)

- **Proof:** `docs/reports/verification/VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_SLICE2_PROOF_2026-03-28.md`
- **Evidence:** existing `SearchOverlayCoordinatorTests` + index in proof doc

### 12.3 Slice 3 — Closed (2026-03-28) — NOT REQUIRED

- **Decision:** `docs/reports/verification/VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_SLICE3_DECISION_2026-03-28.md`

### 12.4 Slice 4 — Closed (2026-03-28)

- **Closure:** `docs/reports/verification/VOICESTUDIO_WORKFLOW_COHERENCE_ADVANCED_LANE_CLOSURE_2026-03-28.md`
