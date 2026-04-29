# Generated Audio Product Closure v1 - 2026-04-29

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: generated_audio
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->

**Classification:** REAL_ENGINE
**Date:** 2026-04-29

## Purpose

Record current-head evidence for Generated Audio Product Authority v1: real synthesis output must become a project-owned, durable, reloadable, exportable, replay-verifiable asset with automated JSON and Markdown proof.

## Closure Verdict

`PRODUCT_CLOSURE_VERIFIED_WITH_RESTART_DURABILITY_NON_CLAIM`

Current-head evidence now includes a live real-engine product-closure run through synthesis, generated audio artifact, library asset, timeline clip, timeline export, proof JSON validation, identity-spine validation, and automated replay/decode validation. Backend restart durability is not claimed because no restart command was supplied; project reload was verified through backend API state during the live product-closure run.

Proof bundle:

- JSON: `docs/reports/verification/product_closure_live_attempt/GENERATED_AUDIO_PRODUCT_CLOSURE_LIVE_ATTEMPT_2026-04-29.json`
- Markdown: `docs/reports/verification/product_closure_live_attempt/GENERATED_AUDIO_PRODUCT_CLOSURE_LIVE_ATTEMPT_2026-04-29.md`
- Full quick verification: `artifacts/verify/20260429_174810/verification_report.md`

## Product Authority Chain

| Step | Authority | Current implementation status |
| --- | --- | --- |
| Synthesis | `POST /api/voice/synthesize` | Response includes `generated_audio_id` and `profile_id`; request accepts `project_id` and `session_id`. |
| Generated artifact | `AudioArtifact` registry | Synthesis service records project/session/profile/engine provenance in artifact metadata. |
| Library asset | `POST /api/library/assets/upload` | Upload accepts provenance query fields plus `metadata_json`; metadata is stored on the library asset. |
| Timeline clip | `POST /api/timeline/clips` | `AddClipRequest.metadata` persists generated-audio identity fields into `Clip.metadata`. |
| Export | `POST /api/timeline/export` | Harness calls route when `--export-timeline` is enabled and validates the exported WAV with `audio_forensics.py`. |
| Replay validation | `scripts/proof/verify_generated_audio_replay.py` | Validates path, URL, and proof JSON targets through automated decode/non-silence checks. |
| Restart durability | `scripts/proof/verify_backend_restart_durability.py` | Runs restart command, waits for readiness, re-queries audio/library/timeline, and validates export replay when claimed. |
| Identity graph | `scripts/ci/check_generated_audio_identity_spine.py` | Validates project, generated audio, library, timeline, export, duration, engine, and hash coherence. |
| No-fallback audit | `scripts/ci/check_runtime_no_fallback_product_path.py` | Self-test wired into `scripts/run_verification.py`; detects fallback/mock/stub drift patterns. |

## Identity Spine

The expanded proof JSON schema supports:

- `project.project_id`, `project.project_name`, `project.session_id`
- `generated_audio.generated_audio_id`, `audio_id`, `library_asset_id`, `timeline_track_id`, `timeline_clip_id`
- `generated_audio.source_engine`, `routed_engine`, `profile_id`
- `generated_audio.artifact_sha256`, `artifact_path`, `duration_seconds`, `provenance`
- `export.claimed`, `path`, `sha256`, `size_bytes`, `container`, `non_silent`, `duration_seconds_from_wav`

## Automated Replay Evidence

Replay proof is automated only. The implementation does not use human listening or operator verification language.

Live proof replay validation:

- Source: `docs/reports/verification/product_closure_live_attempt/GENERATED_AUDIO_PRODUCT_CLOSURE_LIVE_ATTEMPT_2026-04-29.json`
- Result: PASS
- WAV: decoded, non-silent, 24 kHz, mono, duration 6.774 seconds
- Body check: binary audio, not a JSON error body, and does not start with `{`
- SHA-256: `bdf40c0d81db31ddf388991f697dc27f025adea81efc837e6a7d2860c3defecc`

Current focused tests:

- `tests/unit/scripts/proof/test_generated_audio_replay.py` - 6 tests
- Covers non-silent WAV pass, silent WAV fail, JSON body fail, proof JSON path resolution, URL fetch validation, and CLI JSON failure output.

## Restart Durability Evidence

Restart durability is only claimed when a restart command is supplied and post-restart reload checks pass.

Current live run non-claim:

- `scripts/proof/verify_backend_restart_durability.py --proof-json docs/reports/verification/product_closure_live_attempt/GENERATED_AUDIO_PRODUCT_CLOSURE_LIVE_ATTEMPT_2026-04-29.json --json`
- Result: BLOCKED
- Blocker: `restart command not supplied; restart durability is a non-claim`

Current focused tests:

- `tests/unit/scripts/proof/test_backend_restart_durability.py` - 7 tests
- Covers missing restart non-claim, restart command failure, readiness failure, successful reload chain, audio JSON body failure, library reload failure, and export replay validation.

## Export Evidence

The harness export path records:

- `export.success`
- `export.path`
- `export.size_bytes`
- `export.sha256`
- `export.container`
- `export.non_silent`
- `export.duration_seconds_from_wav`

Live export evidence:

- `export.claimed`: true
- `export.non_silent`: true
- `export.container`: `RIFF/WAVE`
- `export.duration_seconds_from_wav`: 6.774
- `export.sha256`: `bdf40c0d81db31ddf388991f697dc27f025adea81efc837e6a7d2860c3defecc`

Focused export tests:

- `tests/unit/backend/api/routes/test_timeline.py::TestExport::test_export_timeline_writes_replay_verifiable_wav`
- `tests/unit/scripts/proof/test_voice_synthesis_real_engine_proof_harness.py::test_product_closure_flow_records_project_generated_audio_and_export`

