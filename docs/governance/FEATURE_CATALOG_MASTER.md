# Feature Catalog Master (FCM)

Document ID: `FEATURE_CATALOG_MASTER`  
Version: `1.0.0`  
Date: `2026-03-05`  
Owner: `Architecture / Governance`  
Companion machine-readable appendix: [`FEATURE_CATALOG_MASTER.appendix.json`](./FEATURE_CATALOG_MASTER.appendix.json)

## FCM-000 Document Contract

This file is the single canonical feature catalog for VoiceStudio.

- Scope: product feature inventory and capability status across UI, API, engines, plugins, and major archived snapshots.
- Status model: factual, evidence-linked, and date-stamped.
- Update trigger: any material change to panel registry, route registry, engine manifests, plugin catalog, or feature-gate posture.
- Machine source: `docs/governance/FEATURE_CATALOG_MASTER.appendix.json` is the CI-friendly contract artifact.

## FCM-001 Scope and Method

### FCM-001.1 Roots inspected

- `E:\VoiceStudio` (primary)
- `E:\VoiceStudio-baseline`
- `E:\VoiceStudio-feb13`
- `E:\VoiceStudio-golden`
- `E:\VoiceStudio-integration`
- `E:\__feature_harvest`
- `E:\__cleanup_staging`
- `E:\cursor` (for contamination checks)

### FCM-001.2 Primary evidence inputs

- [`V1_SCOPE.md`](./V1_SCOPE.md)
- [`PANEL_REGISTRY_AUDIT.md`](../reports/audit/PANEL_REGISTRY_AUDIT.md)
- [`CorePanelRegistrationService.cs`](../../src/VoiceStudio.App/Services/CorePanelRegistrationService.cs)
- [`AdvancedPanelRegistrationService.cs`](../../src/VoiceStudio.App/Services/AdvancedPanelRegistrationService.cs)
- [`route_registry.py`](../../backend/api/route_registry.py)
- [`observability.py`](../../backend/api/observability.py)
- [`plugins.json`](../../shared/catalog/plugins.json)
- [`STATE.md`](../../.cursor/STATE.md)
- [`.buildlogs/feature_inventory_cross_snapshot.json`](../../.buildlogs/feature_inventory_cross_snapshot.json)
- [`.buildlogs/route_operation_inventory.json`](../../.buildlogs/route_operation_inventory.json)
- [`.buildlogs/verification/last_run.json`](../../.buildlogs/verification/last_run.json)

### FCM-001.3 Measurement date

All counts in this document were measured on `2026-03-05`.

## FCM-002 Ruthless Assessment

You built a lot. You did not control the catalog.

- Feature breadth is strong.
- Feature traceability is weak.
- Release gate posture is not fully green yet (Gate B remains open).
- Archive and staging noise is high enough to distort audits unless filtered.

Bottom line: execution capability is real; operational discipline is lagging.

## FCM-003 Current Product Inventory (Primary Root)

| Surface | Count | Evidence |
|---|---:|---|
| Registered canonical panels | 47 | `V1_SCOPE.md`, panel registration services |
| Panel XAML views under `Views/Panels` | 97 | source scan |
| Panel ViewModels (`ViewModels` + `Views/Panels`) | 104 | source scan |
| API route files (total) | 143 | `backend/api/routes` |
| API route files (active) | 126 | excludes `_archived/*` and `contexts/*` |
| API route files (`_archived/*`) | 10 | source scan |
| API route files (`contexts/*`) | 7 | source scan |
| Active API operations | 880 | 410 GET, 343 POST, 51 PUT, 69 DELETE, 1 PATCH, 6 WS |
| Route modules listed in registry | 111 | `route_module_names` |
| Route modules directly included | 109 | `_include_route("...")` |
| Engine manifests | 70 | `engines/**/engine.manifest.json` |
| Engine adapter modules (`app/core/engines/*_engine.py`) | 54 | source scan |
| Plugin categories | 8 | `shared/catalog/plugins.json` |
| Actual plugins in catalog | 3 | `shared/catalog/plugins.json` |
| `docs/archive` entries | 2244 | source scan |

## FCM-004 Canonical Feature Taxonomy

### FCM-004.1 UI Feature Surface

- Canonical panel registry is 47 panels.
- Region distribution: Center 26, Left 3, Right 17, Bottom 1.
- There are 50+ additional panel-like XAML surfaces beyond the canonical registry; they must be classified (active, deprecated, dead).

