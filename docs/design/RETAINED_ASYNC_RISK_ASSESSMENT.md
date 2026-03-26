# Retained-Async Risk Assessment

**Date:** 2026-03-15  
**Purpose:** Assess which retained-async baseline entries are high-risk. Per Bulletproof Hardening Wave Gap 5: replace vague "if it hides real risk" with documented assessment.  
**Related:** [RETAINED_ASYNC_RULE.md](RETAINED_ASYNC_RULE.md), [RETAINED_ASYNC_EXEMPTIONS.md](RETAINED_ASYNC_EXEMPTIONS.md), [TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md](TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md)

---

## Risk Criteria

| Criterion | High Risk | Lower Risk |
|-----------|-----------|------------|
| **Selection-triggered** | Yes, without staleness guard | No, or has staleness guard |
| **Constructor FAF** | Yes (banned per ADR-047) | No |
| **Core user flow** | Synthesis, timeline, profiles, browser | Auxiliary panels |
| **Churn** | High (frequently edited) | Low |
| **Exemption documented** | No | Yes (RETAINED_ASYNC_EXEMPTIONS) |

---

## Top 5 Highest-Risk Entries

| Rank | ViewModel | Baseline Count | Risk Rationale |
|------|-----------|----------------|----------------|
| 1 | **VoiceSynthesisViewModel** | 7 | Core synthesis panel; high churn; selection/command-triggered loads; no documented staleness guard |
| 2 | **TimelineViewModel** | 6 | Core timeline; high churn; selection-triggered waveform/region loads; no documented staleness guard |
| 3 | **ProfilesViewModel** | 1 | Core profile selection; high churn; selection-triggered profile load |
| 4 | **VoiceBrowserViewModel** | 3 | Voice browser; selection-triggered; no documented staleness guard |
| 5 | **JobProgressViewModel** | 2 | Job progress; selection-triggered job detail load |

---

## Summary

- **High-risk (need staleness guard or remediation):** 5 ViewModels, 19 baseline entries. VoiceSynthesisViewModel and TimelineViewModel are the highest priority.
- **Acceptable per exemption:** TrainingViewModel (ADR-051), AdvancedRealTimeVisualizationViewModel (CONSTRUCTOR_INVARIANT_COVERAGE_AUDIT). Not in baseline.
- **Need staleness guard:** Selection-triggered loads in VoiceSynthesisViewModel, TimelineViewModel, ProfilesViewModel, VoiceBrowserViewModel, JobProgressViewModel. Per RETAINED_ASYNC_RULE: verify selection ID before applying async result.

---

## Release-Blocking?

**None are release-blocking** at this time. The baseline uses delta mode: no new violations. Existing violations are documented. Remediation is planned work; add staleness guards when touching these ViewModels (no-deferral-on-encounter rule).

---

## Changelog

- 2026-03-15: Initial assessment. Top 5 identified; no release blockers.
