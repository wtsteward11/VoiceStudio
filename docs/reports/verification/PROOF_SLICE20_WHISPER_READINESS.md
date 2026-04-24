# PROOF — Bounded Slice 20 (Whisper readiness truth)

**Date:** 2026-04-22  
**Scope:** Replace **`checks.whisper` (`ok: null`)** on preflight with a real **`ensure_whisper`** API (delegates to **`ensure_faster_whisper`**) and boolean **`ok`** for **`engine_id: whisper`**, per [VOICESTUDIO_BOUNDED_SLICE20_WHISPER_SUPPORT_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE20_WHISPER_SUPPORT_CONTRACT.md).

## Outcome A / B (readiness only)

| Outcome | Condition |
| --- | --- |
| **A** | `GET {base}/api/health/preflight` → **`checks.whisper.ok: true`** (or **`false`** with explicit `message` — both are **honest** boolean readiness). |
| **B** | Implementation missing — preflight still **`ok: null`** for `whisper`. |

**Slice 20 (this document)** claims **Outcome A** for **wiring** — the API surface is real; on a host **without** `faster-whisper` installed, **`ok: false`** is expected and **valid**.

## What this slice does not close

- **Matrix** “full STT runtime PASS” (e.g. **`pytest -m real_whisper`** + C# transcript / HTTP proof) — **out of scope** for **20**; track as **Slice 20+** or **20D**-style live lane when added.
- **`whisper_cpp`**, **vosk**, **parakeet** — **Superseded for preflight wiring** by Slices **22** / **26** / **28** (boolean `checks.*`). This bullet reflected **Slice 20**-era `_NO_PUBLIC_PREFLIGHT` only; do not treat as current truth for those ids.

## Evidence (automated)

- **Code:** `ensure_whisper` in `backend/services/model_preflight.py` + mirror in `backend/ml/models/model_preflight.py`; **`run_preflight`** includes **`whisper`**.  
- **Health:** `backend/api/routes/health.py` — **`whisper`** removed from **`_NO_PUBLIC_PREFLIGHT`**.  
- **Probe:** `scripts/engine_readiness_probe.py` — **`whisper`** engine branch.  
- **Unit:** `tests/unit/backend/services/test_model_preflight.py` — `run_preflight` aggregation includes **`whisper`**.

## Regression bar

**2026-04-22 session:** `dotnet build` **0 errors**; `python scripts/run_verification.py` **PASS**; `.\scripts\verify.ps1 -Quick` **VERIFICATION PASSED** — [`artifacts/verify/20260422_192744/verification_report.md`](../../../artifacts/verify/20260422_192744/verification_report.md) (see [.cursor/STATE.md](../../../.cursor/STATE.md) **Last Verified**).

## Changelog

| Date | Change |
| --- | --- |
| 2026-04-22 | Initial proof: boolean `checks.whisper`; contract + matrix STT row update. |
