# Engine parity matrix (voice domain)

**Status:** Living document — **Slice 10** freezes the contract; per-engine proof status is updated when a bounded runtime slice closes.  
**Does not claim:** umbrella “synthesis works” / “all engines pass”. Each engine is independently named.

**Slice 12 governance**

- **Router unload fix:** instance unload (`unregister_engine`) must **not** remove the engine id from `engine_router.list_engines()` — see `app/core/engines/router.py` and [PROOF_SLICE12_ESPEAK_NG_AUDITION.md](PROOF_SLICE12_ESPEAK_NG_AUDITION.md).

**Slice 13 governance**

- **`ensure_rhvoice`:** `GET /api/health/preflight` exposes `checks.rhvoice` (local `rhvoice-say` / `rhvoice-cli` or configured `executable_path`). Path B closure: binary absent on operator host — synth/retrieval/playback proof **deferred to Slice 14** — see [PROOF_SLICE13_RHVOICE_AUDITION.md](PROOF_SLICE13_RHVOICE_AUDITION.md).

**Slice 14 governance**

- **Harness landed:** `real_rhvoice` pytest (`tests/integration/test_synthesis_rhvoice_real.py`), C# `RealSynthesisRhVoiceLiveBackendTests` + `RhVoicePlaybackAuditionLiveBackendTests`, proof doc [PROOF_SLICE14_RHVOICE_AUDITION.md](PROOF_SLICE14_RHVOICE_AUDITION.md). **Runtime PASS** (matrix row **PASS**) only after operator install + green `checks.rhvoice.ok` + commands in proof doc — no fabricated WAV/PASS lines.
- **Path 1 (2026-04-19):** Probe refresh [`slice14/engine_readiness_probe.json`](slice14/engine_readiness_probe.json) (`timestamp_utc` **2026-04-19T19:13:20Z**) — `rhvoice.preflight_assets.ok` still **false** (CLI missing); see proof doc **Path 1 attempt** section.

**Slice 14B governance (2026-04-19 — Mode B runtime parity attempt)**

- **Scope:** RHVoice only; **Mode B** — `engine_configs.rhvoice.parameters.executable_path` (no PATH/package assumption for closure). See proof doc **Slice 14B** section.
- **Outcome (this session):** No real RHVoice CLI on host; `executable_path` placeholder only; probe + preflight **red**; `pytest -m real_rhvoice` **2 skipped**; C# RhVoice filters **3 skipped**; **no artifacts**; matrix **`rhvoice` row still pending** — [PROOF_SLICE14_RHVOICE_AUDITION.md](PROOF_SLICE14_RHVOICE_AUDITION.md).

**Slice 16 governance (2026-04-18 — RHVoice support-contract truth)**

- **Contract:** [VOICESTUDIO_BOUNDED_SLICE16_RHVOICE_SUPPORT_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE16_RHVOICE_SUPPORT_CONTRACT.md) — Modes A–D; **Mode D** (external/manual RHVoice runtime on typical Windows) + **Mode B** (`engine_configs.rhvoice.parameters.executable_path`); `executable_path` merged into `RHVoiceEngine` init via `EngineRouter`. **No WSL wrapper** unless added under a future ADR.
- **Matrix:** **`rhvoice` row unchanged** — still **pending runtime PASS** until live `pytest -m real_rhvoice` + C# PASS lines; Slice 16 fixes contract drift, not fabricated synthesis closure.

**Slice 17 governance (2026-04-20 — Chatterbox readiness + bounded parity)**

- **Contract:** [VOICESTUDIO_BOUNDED_SLICE17_CHATTERBOX_SUPPORT_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE17_CHATTERBOX_SUPPORT_CONTRACT.md) — `ensure_chatterbox` + `checks.chatterbox` (boolean; HF repo **`ResembleAI/chatterbox`**, no silent download when `auto_download=False`); probe mirror [`slice17/engine_readiness_probe.json`](slice17/engine_readiness_probe.json); proof [PROOF_SLICE17_CHATTERBOX_AUDITION.md](PROOF_SLICE17_CHATTERBOX_AUDITION.md). **RHVoice:** no churn — still deferred per Slice 14B/16.
- **Matrix:** **`chatterbox` row — PASS** (Slice 17D) — non-skipped `real_chatterbox` + C# PASS on dedicated-port backend; proof [PROOF_SLICE17_CHATTERBOX_AUDITION.md](PROOF_SLICE17_CHATTERBOX_AUDITION.md) §Slice 17D.

**Slice 17A governance (2026-04-20 — Chatterbox `venv_advanced_tts` authority)**

- **Contract:** [VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md) — preflight probes **family venv** Python (`import` + HF cache subprocess), not the API worker interpreter alone.
- **Matrix:** Superseded for **venv missing** on hosts where **`runtime/venvs/torch26`** is provisioned — see **Slice 17B** in proof.

**Slice 17B governance (2026-04-20 — `torch26` provision + preflight green)**

- **Outcome:** Family venv on disk **`runtime/venvs/torch26`** aligned with `VenvFamily.ADVANCED_TTS`; `checks.chatterbox.ok` **true**; probe [`slice17/engine_readiness_probe.json`](slice17/engine_readiness_probe.json) **`2026-04-20T11:55:08.699504+00:00`** — `preflight_assets.ok: true`. **`pytest -m real_chatterbox`** still **skipped** / C# **fail** — **engine router** does not initialize Chatterbox in the API process (synthesis 500). Proof [PROOF_SLICE17_CHATTERBOX_AUDITION.md](PROOF_SLICE17_CHATTERBOX_AUDITION.md) §Slice 17B.

