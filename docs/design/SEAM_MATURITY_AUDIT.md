# Seam Maturity Audit

> **Source:** Mid-Stage Architecture Compression Plan (2026-03-11)  
> **Purpose:** Honest classification of extracted seams; avoid fake modularity.

---

## Scope

Seams that sit between ViewModels/Panels and `IBackendClient`. Each is classified by actual behavior, not aspiration.

---

## Category Definitions

| Category | Definition |
|----------|------------|
| **Client** | Thin transport; no policy. Pass-through to backend. |
| **Gateway** | Routes to multiple backends/adapters; aggregates or multiplexes. |
| **Adapter** | Wraps external system with minimal translation. |
| **Policy-owning Service** | Owns defaults, normalization, retry/caching, orchestration, or business rules. |

---

## Seam Inventory

| Seam | Category | Rationale | Naming Recommendation |
|------|----------|-----------|------------------------|
| ABTestService | Client | Pure pass-through; 2 methods delegate to IBackendClient | Rename to ABTestClient or keep |
| VoiceSynthesisService | Client (candidate for Service) | Pure pass-through; central workflow; deepen in Task 1.2 | Keep; deepen later |
| TimelineTrackService | Policy-owning Service | OrderBy TrackNumber/Name; default track naming via GenerateDefaultTrackNameAsync | Keep |
| ProjectAudioClient | Policy-owning Client | Filename validation; dedup guard on save; list/save consistency | Keep (honest) |
| TimelineTranscriptionService | Policy-owning Service | Null/empty Segments normalization; never return null | Keep |
| TimelineClipService | Client | Pure pass-through; CreateClipAsync, DeleteClipAsync | Rename to TimelineClipClient or keep |
| EnginesClient | Client | Delegates to GetEnginesAsync; single-flight/TTL in BackendClient | Keep |
| ProfilesClient | Policy-owning Client | IRequestCoordinator; single-flight + TTL for profiles | Keep |
| ProjectsClient | Policy-owning Client | IRequestCoordinator; single-flight + TTL for projects | Keep |
| IEmotionStyleClient | Policy-owning Client | IRequestCoordinator; single-flight + TTL for emotions/styles; preset coalescing | Keep |
| IEmotionControlClient | Policy-owning Client | IRequestCoordinator; single-flight + TTL for list/presets; cache invalidation on create/delete | Keep |

---

## Naming Mismatches

| Current Name | Issue | Recommendation |
|--------------|-------|----------------|
| ABTestService | "Service" implies policy; none exists | ABTestClient |
| TimelineClipService | Pure delegator | TimelineClipClient (optional) |

---

## Next Steps

1. **Task 1.2** — DEFERRED. Deepen VoiceSynthesisService blocked by type location: VoiceSynthesisRequest/Response live in App.Core.Models; IVoiceSynthesisService/IBackendClient use Core.Models. Resolve type consolidation before adding request shaping/response normalization.
2. **Task 2.1** — DONE. IEmotionStyleClient added; EmotionStyleControlViewModel migrated.
3. **Task 2.2** — DONE (2026-03-11). IEmotionControlClient added; EmotionControlViewModel migrated.
4. Rename ABTestService → ABTestClient if desired (low priority).

---

## Changelog

- 2026-03-11: Initial audit per Mid-Stage Architecture Compression Plan.
- 2026-03-11: Added IEmotionControlClient; EmotionControlViewModel migrated (Mypy Reassess and Architecture Pivot Plan Phase 2).
