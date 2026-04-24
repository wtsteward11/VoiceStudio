# Bounded Slice 15 — Pivot handoff (post–Slice 14 Path 2)

**Document state:** Governance record for **Path 2** of the mentor-aligned Slice 14 post-harness plan. This is **not** a runtime parity closure for any engine.

## Decision (Path 1 vs Path 2)

| Path | Outcome |
| --- | --- |
| **Path 1** (install RHVoice → close Slice 14 runtime) | **Not taken** in this work block: no RHVoice synthesis executable was discoverable via `where.exe` for `rhvoice-say`, `rhvoice-cli`, `rhvoice-client`, or `RHVoice-test` on the host used for verification. |
| **Path 2** (honest pivot — no fake Slice 14 closure) | **Taken.** Slice 14 remains **harness landed / runtime parity not closed** for `rhvoice`. |

## Slice 14 truth (unchanged)

- Proof harness: [PROOF_SLICE14_RHVOICE_AUDITION.md](PROOF_SLICE14_RHVOICE_AUDITION.md) — PASS tables stay **TBD** until real operator evidence.
- Matrix: [ENGINE_PARITY_MATRIX.md](ENGINE_PARITY_MATRIX.md) — `rhvoice` row remains **pending runtime PASS**.

## Probe refresh (Path 2 — Phase B)

Full router probe (same mirrors as prior slices: `slice10`, `slice12`, `slice13`, `slice14`):

- **Command:** `Set-Location <repo>; $env:VOICESTUDIO_ENGINE_PROBE_FULL='1'; .\.venv\Scripts\python.exe scripts\engine_readiness_probe.py`
- **Artifact:** [slice14/engine_readiness_probe.json](slice14/engine_readiness_probe.json)
- **`timestamp_utc`:** `2026-04-18T23:37:14.364450+00:00`

**Excerpt (authoritative for Path 2 / RHVoice + pivot timing — frozen to `slice14` probe `timestamp_utc` below):**

- `router.engines.rhvoice.preflight_assets.ok` → **false** (RHVoice executable not found — same honest posture as before refresh).
- `router.engines.silero` → `registered: true`, `instantiable: true`, `preflight_assets.ok` → **null**, reason **`no ensure_* in probe (runtime-only)`**.

*That Silero line is **historical** for this decision snapshot only. Current Silero preflight truth lives in [`slice15/engine_readiness_probe.json`](slice15/engine_readiness_probe.json), [`PROOF_SLICE15_SILERO_AUDITION.md`](PROOF_SLICE15_SILERO_AUDITION.md), and [`.cursor/STATE.md`](../../.cursor/STATE.md) — see **Post–Slice 15 harness** below.*

## Post–Slice 15 harness (governance update)

**After** `2026-04-18T23:37:14Z`, bounded Slice 15 implementation landed (harness — not matrix runtime closure unless proof doc says so):

| Item | Status |
| --- | --- |
| `ensure_silero` + `checks.silero` on `GET /api/health/preflight` | Implemented — see [`backend/api/routes/health.py`](../../../backend/api/routes/health.py) |
| `engine_readiness_probe` `silero` branch | Implemented — mirror [`slice15/engine_readiness_probe.json`](slice15/engine_readiness_probe.json); `preflight_assets.ok` is **boolean** (`true` / `false`), not **null** |
| Opt-in `real_silero` pytest + C# live-backend tests | Landed — see [PROOF_SLICE15_SILERO_AUDITION.md](PROOF_SLICE15_SILERO_AUDITION.md) |

**Still not asserted by this pivot doc:** Silero **matrix PASS**, non-skipped `real_silero` PASS, or WAV artifacts — those require a **green** proof host (`checks.silero.ok == true`) and are tracked in the audition proof + matrix.

## Next bounded slice — single engine anchor

**Selected engine_id:** `silero`

| Criterion | Notes |
| --- | --- |
| Manifest | `engines/audio/silero/engine.manifest.json` |
| Router | Engine loads (`SileroEngine`); probe shows instantiable **true** |
| Preflight | **At pivot timestamp:** not yet on preflight (see excerpt above). **Post-harness:** `ensure_silero` + `checks.silero` landed — see **Post–Slice 15 harness**; runtime matrix PASS still pending green preflight + proofs. |
| Rationale | Neural TTS in-repo (torch/torch.hub style), no separate Windows CLI install like RHVoice; aligns with existing XTTS/Piper bounded proof shape once preflight is truthful |

**Explicit non-claims:** This document does **not** assert Silero synthesis PASS, matrix PASS, or WAV artifacts. Those are **Slice 15 implementation + operator runtime** work.

## Scoped work outline (Slice 15 — execution row)

1. Add **`ensure_silero`** (or equivalent) and **`checks.silero`** on `GET /api/health/preflight` with honest `ok` / messages (no silent substitute engines).
2. Extend **engine_readiness_probe** coverage for Silero preflight when implemented.
3. Add opt-in **`real_silero`** pytest + C# live-backend tests (same seam as `real_rhvoice` / `real_espeak_ng`).
4. Capture artifacts under `docs/reports/verification/slice15/` and fill a dedicated audition proof doc when runtime PASS exists.
5. Update [ENGINE_PARITY_MATRIX.md](ENGINE_PARITY_MATRIX.md) `silero` row only after real PASS evidence.

## RHVoice follow-up

Operators who install RHVoice can return to **Path 1** anytime: green `checks.rhvoice` → `pytest -m real_rhvoice` → C# tests → matrix row. That path is **independent** of Slice 15 Silero work.

## Related

- [ENGINE_PARITY_MATRIX.md](ENGINE_PARITY_MATRIX.md)
- [PROOF_SLICE14_RHVOICE_AUDITION.md](PROOF_SLICE14_RHVOICE_AUDITION.md)
- [slice14/engine_readiness_probe.json](slice14/engine_readiness_probe.json)
- [slice15/engine_readiness_probe.json](slice15/engine_readiness_probe.json)
- [PROOF_SLICE15_SILERO_AUDITION.md](PROOF_SLICE15_SILERO_AUDITION.md)

## Changelog

| Date | Change |
| --- | --- |
| 2026-04-19 | Added **Post–Slice 15 harness** and **Changelog**; clarified that the probe excerpt is **frozen historical** for Path 2; linked current Silero artifacts — **Path 2 decision text unchanged.** |