**Slice 17C governance (2026-04-20 — router/runtime Model B)**

- **Implementation:** Manifest `entry_point` → **`ChatterboxTorch26Engine`** — synthesis via **`python -m app.cli.chatterbox_worker_synthesize`** in **`VenvFamily.ADVANCED_TTS`** (`runtime/venvs/torch26`); API worker does not import `chatterbox.tts`. Proof [PROOF_SLICE17_CHATTERBOX_AUDITION.md](PROOF_SLICE17_CHATTERBOX_AUDITION.md) §Slice 17C; status [`slice17/chatterbox/slice17c_proof_status.md`](slice17/chatterbox/slice17c_proof_status.md).
- **Matrix (superseded by Slice 17D):** **`chatterbox` PASS** — see **Slice 17D** in [PROOF_SLICE17_CHATTERBOX_AUDITION.md](PROOF_SLICE17_CHATTERBOX_AUDITION.md); dedicated port + `VOICESTUDIO_REAL_XTTS_HTTP_BASE` + artifacts under `slice17/chatterbox/`.

**Slice 19A governance (2026-04-21 — OpenVoice runtime parity harness)**

- **Harness:** `pytest -m real_openvoice` (`tests/integration/test_synthesis_openvoice_real.py`); C# **`RealSynthesisOpenVoiceLiveBackendTests`** + **`OpenVoicePlaybackAuditionLiveBackendTests`** (`LiveBackend`); **`AssertOpenVoicePreflightOkAsync`**. Proof [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) §19A; operator notes [`slice19/openvoice/README.md`](slice19/openvoice/README.md). **Dedicated port narrative:** **`http://127.0.0.1:8031`**; **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** identical for Python + C# (same discipline as 17D/18D).
- **Matrix:** **`openvoice` runtime PASS still pending** until operator Branch A — **`real_openvoice` 2/2** + C# OpenVoice filter **3/3** + artifacts under `slice19/openvoice/`; **no** matrix PASS on readiness or harness alone.

**Slice 19B governance (2026-04-21 — OpenVoice live-proof closure attempt, Branch B)**

- **Session:** Fresh Uvicorn **`http://127.0.0.1:8032`** (current `health.py`); **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** identical for pytest + C#. **Stale-port warning:** long-lived **8031** listener showed legacy `checks.openvoice` (`ok: null`) — restart on current code before trusting **8031**.
- **Primary seam:** `ensure_openvoice` → **`ModuleNotFoundError: No module named 'openvoice'`** in **`runtime/venvs/torch26`** (`checks.openvoice.ok: false`, HTTP preflight 200 with boolean). **No** synthesis attempted.
- **Proofs:** `pytest -m real_openvoice` **2 skipped**; C# OpenVoice `LiveBackend` **3 skipped**. Artifacts: [`slice19b_proof_session.md`](slice19/openvoice/slice19b_proof_session.md), [`slice19b_preflight_openvoice.json`](slice19/openvoice/slice19b_preflight_openvoice.json). Proof [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) §19B.
- **Matrix:** **`openvoice` row unchanged** (pending runtime PASS).

**Slice 19C governance (2026-04-21 — OpenVoice `torch26` provisioning)**

- **Scope:** Install **`myshell-openvoice`** into **`runtime/venvs/torch26`** (not backend `.venv`); satisfy `ensure_openvoice` checkpoint layout; fresh backend + **`checks.openvoice.ok: true`** before live proofs — [VOICESTUDIO_BOUNDED_SLICE19C_OPENVOICE_PROVISIONING.md](../../design/VOICESTUDIO_BOUNDED_SLICE19C_OPENVOICE_PROVISIONING.md). **No** new harness; **RHVoice** unchanged.
- **Outcome (this session — Branch B):** **`pip install`** pinned OpenVoice **failed** — dependency **`av==10.*`** (via **`faster-whisper==0.9.0`**) **wheel build** **Cython `CompileError`** (`av\logging.pyx`). **Additionally:** operator models root had **no** `openvoice/` checkpoint trees (`base_speakers` / `converter`). Session: [`slice19c_proof_session.md`](slice19/openvoice/slice19c_proof_session.md); proof [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) §19C.
- **Matrix:** **`openvoice` row** remains **pending** until §19A-style Branch A (2/2 + 3/3 + artifacts) after install + checkpoints + green preflight.

**Slice 19D governance (2026-04-20 — OpenVoice packaging + runtime surface)**

- **Provenance:** [`slice19d_openvoice_package_provenance.md`](slice19/openvoice/slice19d_openvoice_package_provenance.md) — exact Git pin, **`torch26`** interpreter, failing **`pip`** command, upstream **`setup.py`** **`install_requires`** (**`faster-whisper==0.9.0`** mandatory — **not** optional extras), Windows **`av` 10** wheel gap + dry-run contrast.
- **Policy (historical §19D):** [ADR-053](../../architecture/decisions/ADR-053-openvoice-advanced-tts-packaging-surface.md) recorded **`ADVANCED_TTS` → `torch26`** for OpenVoice. **Slice 19F:** **[ADR-054](../../architecture/decisions/ADR-054-openvoice-isolated-venv-proposal.md) Accepted** — OpenVoice uses **`venv_openvoice` → `runtime/venvs/openvoice`**; **`torch26`** remains Chatterbox / advanced-TTS shared surface only.
- **Closure honesty:** **`pip` / import** not green (same blocker as 19C); checkpoint trees **not** laid down in repo; **no** fresh Uvicorn preflight re-run and **no** `real_openvoice` / C# live re-run (plan gate). Proof [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) §19D.
- **Matrix:** **`openvoice`** still **pending** runtime PASS.