### FCM-004.2 API Feature Surface

- Active API surface: 126 route files, 880 operations.
- Highest operation density route families:
  - `quality` (29)
  - `mixer` (22)
  - `macros` (19)
  - `engines` (18)
  - `health` (18)
  - `lexicon`, `models`, `timeline`, `training` (15 each)

### FCM-004.3 Engine Feature Surface

- Manifest count: 70
- Type distribution:
  - `audio`: 38
  - `image`: 13
  - `video`: 8
  - `llm`: 3
  - `stt`: 1
  - `tts`: 5
  - `unknown`: 2
- Adapter modules added vs baseline in primary engine module set:
  - `gemini_live_engine.py`
  - `openai_realtime_engine.py`
  - `tacotron2_engine.py`
  - `whisperx_engine.py`

### FCM-004.4 Plugin Feature Surface

- Catalog taxonomy exists (8 categories).
- Active plugin inventory is shallow (3 plugins).
- Conclusion: plugin platform scaffolding exists; ecosystem is early-stage.

## FCM-005 Canonical Panel Registry (47)

This list is the feature scope authority for the UI panel surface.

1. `VoiceSynthesis` — Voice Synthesis — Center
2. `EnsembleSynthesis` — Ensemble Synthesis — Center
3. `BatchProcessing` — Batch Processing — Center
4. `TrainingDatasetEditor` — Training Dataset Editor — Center
5. `ModelManager` — Model Manager — Center
6. `Training` — Training — Left
7. `Transcribe` — Transcribe — Center
8. `Recording` — Recording — Center
9. `AudioAnalysis` — Audio Analysis — Center
10. `QualityControl` — Quality Control — Right
11. `Timeline` — Timeline — Center
12. `Profiles` — Profiles — Left
13. `Library` — Library — Left
14. `EffectsMixer` — Effects Mixer — Right
15. `Analyzer` — Analyzer — Right
16. `VoiceMorph` — Voice Morph — Center
17. `EmotionControl` — Emotion Control — Right
18. `Diagnostics` — Diagnostics — Bottom
19. `Settings` — Settings — Right
20. `Help` — Help — Right
21. `SSMLControl` — SSML Control — Right
22. `VoiceQuickClone` — Quick Clone — Center
23. `QualityDashboard` — Quality Dashboard — Center
24. `QualityBenchmark` — Quality Benchmark — Center
25. `ImageGen` — Image Generation — Center
26. `VideoGen` — Video Generation — Center
27. `DeepfakeCreator` — Deepfake Creator — Center
28. `DatasetQA` — Dataset QA — Center
29. `ScriptEditor` — Script Editor — Center
30. `SceneBuilder` — Scene Builder — Center
31. `Macro` — Macro — Center
32. `WorkflowAutomation` — Workflow Automation — Center
33. `AdvancedSettings` — Advanced Settings — Right
34. `APIKeyManager` — API Key Manager — Right
35. `GPUStatus` — GPU Status — Right
36. `TodoPanel` — Todo Panel — Right
37. `text-speech-editor` — Text Speech Editor — Center
38. `prosody` — Prosody and Phoneme Control — Center
39. `spatial-audio` — Spatial Audio — Right
40. `ai-mixing-mastering` — AI Mixing and Mastering — Right
41. `voice-style-transfer` — Voice Style Transfer — Center
42. `embedding-explorer` — Speaker Embedding Explorer — Right
43. `ai-production-assistant` — AI Production Assistant — Right
44. `pronunciation-lexicon` — Pronunciation Lexicon — Right
45. `voice-morphing-blending` — Voice Morphing and Blending — Center
46. `plugin-gallery` — Plugin Gallery — Center
47. `theme-editor` — Theme Editor — Right

## FCM-006 Registration Integrity and Drift

### FCM-006.1 Route registry parity findings

RESOLVED (2026-03-06, commit 2b744691). Parity enforced by `tests/ci/test_route_registry_parity.py`.

### FCM-006.2 Verification baseline finding

- Latest run `.\scripts\verify.ps1 -Quick` is GREEN (`20260305_011208`).
- Gate/Ledger validation passed including `empty_catch_check`.
- Gate B CLOSED (10/10) as of 2026-03-06.
- Historical note: an earlier same-day quick run failed on empty catches, then recovered.

Real-engine golden-path proof remains a prerequisite; claims of “bulletproof” are not credible.

