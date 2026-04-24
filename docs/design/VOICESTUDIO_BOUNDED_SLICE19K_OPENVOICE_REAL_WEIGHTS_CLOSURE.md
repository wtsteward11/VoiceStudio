# Bounded Slice 19K — OpenVoice real-weights closure attempt

**Status:** **Closed — Outcome B (2026-04-22)** per [PROOF §19K](../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md) and [`.cursor/STATE.md`](../.cursor/STATE.md) (real HF checkpoints; live ladder red — **VAD** + **non-speech** harness reference; matrix **pending**).  
**Depends on:** [Slice 19J](VOICESTUDIO_BOUNDED_SLICE19J_OPENVOICE_AUTHENTIC_WEIGHTS_LIVE_PROOF.md) (Outcome B — structural preflight green, **2 B** `checkpoint.pth` placeholders) · [ADR-054](../architecture/decisions/ADR-054-openvoice-isolated-venv-proposal.md) · [ADR-055](../architecture/decisions/ADR-055-myshell-openvoice-vendored-patches.md)

## Goal

With **Strategy B** and **`venv_openvoice`** already proven (19I/19J), close the **runtime** story by:

- **Outcome A:** Authentic MyShell OpenVoice v2 **EN** + **converter** weights on disk; **`real_openvoice` 2/2** + C# `LiveBackend` **3/3** on one URL; **Conclusion A or B** for file-driven `return None` when HTTP **200**; [ENGINE_PARITY_MATRIX.md](../reports/verification/ENGINE_PARITY_MATRIX.md) **`openvoice` → PASS**.

- **Outcome B:** Real weights **attempted** or unavailability documented; one **exact** next seam (load, worker, handoff) with verbatim evidence; matrix **pending**; **no** matrix PASS without **2/2 + 3/3**.

## Authoritative file contract (what “real weights” means)

Relative to **`VOICESTUDIO_MODELS_PATH`** (or `%ProgramData%\VoiceStudio\models` if unset), the **worker** defaults require:

| Path | Files loaded by code | Code |
| --- | --- | --- |
| `openvoice/base_speakers/EN/` | `config.json` (constructor), `checkpoint.pth` (`load_ckpt`) | [`OpenVoiceEngine._load_models`](../../app/core/engines/openvoice_engine.py); worker default `base_speaker_model` in [`openvoice_worker_synthesize.py`](../../app/cli/openvoice_worker_synthesize.py) |
| `openvoice/converter/` | `config.json`, `checkpoint.pth` | Same |

**Preflight (structural)** in [`_openvoice_has_checkpoints`](../../backend/services/model_preflight.py) only requires some `config.json` + sibling `checkpoint.*` under each tree — it does **not** validate tensor content.

**Placeholder (invalid for runtime):** 19J/19J-finding: **`checkpoint.pth` of ~2 bytes** (or any file that `load_ckpt` cannot deserialize as the OpenVoice model).

**Sanity (operator, host-only):** File size should match upstream release (typically **MB+** per checkpoint, not bytes). Upstream **source of truth:** [MyShell OpenVoice](https://github.com/myshell-ai/OpenVoice) (README / releases / linked assets). **Do not** commit `.pth` to git.

## `return None` + `SynthesisService` protocol (19K must settle on success)

- [`OpenVoiceSubprocessEngine.synthesize`](../../app/core/engines/openvoice_subprocess_engine.py): returns `None` after a **valid** output WAV path exists (size &gt; threshold).
- [`SynthesisService.synthesize`](../../backend/services/synthesis_service.py): `result is None` + `_synth_output_file_ready` → success path and artifact registration.

| Conclusion | When |
| --- | --- |
| **A** | After **200** + real WAV: file-driven `None` path is **correct**; evidence from logs or test output. |
| **B** | **200** + file on disk but **client/service** path broken (then code fix is a **follow-up** slice, with evidence) — or architecture needs in-process array return (only with proof). |
| **N/A** | Synthesis never reaches **200** (500 / init failure) — seam is **upstream** of the happy `None` contract. |

## Non-goals

- Reopen Strategy A, re-litigate ADR-055 vendoring, or add new `real_openvoice` tests.  
- RHVoice; other engines before this attempt completes honestly.  
- Matrix PASS without **2/2 + 3/3** and artifacts.

## Artifacts (this slice)

| Artifact | Role |
| --- | --- |
| [slice19k_proof_session.md](../reports/verification/slice19/openvoice/slice19k_proof_session.md) | Commands, port, `VOICESTUDIO_REAL_XTTS_HTTP_BASE`, pass/fail, return-`None` conclusion |
| [slice19k_preflight_openvoice.json](../reports/verification/slice19/openvoice/slice19k_preflight_openvoice.json) | Verbatim `GET /api/health/preflight` |

## Verification order

1. Lay authentic **EN** + **converter** trees under the model root.  
2. One fresh Uvicorn; **new** port (e.g. **8042**); one `VOICESTUDIO_REAL_XTTS_HTTP_BASE`.  
3. Preflight → `checks.openvoice.ok: true`.  
4. `pytest -m real_openvoice` then C# `LiveBackend` (same URL).  
5. Document **A / B / N/A** for `return None` per table above.  
6. If **2/2 + 3/3:** PROOF §19K, matrix, STATE; else Outcome B.  
7. Regression: `dotnet build`, `python scripts/run_verification.py`, `.\scripts\verify.ps1 -Quick`.

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-22 | Initial bounded brief — **19K** real-weights closure. |
| 2026-04-22 | **Closed Outcome B:** [session](../reports/verification/slice19/openvoice/slice19k_proof_session.md) — **HF `myshell-ai/OpenVoice` v1** weights; **8042**; **2/2 + 3/3** red; primary seam **VAD** vs **440 Hz** fixture. |