**Slice 19E governance (2026-04-21 — OpenVoice dependency unblocker, Outcome B evidence)**

- **Graph + dry-run:** [`slice19e_openvoice_dependency_graph.md`](slice19/openvoice/slice19e_openvoice_dependency_graph.md) — resolver collects **`gradio`**, **`whisper-timestamped`**, **`numpy==1.22.0`**, …; fails building **`av==10.*`** from **`faster-whisper==0.9.0`**; **`se_extractor`** top-level **`faster_whisper`** import proves runtime need beyond **`openvoice.api`** alone.
- **Strategy:** **C** — evidence for isolated venv; **[ADR-054](../../architecture/decisions/ADR-054-openvoice-isolated-venv-proposal.md)** was **Proposed** at §19E closure — **Accepted** in **Slice 19F** (implementation).
- **Gates:** Import probe still **`ModuleNotFoundError`**; **`models/openvoice/`** absent; **no** green preflight / **no** `real_openvoice` / C# live re-proof. Proof [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) §19E.
- **Matrix:** **`openvoice`** **pending** (no PASS claim).

**Slice 19F governance (2026-04-21 — OpenVoice isolated venv + subprocess)**

- **Brief:** [VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md](../../design/VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md); proof [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) §19F; **ADR-054 Accepted**.
- **Seams:** `VenvFamily.OPENVOICE` → `runtime/venvs/openvoice`; manifest `venv_family: venv_openvoice`; `ensure_openvoice` subprocess-probes that interpreter; **`OpenVoiceSubprocessEngine`** + **`app.cli.openvoice_worker_synthesize`** (API worker does not import OpenVoice/torch for this path).
- **Preflight artifact (implementation session, operator still required):** [`slice19f_preflight_openvoice.json`](slice19/openvoice/slice19f_preflight_openvoice.json) — documents expected **`checks.openvoice.ok: false`** until venv + deps + checkpoints green; **no** matrix PASS without §19A Branch A live ladder.
- **Matrix:** **`openvoice`** **pending** runtime PASS.

**Slice 19G governance (2026-04-22 — OpenVoice isolated runtime proof; Outcome B)**

- **Brief:** [VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md](../../design/VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md); proof [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) §19G; session [`slice19g_proof_session.md`](slice19/openvoice/slice19g_proof_session.md).
- **Preflight truth fix:** `GET /api/health/preflight` + `scripts/engine_readiness_probe.py` now call **`ensure_openvoice`** from **`backend.services.model_preflight`** (ADR-054), not stale **`backend.ml.models`** (**`torch26`** narrative).
- **Runtime:** **`venv_openvoice`** interpreter present; **`openvoice`** package **not** installed (**`pip`/`av`** chain — same class as §19C/§19E); **`models/openvoice/`** absent; dedicated backend **`http://127.0.0.1:8036`**; **`checks.openvoice.ok: false`** — artifact [`slice19g_preflight_openvoice.json`](slice19/openvoice/slice19g_preflight_openvoice.json); **`pytest -m real_openvoice` 2 skipped**; C# OpenVoice **`LiveBackend` 3 skipped**.
- **Matrix:** **`openvoice`** **pending** (no PASS).

**Slice 19H governance (2026-04-22 — OpenVoice venv provisioning; Strategy A Outcome B)**

- **Brief:** [VOICESTUDIO_BOUNDED_SLICE19H_OPENVOICE_VENV_PROVISIONING.md](../../design/VOICESTUDIO_BOUNDED_SLICE19H_OPENVOICE_VENV_PROVISIONING.md); proof [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) §19H; session [`slice19h_proof_session.md`](slice19/openvoice/slice19h_proof_session.md); pip log [`slice19h_pip_myshell_attempt.log.txt`](slice19/openvoice/slice19h_pip_myshell_attempt.log.txt).
- **Strategy A:** stock **`myshell-openvoice@74a1d147…`** **`pip install`** → **`av==10.*`** sdist **Cython `CompileError`** (`av\logging.pyx`) — same seam class as §19E; **`openvoice`** / **`faster_whisper`** not importable in **`venv_openvoice`**.
- **Strategy B:** fork / metadata / supply-chain change — **mandatory next bounded slice** (ADR + ADR-044); **not** done in 19H.
- **Preflight / live:** [`slice19h_preflight_openvoice.json`](slice19/openvoice/slice19h_preflight_openvoice.json) red; **`real_openvoice` 2 skipped**; C# **3 skipped** (ports **8037/8038** session).
- **Matrix:** **`openvoice`** **pending**.

**Slice 19I governance (2026-04-22 — OpenVoice Strategy B; ADR-055; live ladder Outcome B)**

- **Brief + ADR:** [VOICESTUDIO_BOUNDED_SLICE19I_OPENVOICE_STRATEGY_B_RUNTIME.md](../../design/VOICESTUDIO_BOUNDED_SLICE19I_OPENVOICE_STRATEGY_B_RUNTIME.md); [ADR-055](../../architecture/decisions/ADR-055-myshell-openvoice-vendored-patches.md); proof [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) **§19I**; preflight [`slice19i_preflight_openvoice.json`](slice19/openvoice/slice19i_preflight_openvoice.json).
- **Strategy B:** Vendored **`runtime/vendor/myshell-openvoice`** (commit **74a1d147**) + patches; **`create_engine_venv --family openvoice --force`** green; import gates + **`checks.openvoice.ok: true`** on **8040** session (with **`VOICESTUDIO_MODELS_PATH`** set for checkpoint **layout**).
- **Runtime proofs:** **`pytest -m real_openvoice` 2/2 FAIL**; C# **OpenVoice `LiveBackend` 3/3 FAIL** — **`POST /api/voice/synthesize` 500** / engine returned **None** (non-valid placeholder `.pth` in this session).
- **Matrix:** **`openvoice` still pending** (no **PASS** until **2/2** + **3/3** green on real weights).

