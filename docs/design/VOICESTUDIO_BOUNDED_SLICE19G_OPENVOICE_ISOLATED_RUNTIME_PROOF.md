# Bounded Slice 19G — OpenVoice isolated runtime proof

**Status:** Closed — **Outcome B** (2026-04-22)  
**Depends on:** [VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md](VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md) (venv family + subprocess wiring), [ADR-054](../architecture/decisions/ADR-054-openvoice-isolated-venv-proposal.md) **Accepted**.

## Goal

Move OpenVoice from **implementation landed / matrix pending** to one honest outcome:

### Outcome A (runtime parity)

- `runtime/venvs/openvoice` exists and **`venv_openvoice`** import probes succeed (same bar as `ensure_openvoice`).
- Checkpoint trees under **`<VOICESTUDIO_MODELS_PATH>/openvoice/base_speakers`** and **`.../openvoice/converter`** satisfy [`ensure_openvoice`](../../backend/services/model_preflight.py).
- One **fresh** Uvicorn on a **dedicated port**; **`GET /api/health/preflight`** → **`checks.openvoice.ok == true`** (verbatim JSON artifact).
- **`pytest -m real_openvoice`** **2/2 PASS** and C# **`FullyQualifiedName~OpenVoice&TestCategory=LiveBackend`** **3/3 PASS** using the **same** `VOICESTUDIO_REAL_XTTS_HTTP_BASE`.
- **File contract verified:** `OpenVoiceSubprocessEngine.synthesize` returns **`None`** after worker writes WAV; confirm [`SynthesisService.synthesize`](../../backend/services/synthesis_service.py) accepts **`result is None`** when **`_synth_output_file_ready(output_path)`** (log or debug evidence), **or** document required engine change if live run disproves.
- Artifacts under [`docs/reports/verification/slice19/openvoice/`](../reports/verification/slice19/openvoice/) + [PROOF_SLICE19_OPENVOICE_AUDITION.md](../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md) §19G + [ENGINE_PARITY_MATRIX.md](../reports/verification/ENGINE_PARITY_MATRIX.md) **`openvoice` → PASS**.

### Outcome B (first seam frozen)

- First real failure on **`venv_openvoice`** path (pip, import, checkpoint **424**, worker stderr, HTTP synthesis, file handoff).
- **No** matrix PASS; update proof §19G + [`slice19g_proof_session.md`](../reports/verification/slice19/openvoice/slice19g_proof_session.md) with verbatim error and **one** primary seam.

## Non-goals

- No new `real_openvoice` tests or pytest markers.
- No mutation of backend **`.venv`** for engine stacks.
- No RHVoice scope; no pivot to another engine before this attempt is recorded.
- No matrix **PASS** without §19A-style live evidence.

## Verification order

1. Provision venv + shell import probes (family `python.exe`).
2. Lay checkpoints (operator weights).
3. Fresh Uvicorn + save preflight JSON.
4. If **`checks.openvoice.ok`**: `pytest -m real_openvoice` then `dotnet test` (same base URL).
5. Regression bar before any PASS claim: `dotnet build …`, `python scripts/run_verification.py`, `.\scripts\verify.ps1 -Quick`.

## References

- Harness: [PROOF_SLICE19_OPENVOICE_AUDITION.md](../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md) §19A.
- Session log: [`slice19g_proof_session.md`](../reports/verification/slice19/openvoice/slice19g_proof_session.md).
- Preflight artifact: [`slice19g_preflight_openvoice.json`](../reports/verification/slice19/openvoice/slice19g_preflight_openvoice.json).

## Closure (this session)

**Outcome B recorded:** `venv_openvoice` on disk but **`openvoice`** not importable (incomplete **`pip`** after **`av`/`faster-whisper`** chain — §19E); **`models/openvoice/`** checkpoint trees absent; **`checks.openvoice.ok`** false; **`pytest -m real_openvoice`** **2 skipped**, C# OpenVoice **`LiveBackend`** **3 skipped**. HTTP preflight + readiness probe corrected to import **`ensure_openvoice`** from **`backend.services.model_preflight`** (was stale **`backend.ml.models`** / **`torch26`**). Evidence: [slice19g_proof_session.md](../reports/verification/slice19/openvoice/slice19g_proof_session.md), [slice19g_preflight_openvoice.json](../reports/verification/slice19/openvoice/slice19g_preflight_openvoice.json), [PROOF_SLICE19_OPENVOICE_AUDITION.md](../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md) §19G.

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-22 | Initial bounded brief for isolated-runtime proof lane (19G). |
| 2026-04-22 | **Closed Outcome B** — session log + preflight JSON + preflight import-path fix; matrix **`openvoice`** still **pending**. |
