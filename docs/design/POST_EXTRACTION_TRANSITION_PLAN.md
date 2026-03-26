# Post-Extraction Transition Plan

**Purpose:** Define the next active engineering lane now that BackendClient extraction is paused.  
**Date:** 2026-03-24  
**Related:** [BACKENDCLIENT_REMAINDER_INVENTORY.md](BACKENDCLIENT_REMAINDER_INVENTORY.md), [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md)

---

## 1. Why Extraction Is Paused

Per [BACKENDCLIENT_REMAINDER_INVENTORY.md](BACKENDCLIENT_REMAINDER_INVENTORY.md) § Explicit Decision:

1. **Leverage:** After PR-13–PR-17, remaining thin-client clusters (Profiles, Projects, Transcription, Settings) each require pipeline migration + DI + caller sweep. No cluster clears "≥5% of remaining methods" with low fragmentation cost. Quality would clear the bar but fails C5 (huge, IDEA-* endpoints) as a hard stop.

2. **Fragmentation cost:** Extracting Project audio (3 methods), Ensemble (2), or Batch lifecycle (4) adds new client types for <5% reduction. The "complete thin client" exception applies to Profiles, Projects, Transcription, Settings — but each has high blast radius (10+ ViewModels).

3. **Sparse caller risk:** Ensemble, Channel routing have 1–2 callers. Extraction would add types without meaningful coupling reduction.

4. **DTO-glue risk:** ProfilesClient, ProjectsClient are thin delegators. Migrating to pipeline does not reduce DTO knowledge — callers still depend on same models.

5. **Cross-cutting:** Voice, Audio retrieval, Audio export, Upload helpers, Quality are cross-cutting or core-path. Stop.

**Conclusion:** Pause PR-18 extraction. Revisit when product reasons justify the migration cost.

---

## 2. What Work Is Now Prioritized Instead

| Category | Scope |
|----------|-------|
| **Product/feature wiring** | Cross-panel workflows, inter-feature flows, profile → synthesis → timeline coherence |
| **Reliability hardening** | Startup orchestration, transport ownership, backend seam stability |
| **UX coherence / cross-panel integration** | Workflow flows, selection propagation, follow-selection, transport UX |
| **Test and proof quality** | Golden path, workflow E2E where product-facing |

---

## 3. Re-entry Triggers for Future Extraction

Lifted from [BACKENDCLIENT_REMAINDER_INVENTORY.md](BACKENDCLIENT_REMAINDER_INVENTORY.md) § Re-entry Rule.

Extraction should be resumed when one or more of the following applies:

- **Product requirement changes:** A new feature or refactor demands domain isolation (e.g., a bounded caller cluster emerges for a new panel).
- **Monolith residue blocker:** Remaining IBackendClient methods block UX, performance, or maintainability (e.g., profiling shows coupling bottleneck).
- **Thin-client migration request:** Product or architecture decision explicitly requests completing a thin-client migration (Profiles, Projects, Transcription, Settings, Project audio).
- **Stop criteria re-assessment:** A cluster that previously failed leverage/fragmentation thresholds gains enough callers or a cleaner endpoint family to clear the bar.

Do **not** resume extraction purely for purity or momentum. The pause is intentional.

---

## 4. Sequencing for the Next 2–3 Workstreams

| Order | Workstream | Description |
|-------|------------|-------------|
| 1 | **Cross-feature workflow coherence pass** | Highest value. Use [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md) to scope bounded passes. Start with Profile → synthesis → timeline. |
| 2 | **Reliability hardening** | Startup orchestration, transport ownership, backend readiness gates. |
| 3 | **UX coherence / panel integration** | Selection broadcast, follow-selection, transport UX consistency. |

---

## 5. Proof Expectations

The post-extraction phase inherits the same proof discipline as the extraction wave. No "latest" placeholder; actual artifact path only.

### Workflow coherence passes

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → PASS
- Targeted tests for affected ViewModels/services
- Manual workflow proof (steps documented in task brief or scope doc)
- `.\scripts\verify.ps1 -Quick`
- Record actual artifact path in STATE.md and task brief (e.g. `artifacts/verify/YYYYMMDD_HHMMSS/verification_report.md`)

### Reliability hardening passes

- Same pattern: build, relevant integration tests, verify.ps1 -Quick, artifact path

### UX wiring passes

- Build; UI smoke or E2E where applicable; verify.ps1 -Quick; artifact path

---

## Summary

| Idea | Key details |
|------|-------------|
| **Extraction paused** | Stop criteria applied; remainder stays on monolith until product reasons justify cost |
| **Next lane** | Workflow coherence → reliability → UX |
| **Re-entry** | Product requirement, monolith blocker, explicit migration request, or stop-criteria re-assessment |
| **Proof discipline** | Build, tests, verify.ps1 -Quick, actual artifact path — same as extraction wave |
