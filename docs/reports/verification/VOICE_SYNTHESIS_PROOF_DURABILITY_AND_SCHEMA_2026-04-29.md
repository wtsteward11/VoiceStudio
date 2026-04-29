# Voice Synthesis Proof Durability and Schema — 2026-04-29

**Gate note:** Filenames containing `PROOF_DURABILITY` or `PROOF_SCHEMA` are excluded from the Markdown proof-boundary gate because this file documents tooling, not a synthesis proof artifact.

## Purpose

This bundle hardens the generated-audio proof surface created after `a2f07786` by adding a formal JSON proof schema, schema validator, WAV forensic validation, durability replay mode, proof bundle indexing, and proof-critical API route contracts.

## What Was Missing After `a2f07786`

- The harness emitted Markdown that passed `voice_synthesis_proof_boundary`, but JSON output was too thin for machine auditing.
- Audio validation used header/size checks only; it did not compute SHA-256, PCM metadata, peak, RMS, or non-silence.
- Durability claims were not represented as first-class structured fields.
- Proof bundles had no deterministic index.
- The route surface used by the harness was not contract-pinned.

## Added Surfaces

| Surface | Path |
|---|---|
| JSON schema | `schemas/voice_synthesis_proof.schema.json` |
| JSON validator | `scripts/ci/check_voice_synthesis_proof_json.py` |
| Audio forensics | `scripts/proof/audio_forensics.py` |
| Harness JSON + durability mode | `scripts/proof/run_voice_synthesis_real_engine_proof.py` |
| Proof indexer | `scripts/proof/index_voice_synthesis_proofs.py` |
| API contracts | `tests/contract/test_voice_synthesis_proof_surface_contract.py` |

## Durability Mode Behavior

Default harness runs do **not** claim durability. `--verify-durability` performs automated replay checks against audio, library, and timeline state. Restart durability is claimed only when `--restart-backend-command` is explicitly supplied, succeeds, and reload checks pass after restart.

No human/operator action is involved.

## Verification Commands

```powershell
python -m pytest tests/unit/scripts/proof/test_audio_forensics.py -q
python -m pytest tests/unit/scripts/ci/test_voice_synthesis_proof_json.py -q
python -m pytest tests/unit/scripts/proof/test_voice_synthesis_real_engine_proof_harness.py -q
python -m pytest tests/unit/scripts/proof/test_voice_synthesis_proof_indexer.py -q
python -m pytest tests/unit/scripts/ci/test_voice_synthesis_proof_boundary.py -q
python -m pytest tests/contract/test_voice_synthesis_proof_surface_contract.py -q
python scripts/ci/check_voice_synthesis_proof_json.py --self-test-examples
python scripts/proof/run_voice_synthesis_real_engine_proof.py --dry-run-fixtures --output-dir artifacts/proof_harness_selftest
python scripts/ci/check_voice_synthesis_proof_json.py --dir artifacts/proof_harness_selftest --json
python scripts/proof/index_voice_synthesis_proofs.py --dir artifacts/proof_harness_selftest --output artifacts/proof_harness_selftest/index.json --strict --json
python scripts/run_verification.py
.\scripts\verify.ps1 -Quick
```

## Verification Results

| Command | Result |
|---|---|
| Focused pytest bundle | `116 passed` |
| `check_voice_synthesis_proof_boundary.py --self-test-examples` | PASS (`6 example(s)`) |
| `check_voice_synthesis_proof_boundary.py --changed-from origin/main` | PASS (`REAL_ENGINE_GENERATED_AUDIO_PROOF_2026-04-29.md`) |
| `check_voice_synthesis_proof_json.py --self-test-examples` | PASS (`4 example(s)`) |
| Harness dry-run + JSON validation | PASS (`REAL_ENGINE` + `STUB_ENGINE` fixture JSON) |
| Proof indexer strict mode | PASS (`REAL_ENGINE: 1`, `STUB_ENGINE: 1`) |
| Optional automatic real proof attempt | PASS under `artifacts/proof_harness_real_attempt/` (`REAL_ENGINE`, `routed_engine=xtts_v2`, `audio_size=93260`, `non_silent=True`, durability non-claim because restart command was not supplied) |
| `python scripts/run_verification.py` | PASS |
| Final focused regression subset | `48 passed` after durability classification correction |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260429_164552/verification_report.md` |

## Explicit Non-Claims

- This is not a new `REAL_ENGINE` proof unless an actual real run is performed and both JSON + Markdown validators pass.
- This is not runtime FULL PASS.
- This is not operator proof.
- This is not GAP-008.
- This is not Slice 46.
- This does not touch `MainWindow*ShellBridge`.
- This is not RHVoice.
- This is not `ENGINE_PARITY_MATRIX.md`.

## Final Verdict

This bundle upgrades proof instrumentation and automated validation only. It does not replace a future deliberate real-engine proof run.