**Slice 19J governance (2026-04-20 — OpenVoice authentic-weights + live proof ladder)**

- **Brief:** [VOICESTUDIO_BOUNDED_SLICE19J_OPENVOICE_AUTHENTIC_WEIGHTS_LIVE_PROOF.md](../../design/VOICESTUDIO_BOUNDED_SLICE19J_OPENVOICE_AUTHENTIC_WEIGHTS_LIVE_PROOF.md) — **preflight (structural) vs runtime (load_ckpt) weight contract**; official weights from [MyShell OpenVoice](https://github.com/myshell-ai/OpenVoice) (operator-local; not in git). Proof [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) **§19J**; sessions [`slice19j_proof_session.md`](slice19/openvoice/slice19j_proof_session.md) / [`slice19j_preflight_openvoice.json`](slice19/openvoice/slice19j_preflight_openvoice.json).
- **Rule:** **No** matrix **PASS** without **2/2** + **3/3** on the **same** `VOICESTUDIO_REAL_XTTS_HTTP_BASE` after **`checks.openvoice.ok: true`**; **Conclusion A/B** required for `OpenVoiceSubprocessEngine` **`return None`** + file-ready seam.
- **Outcome (2026-04-22):** **Outcome B** — placeholders unchanged; [slice19j_preflight_openvoice.json](slice19/openvoice/slice19j_preflight_openvoice.json) + [slice19j_proof_session.md](slice19/openvoice/slice19j_proof_session.md); **matrix `openvoice` pending**.
- **Matrix:** See **openvoice** row in the table and **STATE** for latest **19J** closure.

**Slice 19K governance (2026-04-22 — OpenVoice real-weights + live ladder; Outcome B)**

- **Brief:** [VOICESTUDIO_BOUNDED_SLICE19K_OPENVOICE_REAL_WEIGHTS_CLOSURE.md](../../design/VOICESTUDIO_BOUNDED_SLICE19K_OPENVOICE_REAL_WEIGHTS_CLOSURE.md); proof [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) **§19K**; session [`slice19k_proof_session.md`](slice19/openvoice/slice19k_proof_session.md); preflight [`slice19k_preflight_openvoice.json`](slice19/openvoice/slice19k_preflight_openvoice.json).
- **Weights:** **Official** HuggingFace **`myshell-ai/OpenVoice`** `checkpoints/base_speakers/EN` + `checkpoints/converter` (v1 tree) copied to `openvoice/...` under **`VOICESTUDIO_MODELS_PATH`**; worker reports **`OpenVoice version: v1`** and **clean** `load_ckpt`.
- **Runtime proofs:** **`pytest -m real_openvoice` 2/2 FAIL**; C# **3/3 FAIL** — **500** / engine returned **None**; **primary seam** = **`se_extractor` + VAD** vs **non-speech** `test_440hz_2s.wav` (not 2-byte placeholders). **Task 7** `return None` / file contract **N/A** (no **200**).
- **Matrix:** **`openvoice` still pending** (no **PASS** until **2/2** + **3/3** green).

**Slice 19L governance (2026-04-22 — OpenVoice reference-audio + VAD contract; Policy A; Outcome B)**

- **Brief:** [VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md); proof [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) **§19L**; session [`slice19l_proof_session.md`](slice19/openvoice/slice19l_proof_session.md); preflight [`slice19l_preflight_openvoice.json`](slice19/openvoice/slice19l_preflight_openvoice.json).
- **Harness:** `tests/fixtures/audio/openvoice_reference_speech.wav` + optional **`VOICESTUDIO_OPENVOICE_PROOF_REFERENCE_WAV`**; Python `real_openvoice` + C# `OpenVoiceProofFixtures.ResolveOpenVoiceReferenceWavPath` — **not** `test_440hz_2s.wav` for OpenVoice.
- **Runtime proofs:** **`pytest -m real_openvoice` 2/2 FAIL**; C# **3/3 FAIL** — **500** / *engine returned None* with **200** preprocess on speech reference (**differs** from **§19K** VAD-on-tone seam). **Conclusion A/B** for file-driven `None` **N/A** (no synthesis **200**).
- **Matrix:** **`openvoice` still pending**.

**Slice 19M governance (2026-04-20 — OpenVoice worker + synthesis path; code fixes; matrix still pending re-ladder)**

- **Brief + proof:** [VOICESTUDIO_BOUNDED_SLICE19M_OPENVOICE_WORKER_SYNTHESIS_PATH.md](../../design/VOICESTUDIO_BOUNDED_SLICE19M_OPENVOICE_WORKER_SYNTHESIS_PATH.md); [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) **§19M**; [slice19m_worker_capture.md](slice19/openvoice/slice19m_worker_capture.md); [slice19m_proof_session.md](slice19/openvoice/slice19m_proof_session.md).
- **Root-cause (closed in code):** myshell `BaseSpeakerTTS.tts` API + `se_extractor.get_se` tuple return + Windows stdio encoding for the worker; **Conclusion A** for `return None` + on-disk WAV (see **§19M**).
- **Matrix:** **`openvoice` → PASS** — **2/2 + 3/3** on **`http://127.0.0.1:8055`** (2026-04-22); [PROOF](PROOF_SLICE19_OPENVOICE_AUDITION.md) **§19M**.

**Slice 18D governance (2026-04-20 — Tortoise live proof closure)**

- **Proof:** [PROOF_SLICE18_TORTOISE_AUDITION.md](PROOF_SLICE18_TORTOISE_AUDITION.md) §Slice 18D; session log [`slice18/tortoise/slice18d_proof_session.md`](slice18/tortoise/slice18d_proof_session.md). **Backend** `http://127.0.0.1:8028` (single uvicorn); **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** identical for **`pytest -m real_tortoise` 2/2** and **C# Tortoise `LiveBackend` 3/3**. **Matrix `tortoise` → PASS** (runtime parity). **RHVoice** unchanged.

**Slice 18A governance (2026-04-20 — Tortoise provisioning attempt; second blocker)**

- **Proof:** [PROOF_SLICE18A_TORTOISE_PROVISIONING.md](PROOF_SLICE18A_TORTOISE_PROVISIONING.md) — **Outcome B:** `pip install tortoise-tts` into backend `.venv` conflicts with **`coqui-tts` / `transformers`** pins; restoring `transformers` for XTTS breaks Tortoise (`LogitsWarper`). **Superseded for matrix** by **18D** + ADR-052 — historical blocker narrative retained in provisioning proof.

**Slice 15 governance (Path 2 pivot — 2026-04-18; Silero runtime — 2026-04-19)**

- **Pivot:** Mentor-aligned post–Slice 14 decision — RHVoice CLI not on operator PATH → **no fake Slice 14 runtime closure**. Bounded work for **Silero** (`silero`) per [PROOF_SLICE15_PIVOT_AND_NEXT_ENGINE.md](PROOF_SLICE15_PIVOT_AND_NEXT_ENGINE.md). **`rhvoice` matrix row unchanged** (pending).
- **Slice 15 Silero closure:** **`silero` row PASS** — preflight `checks.silero` + `real_silero` + C# live proofs + artifacts — [PROOF_SLICE15_SILERO_AUDITION.md](PROOF_SLICE15_SILERO_AUDITION.md) (2026-04-19).

**Sources of truth**

| Source | Role |
| --- | --- |
| `engines/**/engine.manifest.json` | Declared engine ids, entry points, subtype |
| `engine_router.list_engines()` | Runtime registration (after `load_all_engines`) |
| `GET /api/health/preflight` `checks` | `ensure_*` preflight where implemented; `ok: null` = no public API |
| `docs/reports/verification/slice12/engine_readiness_probe.json` | Authoritative full-router snapshot when `VOICESTUDIO_ENGINE_PROBE_FULL=1` (operator session) |
| `docs/reports/verification/slice13/engine_readiness_probe.json` | Slice 13 artifact (same payload as slice12 mirror; RHVoice preflight row) |
| `docs/reports/verification/slice14/engine_readiness_probe.json` | Slice 14 artifact (same payload as slice12 mirror; RHVoice runtime slice) |
| `docs/reports/verification/slice15/engine_readiness_probe.json` | Slice 15 mirror (same payload as slice12; Silero `preflight_assets` truth) |
| `docs/reports/verification/slice17/engine_readiness_probe.json` | Slice 17 mirror (Chatterbox `preflight_assets` truth) |
| `docs/reports/verification/slice10/engine_readiness_probe.json` | Legacy mirror / fast scan reference |

**Slice 10 governance**

- **Removed:** automatic TTS engine substitution when the requested engine is missing from `engine_router.list_engines()` (`SynthesisService.synthesize` no longer walks `resolve_engine_priority` fallback chain). Invalid engine → `InvalidEngineException`.
- **Added:** `routed_engine` on `VoiceSynthesizeResponse` / C# `RoutedEngine` — must match the engine that produced audio (stub uses `stub`). Explicit `synthesize_with_utility` tests remain in `tests/integration/test_tts_utilities.py` — not an automatic synthesis path.
- **Slice 11 (2026-04-18):** `_try_utility_tts_fallback` **removed** from `SynthesisService` and `voice/_helpers.py`. Primary engine failure → explicit processing error; **no** automatic `gtts_utility` / `pyttsx3_utility` substitution. Proof: [PROOF_SLICE11_NO_FALLBACKS_REMOVAL.md](PROOF_SLICE11_NO_FALLBACKS_REMOVAL.md).

## TTS engines (proof shape: synth → `GET /api/audio/file/{id}` → client stream → optional NAudio)

| engine_id | Intended runtime | Manifest | Preflight key | Synth proof | Retrieval proof | Playback proof | UI proof | First blocker / notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| xtts_v2 | Local GPU/CPU Coqui | `engines/audio/xtts_v2/engine.manifest.json` | `checks.xtts_v2` | **PASS** — [PROOF_SLICE9_PLAYBACK_AUDITION.md](PROOF_SLICE9_PLAYBACK_AUDITION.md) | **PASS** (same doc) | **PASS** stream + NAudio (same doc) | optional | Slice 9 closed (XTTS-only). |
| piper | Local ONNX fast TTS | `engines/audio/piper/engine.manifest.json` | `checks.piper` | **PASS** — [PROOF_SLICE10_PIPER_AUDITION.md](PROOF_SLICE10_PIPER_AUDITION.md) | **PASS** (same doc) | **PASS** stream + NAudio (same doc) | optional | Slice 10 engine-specific closure (non-XTTS). |
| espeak_ng | Local eSpeak NG CLI | `engines/audio/espeak_ng/engine.manifest.json` | `checks.espeak_ng` | **PASS** — [PROOF_SLICE12_ESPEAK_NG_AUDITION.md](PROOF_SLICE12_ESPEAK_NG_AUDITION.md) | **PASS** (same doc) | **PASS** stream + NAudio (same doc) | optional | Slice 12 closure; GPL-3.0 manifest license — operator due diligence. |
| rhvoice | External RHVoice CLI (not stock Windows) | `engines/audio/rhvoice/engine.manifest.json` | `checks.rhvoice` | **pending runtime PASS** — harness [PROOF_SLICE14_RHVOICE_AUDITION.md](PROOF_SLICE14_RHVOICE_AUDITION.md); set `engine_configs.rhvoice.parameters.executable_path` or PATH per [Slice 16 contract](../../design/VOICESTUDIO_BOUNDED_SLICE16_RHVOICE_SUPPORT_CONTRACT.md) | pending | pending | optional | **Slice 16:** contract + wiring frozen — **tier2 / external runtime** on typical Windows; matrix **PASS** only after real `pytest -m real_rhvoice` + C# PASS lines; GPL-3.0 — operator due diligence. |
| chatterbox | Optional pip package (`venv_advanced_tts` / provision **`runtime/venvs/torch26`**) | `engines/audio/chatterbox/engine.manifest.json` | `checks.chatterbox` (`ensure_chatterbox`) | **PASS** — [PROOF_SLICE17_CHATTERBOX_AUDITION.md](PROOF_SLICE17_CHATTERBOX_AUDITION.md) §Slice 17D; contracts [VOICESTUDIO_BOUNDED_SLICE17_CHATTERBOX_SUPPORT_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE17_CHATTERBOX_SUPPORT_CONTRACT.md) + [VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md) | **PASS** | **PASS** stream + NAudio (same doc) | optional | **Slice 17D:** dedicated port + `VOICESTUDIO_REAL_XTTS_HTTP_BASE`; Model B worker; IEEE float WAV accepted by proof harness. |
| tortoise | Optional pip (`tortoise-tts` + `torch`; **`venv_tortoise`** + subprocess per ADR-052; cache under `tortoise_models`) | `engines/audio/tortoise/engine.manifest.json` | `checks.tortoise` (`ensure_tortoise`) | **PASS** — [PROOF_SLICE18_TORTOISE_AUDITION.md](PROOF_SLICE18_TORTOISE_AUDITION.md) §Slice 18D; contract [VOICESTUDIO_BOUNDED_SLICE18_TORTOISE_SUPPORT_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE18_TORTOISE_SUPPORT_CONTRACT.md) | **PASS** | **PASS** stream + NAudio (same doc) | optional | **Slice 18D:** dedicated **8028** + `VOICESTUDIO_REAL_XTTS_HTTP_BASE`; `real_tortoise` **2/2**; C# **3/3**; artifacts [`slice18/tortoise/`](slice18/tortoise/). Historical **18A** schism resolved by isolated venv — not co-installed in backend `.venv`. |
| bark | Optional pip | `engines/audio/bark/engine.manifest.json` | `checks.bark` (`ok: null`) | none | none | none | none | Slice 12 candidate **blocked** for bounded real proof: HF `suno/bark` weight fetch 404 in session; no substitute proof engine. |
| openvoice | Isolated venv + subprocess (**`venv_openvoice`** / `runtime/venvs/openvoice`; ADR-054) + **Strategy B** vendored stack (**ADR-055**, `runtime/vendor/myshell-openvoice`) | `engines/audio/openvoice/engine.manifest.json` | `checks.openvoice` (`ok: bool` — `ensure_openvoice` in **`venv_openvoice`**; no silent download) | **PASS** — **§19M** (2026-04-22): **`pytest -m real_openvoice` 2/2** + C# OpenVoice `LiveBackend` **3/3** on **`http://127.0.0.1:8055`**; [PROOF](PROOF_SLICE19_OPENVOICE_AUDITION.md) **§19M**; worker fixes (TTS API, **SE** tuple unpack, `PYTHONIOENCODING`, stdio) | **PASS** (same session) | **PASS** (same session) | optional | **Closed** for matrix — bounded proof like 17D/18D; operator may use any dedicated port + one **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`**. |
| fish_speech | Optional | `engines/audio/fish_speech/engine.manifest.json` | `checks.fish_speech` (`ok: null`) | none | none | none | none | — |
| gpt_sovits | Training-heavy | `engines/audio/gpt_sovits/engine.manifest.json` | `checks.gpt_sovits` (`ok: null`) | none | none | none | none | — |
| higgs_audio | Optional | `engines/audio/higgs_audio/engine.manifest.json` | `checks.higgs_audio` (`ok: null`) | none | none | none | none | — |
| silero | Local PyTorch / torch.hub TTS | `engines/audio/silero/engine.manifest.json` | `checks.silero` (`ensure_silero`) | **PASS** — [PROOF_SLICE15_SILERO_AUDITION.md](PROOF_SLICE15_SILERO_AUDITION.md); bounded plan [VOICESTUDIO_BOUNDED_SLICE15_SILERO_PLAN.md](../../design/VOICESTUDIO_BOUNDED_SLICE15_SILERO_PLAN.md) | **PASS** (same doc) | **PASS** stream + NAudio (same doc) | optional | **Slice 15 closed (2026-04-19):** warm `torch.hub` / `snakers4/silero-models` once; preflight uses `auto_download=False`. |

## STT engines (different proof shape — transcript JSON, not umbrella “playback parity”)

| engine_id | Notes |
| --- | --- |
| whisper | `checks.whisper` (`ok: bool` — **`ensure_whisper` → `ensure_faster_whisper`**) — [PROOF_SLICE20_WHISPER_READINESS.md](PROOF_SLICE20_WHISPER_READINESS.md); contract [VOICESTUDIO_BOUNDED_SLICE20_WHISPER_SUPPORT_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE20_WHISPER_SUPPORT_CONTRACT.md). **Readiness** boolean on preflight; **full STT live transcript proof** (matrix-style closure) still **future** bounded slice. |
| whisper_cpp | `checks.whisper_cpp` (`ok: null`) — deferred (separate contract) |
| vosk | `checks.vosk` (`ok: null`) |
| parakeet | `checks.parakeet` (`ok: null`) |

## STS / voice conversion (audio→audio; not Slice 10)

| engine_id | Preflight |
| --- | --- |
| sovits_svc | `checks.sovits_svc` (`ensure_sovits`) |

## Changelog

| Date | Change |
| --- | --- |
| 2026-04-22 | **Slice 20 Whisper readiness (boolean preflight):** [PROOF_SLICE20_WHISPER_READINESS.md](PROOF_SLICE20_WHISPER_READINESS.md); [VOICESTUDIO_BOUNDED_SLICE20_WHISPER_SUPPORT_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE20_WHISPER_SUPPORT_CONTRACT.md); `checks.whisper` no longer **`ok: null`** — **`ensure_whisper`** / faster-whisper; **STT** section updated. |
| 2026-04-22 | **Slice 19M OpenVoice worker + synthesis (matrix PASS):** [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) **§19M**; [VOICESTUDIO_BOUNDED_SLICE19M_OPENVOICE_WORKER_SYNTHESIS_PATH.md](../../design/VOICESTUDIO_BOUNDED_SLICE19M_OPENVOICE_WORKER_SYNTHESIS_PATH.md); **`http://127.0.0.1:8055`** — **`real_openvoice` 2/2** + C# OpenVoice **3/3**; **`openvoice` → PASS**. |
| 2026-04-22 | **Slice 19L OpenVoice reference-audio + VAD contract (Policy A; Outcome B):** [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) **§19L**; [VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md); dedicated **8043**; speech fixture; **`openvoice` row still pending** — **2/2 + 3/3** red (synthesis **500** after **200** preprocess; not **§19K** 440 Hz seam). |
| 2026-04-22 | **Slice 19K OpenVoice real-weights attempt (Outcome B):** [PROOF_SLICE19_OPENVOICE_AUDITION.md](PROOF_SLICE19_OPENVOICE_AUDITION.md) **§19K**; [VOICESTUDIO_BOUNDED_SLICE19K_OPENVOICE_REAL_WEIGHTS_CLOSURE.md](../../design/VOICESTUDIO_BOUNDED_SLICE19K_OPENVOICE_REAL_WEIGHTS_CLOSURE.md); HF **`myshell-ai/OpenVoice` v1** checkpoints on operator disk; **`openvoice` row still pending** — **2/2 + 3/3** red (**VAD** vs non-speech reference). |
| 2026-04-20 | **Slice 18D Tortoise runtime PASS:** [PROOF_SLICE18_TORTOISE_AUDITION.md](PROOF_SLICE18_TORTOISE_AUDITION.md) §Slice 18D; `http://127.0.0.1:8028`; `real_tortoise` **2/2**; C# Tortoise **3/3**; matrix **`tortoise` → PASS**; [`slice18/tortoise/slice18d_proof_session.md`](slice18/tortoise/slice18d_proof_session.md). |
| 2026-04-20 | **Slice 18A Tortoise provisioning (Outcome B):** [PROOF_SLICE18A_TORTOISE_PROVISIONING.md](PROOF_SLICE18A_TORTOISE_PROVISIONING.md) — second blocker = **`transformers` / coqui vs tortoise-tts** in one `.venv`; matrix **`tortoise` pending**; probe refresh **`2026-04-20T17:38:45.988793+00:00`**. |
| 2026-04-20 | **Slice 18 Tortoise readiness:** `ensure_tortoise` + boolean `checks.tortoise` (no `ok: null`); probe [`slice18/engine_readiness_probe.json`](slice18/engine_readiness_probe.json); proof [PROOF_SLICE18_TORTOISE_AUDITION.md](PROOF_SLICE18_TORTOISE_AUDITION.md); matrix **`tortoise` pending** — first blocker = **`tortoise` package missing** on proof host (see proof Outcome B). |
| 2026-04-20 | **Slice 17D Chatterbox runtime PASS:** `real_chatterbox` **2/2** + C# Chatterbox filters **3/3** on `http://127.0.0.1:8027` with `VOICESTUDIO_REAL_XTTS_HTTP_BASE`; proof [PROOF_SLICE17_CHATTERBOX_AUDITION.md](PROOF_SLICE17_CHATTERBOX_AUDITION.md) §Slice 17D; matrix **`chatterbox` → PASS**. |
| 2026-04-20 | **Slice 17 Chatterbox:** `ensure_chatterbox` + `checks.chatterbox` (no `ok: null`); probe mirror [`slice17/engine_readiness_probe.json`](slice17/engine_readiness_probe.json); proof [PROOF_SLICE17_CHATTERBOX_AUDITION.md](PROOF_SLICE17_CHATTERBOX_AUDITION.md); matrix **`chatterbox` pending** — first blocker = **chatterbox-tts deps** on proof host (see proof §First blocker). |
| 2026-04-20 | **Slice 17A Chatterbox venv authority:** [VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE17A_CHATTERBOX_VENV_CONTRACT.md); `ensure_chatterbox` subprocess-aligned to **`venv_advanced_tts`**; probe **`2026-04-20T03:56:14.058108+00:00`** — matrix **`chatterbox` still pending** (venv provision + deps + HF on operator host). |
| 2026-04-20 | **Slice 17B:** `runtime/venvs/torch26` + green **`checks.chatterbox`**; probe **`2026-04-20T11:55:08.699504+00:00`** `preflight_assets.ok: true`; synthesis/router still blocking — matrix **`chatterbox` pending** — [PROOF_SLICE17_CHATTERBOX_AUDITION.md](PROOF_SLICE17_CHATTERBOX_AUDITION.md) §Slice 17B. |
| 2026-04-19 | **Slice 14B Mode B attempt:** `engine_configs.rhvoice` placeholder in `backend/config/engine_config.json`; probe [`slice14/engine_readiness_probe.json`](slice14/engine_readiness_probe.json) `timestamp_utc` **2026-04-19T20:13:05Z** — `rhvoice.preflight_assets.ok` **false**; no operator CLI — `real_rhvoice` **2 skipped**, C# RhVoice **3 skipped**; matrix **`rhvoice` pending** — [PROOF_SLICE14_RHVOICE_AUDITION.md](PROOF_SLICE14_RHVOICE_AUDITION.md) §Slice 14B. |
| 2026-04-18 | **Slice 16 RHVoice support contract:** [VOICESTUDIO_BOUNDED_SLICE16_RHVOICE_SUPPORT_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE16_RHVOICE_SUPPORT_CONTRACT.md); `executable_path` → `RHVoiceEngine`; preflight remediation text (Mode D + explicit `engine_config.json` path); matrix **`rhvoice` row still pending** (no fake PASS). |
| 2026-04-19 | **Slice 14 RHVoice Path 1 (operator session):** Full probe refresh [`slice14/engine_readiness_probe.json`](slice14/engine_readiness_probe.json) `timestamp_utc` **2026-04-19T19:13:20Z** — `rhvoice` `preflight_assets.ok` still **false** (CLI not on PATH / no `executable_path`); `real_rhvoice` + C# RhVoice filters **skipped**; matrix **`rhvoice` row unchanged (pending)** — [PROOF_SLICE14_RHVOICE_AUDITION.md](PROOF_SLICE14_RHVOICE_AUDITION.md). |
| 2026-04-19 | **Slice 15 Silero runtime PASS:** `checks.silero.ok`; probe `slice15/engine_readiness_probe.json` **`timestamp_utc` `2026-04-19T15:15:53.911195+00:00`** — `preflight_assets.ok: true`; `pytest -m real_silero` **2/2**; C# `--filter FullyQualifiedName~Silero` **3/3**; artifacts `slice15/silero/*.wav`; matrix **`silero` → PASS**; proof [PROOF_SLICE15_SILERO_AUDITION.md](PROOF_SLICE15_SILERO_AUDITION.md). |
| 2026-04-19 | **Slice 15 Silero harness:** `ensure_silero` + `checks.silero` + `slice15/engine_readiness_probe.json` (`timestamp_utc` **2026-04-19T04:15:17Z**); `real_silero` pytest + C# live tests landed; proof [PROOF_SLICE15_SILERO_AUDITION.md](PROOF_SLICE15_SILERO_AUDITION.md). Earlier **`preflight_assets.ok: false`** (hub cache miss) — superseded after hub warm. |
| 2026-04-18 | **Path 2 pivot (post–Slice 14 harness):** RHVoice not on PATH — Slice 14 runtime **not** closed; governance [PROOF_SLICE15_PIVOT_AND_NEXT_ENGINE.md](PROOF_SLICE15_PIVOT_AND_NEXT_ENGINE.md); full probe refresh `timestamp_utc` **2026-04-18T23:37:14Z** in `slice14/engine_readiness_probe.json`; **Slice 15** bounded anchor **`silero`** (matrix row **pending**; no PASS claims). |
| 2026-04-18 | **Slice 14 harness:** `real_rhvoice` integration test; C# `RealSynthesisRhVoice` + `RhVoicePlaybackAudition`; `pytest.ini` marker; `slice14/engine_readiness_probe.json` mirror; proof [PROOF_SLICE14_RHVOICE_AUDITION.md](PROOF_SLICE14_RHVOICE_AUDITION.md). Matrix row **pending runtime PASS** until operator installs RHVoice and records PASS lines (honest — no fake WAV). |
| 2026-04-18 | Slice 13 **`ensure_rhvoice`** + `checks.rhvoice` on preflight; `slice13/engine_readiness_probe.json`; matrix row **deferred** (Path B — RHVoice binary not on PATH); proof [PROOF_SLICE13_RHVOICE_AUDITION.md](PROOF_SLICE13_RHVOICE_AUDITION.md); Slice 14 handoff for real synth proof. |
| 2026-04-18 | Slice 12 **espeak_ng** row PASS + proof doc; `slice12/engine_readiness_probe.json`; bark candidate blocked note (HF 404); router unload vs `list_engines` fix documented. |
| 2026-04-17 | Initial matrix + Slice 10 Piper proof row; `routed_engine` contract; removal of invalid-engine fallback chain documented. |