## Contract Evidence

`tests/contract/test_voice_synthesis_proof_surface_contract.py` now includes seven product-closure contract checks:

- Synthesis request accepts project/session IDs.
- Synthesis response exposes generated-audio identity fields.
- Library upload exposes provenance parameters.
- Timeline clips accept metadata.
- Timeline export route has request/response shape.
- Timeline export error shape is JSON and traceback-free.
- Proof schema exposes `project`, `generated_audio`, and `export`.

## Verification Commands Run So Far

| Command | Result |
| --- | --- |
| `python -m pytest tests/unit/scripts/proof/test_voice_synthesis_real_engine_proof_harness.py tests/unit/scripts/ci/test_voice_synthesis_proof_json.py tests/unit/scripts/ci/test_generated_audio_identity_spine.py -q` | PASS: 58 passed |
| `python -m pytest tests/unit/scripts/proof/test_generated_audio_replay.py -q` | PASS: 6 passed |
| `python -m pytest tests/unit/scripts/proof/test_backend_restart_durability.py -q` | PASS: 7 passed |
| `python -m pytest tests/unit/backend/api/routes/test_timeline.py::TestExport::test_export_timeline_writes_replay_verifiable_wav tests/unit/scripts/proof/test_voice_synthesis_real_engine_proof_harness.py::test_product_closure_flow_records_project_generated_audio_and_export -q` | PASS: 2 passed |
| `python -m pytest tests/contract/test_voice_synthesis_proof_surface_contract.py -q` | PASS: 15 passed |
| `python -m pytest tests/unit/scripts/ci/test_runtime_no_fallback_product_path.py -q` | PASS: 7 passed |
| `python scripts/ci/check_runtime_no_fallback_product_path.py --self-test-examples` | PASS |
| `python -m pytest tests/unit/backend/api/routes/test_synthesis_stub.py tests/unit/backend/api/routes/test_timeline.py tests/unit/backend/api/routes/test_library.py tests/unit/scripts/ci/test_generated_audio_identity_spine.py tests/unit/scripts/ci/test_voice_synthesis_proof_json.py tests/unit/scripts/proof/test_voice_synthesis_real_engine_proof_harness.py tests/unit/scripts/proof/test_generated_audio_replay.py tests/unit/scripts/proof/test_backend_restart_durability.py tests/unit/scripts/ci/test_runtime_no_fallback_product_path.py tests/contract/test_voice_synthesis_proof_surface_contract.py -q` | PASS: 151 passed |
| `python scripts/proof/run_voice_synthesis_real_engine_proof.py --dry-run-fixtures --product-closure --output-dir artifacts/proof_harness_product_closure_selftest` | PASS |
| `python scripts/ci/check_voice_synthesis_proof_json.py --path artifacts/proof_harness_product_closure_selftest/VOICE_SYNTHESIS_PROOF_HARNESS_DRYRUN_REAL.json --product-closure` | PASS |
| `python scripts/ci/check_generated_audio_identity_spine.py --proof-json artifacts/proof_harness_product_closure_selftest/VOICE_SYNTHESIS_PROOF_HARNESS_DRYRUN_REAL.json` | PASS |
| `python scripts/proof/run_voice_synthesis_real_engine_proof.py --product-closure --require-real --project-id product-closure-live-20260429 --project-name "Generated Audio Product Authority v1" --export-timeline --verify-reload --timeout-seconds 30 --output-dir docs/reports/verification/product_closure_live_attempt --json-output docs/reports/verification/product_closure_live_attempt/GENERATED_AUDIO_PRODUCT_CLOSURE_LIVE_ATTEMPT_2026-04-29.json --markdown-output docs/reports/verification/product_closure_live_attempt/GENERATED_AUDIO_PRODUCT_CLOSURE_LIVE_ATTEMPT_2026-04-29.md` | PASS: REAL_ENGINE product-closure proof |
| `python scripts/ci/check_voice_synthesis_proof_json.py --path docs/reports/verification/product_closure_live_attempt/GENERATED_AUDIO_PRODUCT_CLOSURE_LIVE_ATTEMPT_2026-04-29.json --product-closure` | PASS |
| `python scripts/ci/check_generated_audio_identity_spine.py --proof-json docs/reports/verification/product_closure_live_attempt/GENERATED_AUDIO_PRODUCT_CLOSURE_LIVE_ATTEMPT_2026-04-29.json` | PASS |
| `python scripts/proof/verify_generated_audio_replay.py --proof-json docs/reports/verification/product_closure_live_attempt/GENERATED_AUDIO_PRODUCT_CLOSURE_LIVE_ATTEMPT_2026-04-29.json --json` | PASS |
| `python scripts/proof/verify_backend_restart_durability.py --proof-json docs/reports/verification/product_closure_live_attempt/GENERATED_AUDIO_PRODUCT_CLOSURE_LIVE_ATTEMPT_2026-04-29.json --json` | BLOCKED: restart command not supplied; restart durability non-claim |
| `python scripts/run_verification.py` | PASS |
| `.\scripts\verify.ps1 -Quick` | PASS: `artifacts/verify/20260429_174810/verification_report.md` |

## Non-Claims

- Backend restart durability is not claimed without a supplied restart command and passing post-restart reload checks.
- No push has been performed.
- No human/operator verification is used or claimed.
- This work does not reopen GAP-008 and does not create Slice 46.
- This work does not touch `MainWindow*ShellBridge`, RHVoice, or `ENGINE_PARITY_MATRIX.md`.

## Rollback

Revert the generated-audio product authority changes as one local commit once created. Until commit, rollback is by restoring the modified backend route/service files, proof scripts, schema, tests, report docs, and verification wiring from git.
