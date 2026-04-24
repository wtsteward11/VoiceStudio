# Bounded Slice 19J — OpenVoice authentic-weights live proof

**Status:** Closed — **Outcome B** (2026-04-22): **preflight** **`checks.openvoice.ok: true`**; host **`checkpoint.pth` files remain 2-byte placeholders**; **`real_openvoice` 2/2 FAIL** + C# **3/3 FAIL**; matrix **`openvoice` pending**. [PROOF §19J](../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md) · [session](../reports/verification/slice19/openvoice/slice19j_proof_session.md).  
**Depends on:** [Slice 19I](VOICESTUDIO_BOUNDED_SLICE19I_OPENVOICE_STRATEGY_B_RUNTIME.md) (Strategy B + **ADR-055** — import/preflight green; live ladder red on invalid checkpoints) · **ADR-054** (isolated `venv_openvoice`) · [ADR-055](../architecture/decisions/ADR-055-myshell-openvoice-vendored-patches.md)

## Goal

Move OpenVoice from **importable + structurally valid checkpoints + green `checks.openvoice.ok`** to either:

- **Outcome A:** Authentic MyShell OpenVoice v2 weights under the model root, **`real_openvoice` 2/2** + C# OpenVoice `LiveBackend` **3/3** on one backend URL, explicit file-contract conclusion, [ENGINE_PARITY_MATRIX.md](../reports/verification/ENGINE_PARITY_MATRIX.md) **`openvoice` → PASS**.

- **Outcome B:** Authentic weights attempted or host still on placeholders; one **exact** runtime seam frozen (checkpoint load, worker, `return None` / file-ready, router) with verbatim evidence; **no** matrix PASS.

## Two-layer weight contract (authoritative)

| Layer | Code | What “green” means |
| --- | --- | --- |
| **Preflight (structural)** | [`_openvoice_has_checkpoints`](../../backend/services/model_preflight.py) in `ensure_openvoice` | Under `VOICESTUDIO_MODELS_PATH` (or `%ProgramData%\VoiceStudio\models` if unset): `openvoice/base_speakers` and `openvoice/converter` each contain **at least one** `config.json` (via `rglob`) with a sibling `checkpoint.pth`, `checkpoint.ckpt`, or `model.pth`. This does **not** validate tensor contents. |
| **Runtime (loader)** | [`OpenVoiceEngine._load_models`](../../app/core/engines/openvoice_engine.py) | Resolves `base_speaker_model` and `tone_color_converter_model` under the model root when not absolute. Defaults from worker: `openvoice/base_speakers/EN` and `openvoice/converter` ([`openvoice_worker_synthesize.py`](../../app/cli/openvoice_worker_synthesize.py)). Loads `config.json` + `checkpoint.pth` for base speaker and converter. **Files must be real OpenVoice v2 weights** (see [MyShell OpenVoice](https://github.com/myshell-ai/OpenVoice) releases / docs — operator download; not committed to git). |

**Invalid placeholders:** Empty or tiny `.pth` files (e.g. **2 bytes**) may satisfy preflight rglob but **fail** `load_ckpt` or synthesis — [§19I](../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md) showed **500** / *engine returned None* with such placeholders.

## File-driven `return None` seam (must be verified live)

- [`OpenVoiceSubprocessEngine.synthesize`](../../app/core/engines/openvoice_subprocess_engine.py): on success, returns `None` after the worker writes a **non-trivial** WAV to `output_path`. If the worker never produces a valid file, the engine returns `None` and the service may surface **500** — [§19F/§19I](PROOF) discussion of [`SynthesisService.synthesize`](../../backend/services/synthesis_service.py) (`result is None` + `_synth_output_file_ready`).

- **Conclusion A:** Valid weights + worker output → file on disk + `result is None` + service registers artifact.  
- **Conclusion B:** Failure before valid file (init/synthesis) → **500**; not the happy file-ready path.

## Non-goals

- No packaging / Strategy A retry; no new `real_openvoice` tests; no RHVoice; no matrix PASS without **2/2 + 3/3** and WAV evidence.

## Operator: lay authentic weights

1. Obtain official **OpenVoice v2** **base speaker** and **converter** checkpoints (e.g. from the upstream repo / release assets; layout must match `openvoice/base_speakers/EN/` and `openvoice/converter/` with `config.json` + `checkpoint.pth` as loaded by the engine).
2. Replace any placeholder files under `$env:VOICESTUDIO_MODELS_PATH\openvoice\` (or repo `models\` if that is your models root).
3. **Do not** commit large `.pth` files to the repo.

## Artifacts (this slice)

| Artifact | Role |
| --- | --- |
| [slice19j_proof_session.md](../reports/verification/slice19/openvoice/slice19j_proof_session.md) | Commands, URL, pass/fail, file-contract note |
| [slice19j_preflight_openvoice.json](../reports/verification/slice19/openvoice/slice19j_preflight_openvoice.json) | Verbatim `GET /api/health/preflight` when backend runs |

## Verification order

1. One fresh Uvicorn; `GET {base}/api/health/preflight` → `checks.openvoice.ok: true` (or stop, record).
2. `pytest -m real_openvoice` then C# `LiveBackend` filter — same `VOICESTUDIO_REAL_XTTS_HTTP_BASE`.
3. Document Conclusion A or B for the `return None` + file path.
4. If **2/2** + **3/3** green: PROOF §19J, matrix, STATE; else Outcome B only.
5. Regression: `dotnet build`, `python scripts/run_verification.py`, `.\scripts\verify.ps1 -Quick`.

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-20 | Initial bounded brief — preflight vs runtime contract; **§19J** live ladder. |
| 2026-04-22 | **Closed Outcome B** — placeholder weights; preflight **8041** session; [slice19j_proof_session.md](../reports/verification/slice19/openvoice/slice19j_proof_session.md). |
