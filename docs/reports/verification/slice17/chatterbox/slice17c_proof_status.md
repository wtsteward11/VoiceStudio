# Slice 17C — proof status (2026-04-20)

**Implementation:** `ChatterboxTorch26Engine` + `app.cli.chatterbox_worker_synthesize` (Model B — family venv subprocess). See [PROOF_SLICE17_CHATTERBOX_AUDITION.md](../../PROOF_SLICE17_CHATTERBOX_AUDITION.md) §Slice 17C.

**This session (automated):**

| Check | Result |
| --- | --- |
| `pytest -m real_chatterbox` | **SKIPPED** — `GET /api/health/preflight` → `checks.chatterbox.ok` not true against `http://127.0.0.1:8000` (listener may be stale or preflight red). |
| C# `RealSynthesisChatterbox` / `ChatterboxPlaybackAudition` | **3 skipped** (same preflight gate). |
| `docs/reports/verification/slice17/chatterbox/chatterbox_output.wav` | **Not produced** — requires non-skipped `real_chatterbox`. |

**Operator closure:** Restart backend from **current** repo code with `torch26` provisioned + green `checks.chatterbox`, then re-run the proof commands in the proof doc and copy WAV artifacts here.
