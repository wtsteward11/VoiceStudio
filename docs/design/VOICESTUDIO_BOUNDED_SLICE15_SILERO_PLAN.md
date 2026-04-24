# Bounded Slice 15 — Silero TTS parity (readiness + runtime proof)

**Status:** Active bounded slice  
**Engine id:** `silero` (authoritative; manifest [`engines/audio/silero/engine.manifest.json`](../../engines/audio/silero/engine.manifest.json))

## Execution row (single sentence)

**Silero readiness** means `checks.silero.ok == true` on `GET /api/health/preflight` (torch importable, `torch.hub` cache for `snakers4/silero-models` present when `auto_download=False`, and `torch.hub.load` succeeds for the configured language/speaker aligned with [`SileroEngine`](../../app/core/engines/silero_engine.py)); **Silero closure** means the [`ENGINE_PARITY_MATRIX.md`](../reports/verification/ENGINE_PARITY_MATRIX.md) `silero` row is **PASS** with linked evidence only after non-skipped `pytest -m real_silero`, C# live-backend synthesis + stream proofs, real artifacts under [`docs/reports/verification/slice15/`](../reports/verification/slice15/), and a green regression bar — **this slice does not** claim RHVoice or any other engine PASS, umbrella “all TTS closed,” or synthesis proof without a green preflight.

## Contract

| Item | Truth |
| --- | --- |
| Synthesis route | Same as other real proofs: `POST /api/voice/synthesize` with `"engine": "silero"` (and profile/consent/file-route shape per [`test_synthesis_espeak_ng_real.py`](../../tests/integration/test_synthesis_espeak_ng_real.py)). |
| Output | HTTP 200/201 JSON with `routed_engine == "silero"`; audio via `GET /api/audio/file/{audio_id}` as WAV/PCM (non-trivial size, RIFF). |
| Model mechanism | `torch.hub.load(repo_or_dir="snakers4/silero-models", model="silero_tts", language=..., speaker=...)` per engine defaults (`model_id` / `language` / `speaker` from engine config or `v4` / `en` / `silero_tts_{model_id}`). |
| Network / hub | First-time use may clone/fetch the hub repo; **preflight and probe call `ensure_silero(auto_download=False)`** — **no** automatic hub fetch in preflight; operators must warm `TORCH_HOME` / `torch.hub.get_dir()` once or run tooling with `auto_download=True` where allowed. |
| Preflight key | `checks.silero` (boolean `ok`, actionable `message`). |

## Out of scope

- Slice 14 RHVoice runtime closure, multi-engine refactors, Silero engine-internal loading “fallback” cleanup (tracked separately if we collapse `SileroEngine` to a single load path per no-fallbacks policy).

## Pivot reference

[PROOF_SLICE15_PIVOT_AND_NEXT_ENGINE.md](../reports/verification/PROOF_SLICE15_PIVOT_AND_NEXT_ENGINE.md)

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-19 | Initial bounded slice plan (contract freeze). |