## FCM-007 Snapshot Comparison

| Snapshot | Route Files | Engine Manifests | Plugins | Archived Doc Entries |
|---|---:|---:|---:|---:|
| `VoiceStudio` | 143 | 70 | 3 | 2244 |
| `VoiceStudio-baseline` | 101 | 45 | 0 | 1841 |
| `VoiceStudio-feb13` | 124 | 68 | 0 | 1851 |
| `VoiceStudio-golden` | 136 | 70 | 3 | 2234 |
| `VoiceStudio-integration` | 136 | 70 | 3 | 2234 |

Notes:

- Snapshot panel-ID extraction is not structurally uniform across old roots; treat panel deltas from legacy snapshots as advisory until normalized extraction is implemented.
- Current vs baseline route growth and engine growth are clear and material.

## FCM-008 Source Hygiene Findings

### FCM-008.1 Harvest contamination

`E:\__feature_harvest` includes non-product content (for example, Cursor extension docs from `E:\cursor\resources\app\extensions\...`).  
This makes raw harvest output unfit as a product feature source unless filtered.

### FCM-008.2 Staging bloat

`E:\__cleanup_staging` is very large and mostly non-canonical staging content. It should not be used as feature evidence.

### FCM-008.3 Archive concentration

Most archive volume is concentrated in:

- `docs/archive/legacy_worker_system`
- `docs/archive/pre_restore_20260228`

These are useful historical records, but they are not runtime feature truth.

## FCM-009 Ordered Next 20 Tasks

1. [x] Close Gate B from 9/10 to 10/10.
2. Add a CI assertion that enforces zero empty catches to prevent regression.
3. Adopt this document as canonical in governance index.
4. Mark old feature docs as superseded with pointer to this file.
5. Build generator script for panel/API/engine/plugin inventory refresh.
6. Add CI check for stale catalog versus generated appendix.
7. [x] Add route parity test: files vs `route_module_names` vs `_include_route`.
8. [x] Resolve `consent`, `metrics`, `telemetry`, `experiments` parity drift.
9. Classify all `_archived/*` routes as keep/delete/migrate.
10. Classify all `contexts/*` routes as contract/dead scaffolding.
11. Add panel parity test: registry ID requires View + ViewModel + navigation.
12. Audit unregistered panel XAML surfaces and tag active/deprecated/dead.
13. Remove or archive dead panel surfaces from active source tree.
14. Eliminate `unknown` engine manifest taxonomy entries.
15. Build engine viability matrix: manifest/import/health/smoke synthesis.
16. Expand plugin catalog beyond 3, or shrink category taxonomy to match reality.
17. Replace stub golden-path proof with real-engine proof artifact.
18. Formalize lifecycle policy for baseline/feb13/golden/integration roots.
19. Purge or relocate non-canonical staging and contaminated harvest outputs.
20. Add weekly release-readiness report (verify, gates, drift, doc freshness).

## FCM-010 Evidence Map

- Canonical panel definition: [`V1_SCOPE.md`](./V1_SCOPE.md)
- Panel implementation audit: [`PANEL_REGISTRY_AUDIT.md`](../reports/audit/PANEL_REGISTRY_AUDIT.md)
- Route registration reality: [`route_registry.py`](../../backend/api/route_registry.py)
- Observability side-registration: [`observability.py`](../../backend/api/observability.py)
- Plugin catalog reality: [`plugins.json`](../../shared/catalog/plugins.json)
- Verification status: [`verification_report.md`](../../artifacts/verify/20260305_011208/verification_report.md)
- Machine appendix: [`FEATURE_CATALOG_MASTER.appendix.json`](./FEATURE_CATALOG_MASTER.appendix.json)

## FCM-011 Machine Appendix Contract

The companion JSON file is intended for CI and tool consumers.

- File: `docs/governance/FEATURE_CATALOG_MASTER.appendix.json`
- Schema version: `1.0.0`
- CI drift check: `tests/ci/test_feature_catalog_appendix.py` — fails when appendix is missing, invalid, or stale (> 90 days)
- Required top-level keys:
  - `schema_version`
  - `catalog_id`
  - `generated_at_utc`
  - `source_scope`
  - `canonical_ui`
  - `api_surface`
  - `engine_surface`
  - `plugin_surface`
  - `snapshot_comparison`
  - `verification_status`
  - `known_risks`
  - `next_20_tasks_ordered`
