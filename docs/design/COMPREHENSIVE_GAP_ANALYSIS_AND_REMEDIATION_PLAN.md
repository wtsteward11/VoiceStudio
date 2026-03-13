# Comprehensive Gap Analysis and Remediation Plan

**Date**: 2026-03-11  
**Author**: Ruthless Mentor / Senior Architect Audit  
**Purpose**: Verify work completed vs. work remaining; log gaps; create fix plan.  
**Canonical**: Per document-lifecycle.mdc; update CANONICAL_REGISTRY on approval.

---

## 1. Executive Summary

| Category | Status | Gaps | Priority |
|----------|--------|------|----------|
| Roadmap v2.0 | Phase F COMPLETE | One Metric (CI real golden path) not continuous | P1 |
| Workflow Consolidation | 8/8 slices done for 5 workflows | 30+ jobs still on raw setup-python | P2 |
| Mypy Burn-Down | 18 slices done | ~78 backend/services/*.py remain | P2 |
| Skip Debt | 7 batches done; policy compliant | Target 200 vs. budget 1765; no reduction | P3 |
| Seam Maturity | Audit complete | Task 1.2 DEFERRED; ABTestService rename optional | P3 |
| Phase F Anti-Theater | Discovery done | Implementation steps C2/C3 (golden path output) unclear | P2 |
| Document Governance | Registry current | 3 DEFERRED items; no Rule Proposal Template | P4 |

---

## 2. Work Completed (Verified)

### 2.1 Roadmap v2.0 — Phases 0 through F

| Phase | Status | Proof |
|-------|--------|-------|
| Phase 0 | DONE | 5 CI invariants (I-1 through I-5) |
| Phase A | DONE | voice.py deleted; voice/ submodules |
| Phase B | DONE | backend/application/ removed (ADR-046) |
| Phase C | DONE | AssertionError fixed; pytest-randomly |
| Phase D | DONE | Trust/safety choke point; OpenAPI $ref |
| Phase E | STUB DONE | Golden path stub in CI; proof scripts |
| Phase F | DONE | v1.1.0 released; PROOF_GOLDEN_PATH_REAL_2026-03-06.json exists |

### 2.2 Deferred v1.2 Subplans — Progress

| Subplan | Slices/Batches | Last Completed |
|---------|----------------|----------------|
| Workflow Consolidation | 8 slices | security-monitor.yml (2026-03-11) |
| Strict Mypy Burn-Down | 18 slices | voice_presets, llm_provider_service (2026-03-11) |
| Skip Debt Cleanup | 7 batches | test_input_validation subplan ref (2026-03-11) |

### 2.3 DEFERRED_V1_2 Items Marked DONE

- CI Suppression Policy — `docs/developer/CI_SUPPRESSION_POLICY.md`
- Bandit B614 — `docs/governance/CVE_EXCEPTIONS.md`
- OpenAPI drift check dep alignment — `docs/developer/OPENAPI_CI_ALIGNMENT.md`
- Sentinel smoke backend startup — wait 90×2s; SENTINEL_TESTING_GUIDE.md

### 2.4 Post-Timeline Hardening (2026-03-11)

- REQUEST_COORDINATION_AUDIT: Open Remediation Queue
- Bounded-request test: TimelinePanelScenario_*
- Dialog baseline: 0
- Creep detection: active
- TimelineTrackService, ProjectAudioClient, TimelineTranscriptionService: policy/null-normalization

---

## 3. Gaps Discovered

### GAP-1: One Metric Not Continuous (P1)

**Source**: Roadmap §5 — "The One Metric That Cannot Be Faked"

**Claim**: `pytest tests/e2e/test_golden_path.py -v` exits 0 with `engine_mode='real'`, XTTS + STT loaded, real WAV in, synthesized audio out, proof artifact with I-3 fields.

**Reality**:
- PROOF_GOLDEN_PATH_REAL_2026-03-06.json exists (manual run; verified).
- CI runs golden path in **stub mode** only (ci.yml golden-path job).
- Real run is manual: `scripts/dev/run_golden_path_real.ps1`.
- No continuous CI validation of real-engine golden path.

**Impact**: "100% complete" per governing principle requires real run; one-time proof does not guarantee regression detection.

---

### GAP-2: Workflow Consolidation Incomplete (P2)

**Source**: WORKFLOW_CONSOLIDATION_SUBPLAN.md; grep of `.github/`

**Migrated** (use `./.github/actions/setup-python-pip`):
- build.yml: build-backend (1 job)
- ci.yml: python-tests (1 job)
- test.yml: test-backend, nightly-golden-loop-real (2 jobs)
- sentinel_backend_smoke.yml: schema-validation, sentinel-smoke (2 jobs)
- sbom.yml: python-sbom (1 job)
- release.yml: build-windows, publish-docs (2 jobs)
- security-monitor.yml: python-security-scan, aggregate-report (2 jobs)

**Not migrated** (still use `actions/setup-python@v5`):
- build.yml: 7 jobs (e.g. xaml-check, coverage, etc.)
- ci.yml: 5 jobs (dotnet-build, guardrails, coverage, golden-path, proof-validate)
- test.yml: 14+ jobs (matrix, integration, frontend, etc.)
- governance.yml: 2 jobs
- plugin-submission.yml: 2 jobs
- sentinel_ui_smoke_nightly.yml: 2 jobs

**Total**: ~32 jobs on raw setup-python; no pip cache alignment.

---

### GAP-3: Mypy Burn-Down — Large Remainder (P2)

**Source**: STRICT_MYPY_BURNDOWN_SUBPLAN.md; `backend/services/*.py` count

**Completed** (18 slices):
- Routes: health, v2/health, voice/* (10 files)
- Services: path_service, training_broadcaster, script_store, circuit_breaker, emotion_service, track_store, error_tracker, telemetry, slo_monitor, usage_stats, project_service, training_quality, api_key_store, profile_store, quality_consistency_service, audio_download_service, JobStateStore, workload_balancer, request_queue, edit_history, voice_presets, llm_provider_service

**Remaining** (~78 services): ab_testing, abuse_prevention, ai_audio_enhancement, alerting, api_key_store_service, artifact_provenance, artifact_ref_counter, audio_analysis_service, audio_path_resolver, audio_registry_service, audio_stream, AudioArtifactRegistry, audit_logger, backup_service, batch_processor, circuit_breaker_facade, collaboration_service, ContentAddressedAudioCache, data_retention, diagnostics, effect_chain_store, engine_ensemble, engine_gateway, engine_integration_service, engine_loader, engine_pool, engine_service, engine_shared, EngineConfigService, error_analysis, export_path_validator, feature_status_service, gpu_orchestrator, health_aggregation_service, health_dependencies_service, health_facade, image_gen_service, instant_cloning_service, json_file_store, lexicon_service, lip_sync_service, llm_function_calling, llm_interface_service, macro_execution_service, macro_store, marker_store, marketplace_service, media_storage_service, metrics_cleanup, metrics_history, model_baselines, model_drift_detector, model_facade, model_preflight, model_registry, model_service, multi_speaker_dubbing, persistent_store, phrase_emotion_service, pipeline_service, plugin_sandbox, plugin_schema_validator, plugin_service_testing, plugin_service, profile_search_service, profile_service, profile_storage_service, project_search_service, project_versioning, ProjectStoreService, provenance_policy, quality_benchmark_service, quality_dashboard_service, quality_degradation_service, quality_history_service, quality_metrics_db, quality_service, quality_text_service, quality_trends_service, quality_visualization_service, realtime_voice_changer, resource_analytics, runtime_facade, runtime_service, security_service, synthesis_service, text_processing_service, trace_export, training_service, transcription_service, translation_service, tts_utilities_service, unified_config, video_face_enhancer, voice_helpers, voice_synthesis_service

**Subplan has no completion target**; advisory only per DEFERRED_V1_2.

---

### GAP-4: Skip Debt Target vs. CI Budget Mismatch (P3)

**Source**: SKIP_DEBT_CLEANUP_SUBPLAN.md; tests/ci/test_skip_budget.py; SKIP_DEBT_REPORT

**Subplan**: "Target: 312 → 200" and "Skip count ≤ 200 after execution"

**CI Budget** (test_skip_budget.py):
- collection_skips: 60 + 5 margin
- module_level_skips: 310 + 5 margin
- total_skip_calls: 1760 + 5 margin

**Baseline** (SKIP_DEBT_REPORT 2026-03-11):
- Collection: 46
- Module-level: 300
- Total: 1762

**Gap**: Subplan target "200" is inconsistent with CI budget 1765. Either:
- Target 200 is aspirational and CI would need budget update to enforce, or
- Target 200 referred to a different metric (e.g. module-level only) and is misstated.

**Current state**: Policy compliance improved (7 batches); no skip count reduction. CI passes because 1762 ≤ 1765.

---

### GAP-5: Phase F Anti-Theater — Golden Path Output (P2)

**Source**: PHASE_F_ANTI_THEATER_HARDENING_PLAN.md § C2, C3

**Discovery**:
- C2: Test uses `tempfile.gettempdir()/voicestudio_golden_path`; no `output_manifest.json`.
- C3: Test does not require `VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR`; must add env var, write to that dir, write `output_manifest.json`.

**Status**: PROOF_GOLDEN_PATH_REAL_2026-03-06.json exists and validates; artifact_path points to `docs/reports/verification/artifacts/golden_path_export.wav`. Implementation may have been done ad hoc; plan steps C2/C3 not explicitly closed.

---

### GAP-6: Seam Maturity — Task 1.2 Blocked (P3)

**Source**: SEAM_MATURITY_AUDIT.md

**Task 1.2**: "Deepen VoiceSynthesisService blocked by type location: VoiceSynthesisRequest/Response live in App.Core.Models; IVoiceSynthesisService/IBackendClient use Core.Models. Resolve type consolidation before adding request shaping/response normalization."

**Task 2.1**: DONE (IEmotionStyleClient added).

**ABTestService → ABTestClient**: Optional rename; low priority.

---

### GAP-7: Document Governance — DEFERRED Items (P4)

**Source**: CANONICAL_REGISTRY.md

| Item | Status | Notes |
|------|--------|-------|
| Rule Proposal Template | DEFERRED | docs/governance/templates/RULE_PROPOSATION_TEMPLATE.md not created |
| Archive Policy | DEFERRED | Use document-lifecycle.mdc; archive structure in docs/archive/ |
| Governance Lock | DEFERRED | Low priority |

---

### GAP-8: STATE.md — No Active Task; Plan Linkage (P4)

**Source**: .cursor/STATE.md

- Active Task: None
- Next 3 Steps reference completed work (Workflow 8, Mypy 18)
- "Phase 2 Post-Timeline Hardening" marked COMPLETE
- No explicit linkage to roadmap Phase E/F or DEFERRED_V1_2 next priorities

---

### GAP-9: God-Object Growth (P3)

**Source**: PHASE_F_ANTI_THEATER_HARDENING_PLAN.md § E

- BackendClient.cs: 186,512 bytes (budget 191,632)
- MainWindow.xaml.cs: 133,861 bytes (budget 138,981)

Both within budget; monitor for growth.

---

## 4. Remediation Plan

### Phase R1: Critical Alignment (P1)

| Step | Action | Owner | Verification |
|------|--------|-------|--------------|
| R1.1 | Document One Metric status: PROOF exists; CI runs stub only. Add to ROADMAP or DEFERRED: "Real golden path CI" as v1.2+ enhancement. | Overseer | STATE.md + DEFERRED_V1_2 updated |
| R1.2 | Decide: (a) Accept one-time proof as sufficient for v1.1.0, or (b) Add optional CI job (e.g. weekly) that runs real golden path when models available. | Product/Architect | ADR or DEFERRED note |

### Phase R2: Workflow Consolidation Continuation (P2)

| Step | Action | Owner | Verification |
|------|--------|-------|--------------|
| R2.1 | Migrate governance.yml (2 jobs) to setup-python-pip | Build/Tooling | Slice 9 in WORKFLOW_CONSOLIDATION_SUBPLAN |
| R2.2 | Migrate plugin-submission.yml (2 jobs) | Build/Tooling | Slice 10 |
| R2.3 | Migrate sentinel_ui_smoke_nightly.yml (2 jobs) | Build/Tooling | Slice 11 |
| R2.4 | Migrate ci.yml remaining jobs (5 jobs) — assess cache-mode per job | Build/Tooling | Slice 12 |
| R2.5 | Migrate build.yml remaining jobs (7 jobs) | Build/Tooling | Slice 13 |
| R2.6 | Migrate test.yml remaining jobs (14+ jobs) — preserve matrix behavior | Build/Tooling | Slice 14 |

### Phase R3: Mypy Burn-Down Continuation (P2)

| Step | Action | Owner | Verification |
|------|--------|-------|--------------|
| R3.1 | Prioritize high-traffic services: synthesis_service, engine_service, transcription_service, profile_service, project_service (already done), training_service | Engine/Core | Slice 19 |
| R3.2 | Add completion target to STRICT_MYPY_BURNDOWN_SUBPLAN: e.g. "backend/services/ 50% strict by v1.2" or "all backend/services/ by v1.3" | Overseer | Subplan updated |
| R3.3 | Execute slices 19–25 incrementally | Advisory | mypy --strict passes per slice |

### Phase R4: Skip Debt Clarification (P3)

| Step | Action | Owner | Verification |
|------|--------|-------|--------------|
| R4.1 | Align SKIP_DEBT_CLEANUP_SUBPLAN target with CI: either (a) change target to "≤ budget (1765)" or (b) add phased target (e.g. 1762→1500→1200) with budget updates | Overseer | Subplan + test_skip_budget.py |
| R4.2 | Document that "312 → 200" was a different baseline or retract | Overseer | SKIP_DEBT_REPORT |
| R4.3 | Continue policy compliance; prioritize must-fix (flaky) over infrastructure | Test/Tooling | Skip report regenerated |

### Phase R5: Phase F Anti-Theater Closure (P2)

| Step | Action | Owner | Verification |
|------|--------|-------|--------------|
| R5.1 | Verify tests/e2e/test_golden_path.py uses VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR and writes output_manifest.json; if not, implement per C2/C3 | Engine/Core | test + proof script |
| R5.2 | Mark PHASE_F_ANTI_THEATER discovery steps C2, C3 complete or document deviation | Overseer | Plan checkmarks |

### Phase R6: Seam Maturity (P3)

| Step | Action | Owner | Verification |
|------|--------|-------|--------------|
| R6.1 | Create task brief for type consolidation (VoiceSynthesisRequest/Response) to unblock Task 1.2 | System Architect | TASK-XXXX |
| R6.2 | ABTestService → ABTestClient rename: add to optional backlog | UI Engineer | Backlog |

### Phase R7: Document Governance (P4)

| Step | Action | Owner | Verification |
|------|--------|-------|--------------|
| R7.1 | Create Rule Proposal Template if rule creation workflow is needed | Overseer | docs/governance/templates/ |
| R7.2 | Archive Policy: document that document-lifecycle.mdc + docs/archive/ is canonical; close DEFERRED | Overseer | CANONICAL_REGISTRY |

### Phase R8: STATE and Plan Linkage (P4)

| Step | Action | Owner | Verification |
|------|--------|-------|--------------|
| R8.1 | Update STATE.md Next 3 Steps with concrete next tasks from this plan (e.g. R2.1, R3.1) | Overseer | STATE.md |
| R8.2 | Add "Active Plan" reference to this gap analysis when approved | Overseer | STATE.md |

---

## 5. Verification Checklist

Before closing this plan:

- [ ] All P1 gaps have remediation steps assigned
- [ ] R1.1 and R1.2 completed or explicitly deferred
- [ ] WORKFLOW_CONSOLIDATION_SUBPLAN updated with R2 slices
- [ ] STRICT_MYPY_BURNDOWN_SUBPLAN has completion target (R3.2)
- [ ] SKIP_DEBT_CLEANUP_SUBPLAN target aligned with CI (R4.1)
- [ ] CANONICAL_REGISTRY updated with this document
- [ ] STATE.md reflects next actionable tasks

---

## 6. Changelog

- 2026-03-11: Initial comprehensive gap analysis and remediation plan.
