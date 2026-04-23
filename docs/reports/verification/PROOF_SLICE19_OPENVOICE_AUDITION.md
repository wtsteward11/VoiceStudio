# PROOF — Bounded Slice 19 (OpenVoice readiness truth)

**Date:** 2026-04-21 (updated 2026-04-22 — **§19M** matrix **PASS**)  
**Scope:** **Slice 19** readiness (`ensure_openvoice` + boolean `checks.openvoice`) **and** **Slice 19F** isolated-venv + subprocess **implementation** (ADR-054). **Slices 19G–19J** record **runtime / provisioning / weights** attempts (preflight wiring, **`pip`/`av`**, **ADR-055** Strategy B, **authentic-weight** ladder). **§19M (2026-04-22)** closed **matrix `openvoice` → PASS** with bounded live proof: **`pytest -m real_openvoice` 2/2** + C# OpenVoice `LiveBackend` **3/3** on a dedicated base URL, plus **TTS+SE+stdio** worker seam fixes; authoritative table: **§19M** below.

## Outcome A / B ruling

| Outcome | Condition |
| --- | --- |
| **A (green readiness)** | On a host with **`venv_openvoice`** (`runtime/venvs/openvoice`) containing OpenVoice imports **and** valid checkpoint trees under `<models>/openvoice/base_speakers` and `.../converter`, `checks.openvoice.ok == true`. |
| **B (first blocker)** | Otherwise — typical ordered blockers: (1) `venv_openvoice_not_created`, (2) import failure in that venv, (3) missing / incomplete checkpoint trees (`424`-class `PreflightError` message lists paths). |

**Operator / CI note:** Clean CI images without **`runtime/venvs/openvoice`** or without local OpenVoice weights will land on **Outcome B** until provisioned — this is **honest readiness**, not a regression of the harness. **Historical slices 19B–19E** referenced **`torch26`** / `venv_advanced_tts`; **Slice 19F** (ADR-054) moved the OpenVoice interpreter off shared **`torch26`**.

## Evidence (automated)

- **Unit:** `tests/unit/backend/services/test_model_preflight.py` — `ensure_openvoice` venv-missing, import-fail, and success-with-layout cases.  
- **Implementation:** `ensure_openvoice` + `checks["openvoice"]` wiring in `backend/api/routes/health.py`; mirror in `backend/ml/models/model_preflight.py` and `backend/services/model_preflight.py`; `scripts/engine_readiness_probe.py` `openvoice` branch.  
- **Regression bar (latest OpenVoice matrix closure — §19M):** `python scripts/run_verification.py` **PASS**; `.\scripts\verify.ps1 -Quick` **VERIFICATION PASSED** — [`artifacts/verify/20260422_183808/verification_report.md`](../../../artifacts/verify/20260422_183808/verification_report.md). *(Prior: [`20260421_203242`](../../../artifacts/verify/20260421_203242/verification_report.md) — 19H; [`20260421_195838`](../../../artifacts/verify/20260421_195838/verification_report.md) — 19G; [`20260421_191826`](../../../artifacts/verify/20260421_191826/verification_report.md) — 19F.)*

## §19A — Bounded runtime parity (harness + operator closure)

**Dedicated backend URL (narrative default):** `http://127.0.0.1:8031` — single Uvicorn; **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** must be **identical** for `pytest -m real_openvoice` and `dotnet test` OpenVoice `LiveBackend` filters (same discipline as Slice 17D / 18D).

### Readiness gate (mandatory before runtime proofs)

| Step | Check |
| --- | --- |
| 1 | Family venv on disk for **`VenvFamily.OPENVOICE`** (`runtime/venvs/openvoice`) matches repo contract. |
| 2 | Subprocess import in that venv succeeds for OpenVoice (`openvoice.api` per `ensure_openvoice`). |
| 3 | Local trees `<models>/openvoice/base_speakers` and `.../converter` satisfy checkpoint rules in `ensure_openvoice`. |
| 4 | `GET {base}/api/health/preflight` → **`checks.openvoice.ok: true`** (boolean). |

Log **timestamp (UTC)**, **canonical base URL**, and **port** in `slice19/openvoice/openvoice_backend_log_snippet.txt` (pytest) or session notes when closing Branch A.

### Harness (landed in repo)

- **Python:** marker **`real_openvoice`**; `tests/integration/test_synthesis_openvoice_real.py` — **2** tests (audible WAV + file route / Content-Type); client timeout **900s**; skips when backend unreachable or preflight not green.
- **C#:** `LivePreflightGuards.AssertOpenVoicePreflightOkAsync`; `RealSynthesisOpenVoiceLiveBackendTests` + `OpenVoicePlaybackAuditionLiveBackendTests` (**3** `LiveBackend` tests); same `VOICESTUDIO_REAL_XTTS_HTTP_BASE`.

### Branch A — Runtime PASS (operator)

- `pytest -m real_openvoice` **2/2 PASS** on dedicated URL.
- `dotnet test` filter **`FullyQualifiedName~OpenVoice&TestCategory=LiveBackend`** **3/3 PASS** (playback test may be **Inconclusive** on headless — document; stream test must still **PASS** when preflight green).
- Artifacts under [`slice19/openvoice/`](slice19/openvoice/) + this §19A table filled with commands and paths.
- Flip **`openvoice`** row in [`ENGINE_PARITY_MATRIX.md`](ENGINE_PARITY_MATRIX.md) to **PASS** only with the above.

### Branch B — Runtime not closed (this merge / CI agent)

- **Do not** flip matrix to **PASS** without live green proofs.
- Record **one** primary seam (HTTP status, JSON `code`, engine init, checkpoint layout, router registration) with verbatim error text.

| Session | Host | `VOICESTUDIO_REAL_XTTS_HTTP_BASE` | Python `real_openvoice` | C# `OpenVoice` `LiveBackend` | Notes |
| --- | --- | --- | --- | --- | --- |
| 2026-04-21 — harness merge | CI / agent (no dedicated uvicorn) | default `http://127.0.0.1:8000` (not running) | `pytest … --collect-only` → **2** tests discovered | `dotnet test` filter `FullyQualifiedName~OpenVoice&TestCategory=LiveBackend` → **3 skipped** (~10s probe; preflight/health unreachable) | **Expected:** no matrix PASS; operator runs §19A Branch A on port **8031** when checkpoints + venv green. |

## §19B — Live-proof closure attempt (2026-04-21)

**Branch B** — Runtime parity **not** closed. **Do not** use matrix PASS.

| Field | Value |
| --- | --- |
| **Canonical URL** | `http://127.0.0.1:8032` — single **fresh** Uvicorn from current repo (`py -3.11 -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8032`, `PYTHONPATH` = repo root). |
| **Stale URL warning** | **`http://127.0.0.1:8031`** had an older listener: `checks.openvoice` still showed **`ok: null`** + legacy `no public ensure_*` text — **not** trustworthy for Slice 19/19A/19B; restart or pick a free port after pulling current `health.py`. |
| **Preflight** | `checks.openvoice.ok == false`; **`ModuleNotFoundError: No module named 'openvoice'`** in `runtime/venvs/torch26` (verbatim JSON: [`slice19b_preflight_openvoice.json`](slice19/openvoice/slice19b_preflight_openvoice.json)). |
| **Python** | `pytest -m real_openvoice` → **2 skipped** (preflight not green). |
| **C#** | `dotnet test` OpenVoice `LiveBackend` filter → **3 skipped** (same preflight gate). |
| **Artifacts** | Session narrative: [`slice19b_proof_session.md`](slice19/openvoice/slice19b_proof_session.md). |

**Next operator action (historical for 19B seam):** Prior text targeted **`torch26`**. **Current (19F):** Provision **`runtime/venvs/openvoice`** via `python scripts/engines/create_engine_venv.py --family openvoice` and `config/venv_families/requirements-openvoice.txt` until subprocess `import openvoice` succeeds in that interpreter; satisfy checkpoint trees under `<models>/openvoice/`; restart Uvicorn; re-run preflight then **Python before C#** on the **same** `VOICESTUDIO_REAL_XTTS_HTTP_BASE` — [VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md](../../design/VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md).

## §19C — `torch26` provisioning attempt (2026-04-21)

**Branch B** — Package install **not** completed; matrix **`openvoice`** still **pending**.

| Field | Value |
| --- | --- |
| **Plan** | [VOICESTUDIO_BOUNDED_SLICE19C_OPENVOICE_PROVISIONING.md](../../design/VOICESTUDIO_BOUNDED_SLICE19C_OPENVOICE_PROVISIONING.md) |
| **Primary seam** | `pip install` pinned **`myshell-openvoice`** fails resolving **`faster-whisper==0.9.0`** → **`av==10.*`** source build — **Cython `CompileError`** in **`av\logging.pyx`** (wheel build / `noexcept` mismatch). Exit **1**. |
| **Secondary seam** | On this host, **`E:\VoiceStudio\models\openvoice`** (and `base_speakers` / `converter`) **do not exist** — checkpoint preflight would fail **424-class** after import even if pip succeeded. |
| **Preflight / live** | **Not re-run** (provisioning blocked first). |
| **Session artifact** | [`slice19c_proof_session.md`](slice19/openvoice/slice19c_proof_session.md) |

## §19D — Packaging provenance + runtime surface (2026-04-20)

**One-sentence upstream vs VoiceStudio path:** The **stock MyShell-OpenVoice** package **mandates** **`faster-whisper==0.9.0`** (and thus **`av==10.*`**) in **`install_requires`** at the pinned Git commit — **not** optional extras — so the failing chain is **required for `pip install`**, even though VoiceStudio’s **`openvoice_engine`** adapter imports only **`openvoice`**, **`se_extractor`**, **`BaseSpeakerTTS`**, and **`ToneColorConverter`**.

**Runtime surface policy (Slice 19D historical):** This section recorded **`ADVANCED_TTS` → `torch26`** as authority — **[ADR-053](../../architecture/decisions/ADR-053-openvoice-advanced-tts-packaging-surface.md)**. **Slice 19F / [ADR-054](../../architecture/decisions/ADR-054-openvoice-isolated-venv-proposal.md) Accepted:** OpenVoice preflight + synthesis use **`VenvFamily.OPENVOICE` → `runtime/venvs/openvoice`** (subprocess engine); shared **`torch26`** is **Chatterbox** surface only for OpenVoice-adjacent narrative in §19B–§19E.

**Provisioning / gates (this session — honest Branch B where blocked):**

| Task | Result |
| --- | --- |
| **Provenance** | [`slice19d_openvoice_package_provenance.md`](slice19/openvoice/slice19d_openvoice_package_provenance.md) — pin, interpreter, pip command, upstream `setup.py` citation, `av` wheel facts. |
| **`pip` / import** | **Not green** — same failure class as §19C; **no** successful install into **`torch26`**; subprocess **`from openvoice.api import …`** still **not** provable as green. |
| **Checkpoints** | **`E:\VoiceStudio\models\openvoice`** trees **still absent** on proof host — not laid down in repo (operator weights). |
| **Fresh Uvicorn + preflight** | **Not re-run** per plan hard gate — prior state remains import/checkpoint red; no duplicate **`real_openvoice`** / C# live run until **`checks.openvoice.ok: true`**. |

## §19E — Dependency-chain unblocker inside `torch26` (2026-04-21)

**One-sentence (pip vs runtime):** **`faster-whisper`/`av` is mandatory for stock `pip install`** (`install_requires` + resolver `av==10.*` → sdist **Cython `CompileError`** on Windows cp311); **`openvoice.api` does not import `faster_whisper`**, but VoiceStudio’s engine imports **`se_extractor`**, and upstream **`openvoice/se_extractor.py`** does **`from faster_whisper import WhisperModel` at module top** — so the **supported synthesis path requires the whisper stack once `se_extractor` loads**, not merely “pip over-pull.”

**Active strategy (Slice 19E Task 3):** **C** — **Branch A** (stock install into **`torch26`**) **reproducibly fails** (`pip install --dry-run` same class as 19C); **Branch B** (metadata-only relax) **insufficient without fork** (see §19E graph); **evidence bundle** → **[ADR-054](../../architecture/decisions/ADR-054-openvoice-isolated-venv-proposal.md)** — **Accepted** in Slice **19F** (implementation); **ADR-053** amended for OpenVoice runtime surface.

| Gate | Result |
| --- | --- |
| **Dependency graph** | [`slice19e_openvoice_dependency_graph.md`](slice19/openvoice/slice19e_openvoice_dependency_graph.md) — full `install_requires`, dry-run edges, `se_extractor` citation. |
| **Import probe (`torch26`)** | **`ModuleNotFoundError: No module named 'openvoice'`** — package not installed. |
| **Checkpoints** | **`E:\VoiceStudio\models\openvoice`** **absent** (host). |
| **Preflight / live** | **Not re-proven green** — same blockers; matrix **`openvoice` pending**. |

## §19F — Isolated `venv_openvoice` + subprocess runtime (2026-04-21)

**Governance:** [ADR-054](../../architecture/decisions/ADR-054-openvoice-isolated-venv-proposal.md) **Accepted**; [ADR-053](../../architecture/decisions/ADR-053-openvoice-advanced-tts-packaging-surface.md) **amended** for OpenVoice interpreter (no longer **`ADVANCED_TTS`/`torch26`** for this engine).

| Item | Result |
| --- | --- |
| **Code** | `VenvFamily.OPENVOICE` → `runtime/venvs/openvoice`; manifest `venv_family: venv_openvoice`; `ensure_openvoice` → `_require_venv_openvoice_python_exe`; `OpenVoiceSubprocessEngine` + `app.cli.openvoice_worker_synthesize`; provisioning **`scripts/engines/create_engine_venv.py --family openvoice`**; [requirements-openvoice.txt](../../config/venv_families/requirements-openvoice.txt). |
| **`self.gpu` fix** | `OpenVoiceSubprocessEngine.__init__` sets **`self.gpu = gpu`** after `EngineProtocol.__init__` so `synthesize()` payload includes `gpu` (constructor fix; unit test `test_openvoice_subprocess_engine`). |
| **`synthesize` return path** | Worker writes **`output_path`** WAV; **`OpenVoiceSubprocessEngine.synthesize`** returns **`None`** on success (same pattern as other file-first subprocess engines). **`SynthesisService.synthesize`** in [`backend/services/synthesis_service.py`](../../backend/services/synthesis_service.py) treats **`result is None`** as success when **`_synth_output_file_ready(output_path)`** — file-driven contract. **Slice 19G** must **prove** this path under real OpenVoice load (logs or debugger); do not assume rewrite without evidence. |
| **Preflight / live** | **Not operator-closed** at 19F — matrix **`openvoice` pending** until `openvoice` venv exists, imports succeed, checkpoints laid, fresh Uvicorn → **`checks.openvoice.ok: true`**, then §19A proof ladder. Gate artifact (expected red until provisioned): [`slice19f_preflight_openvoice.json`](slice19/openvoice/slice19f_preflight_openvoice.json). |

**Slice 19F is not runtime-closed.** **Slice 19G** ([VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md](../../design/VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md)) owns **provisioning + one fresh backend + green preflight + Python/C# live proofs** + explicit **return-`None` / file-ready** verification.

## §19G — Isolated runtime proof attempt (2026-04-22)

**Bounded brief:** [VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md](../../design/VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md).

**Ruling:** **Outcome B** — first seam remains **`myshell-openvoice` → `faster-whisper` → `av`** Windows wheel/sdist failure class (§19C/§19E); isolated **`venv_openvoice`** directory exists but **`openvoice`** package **not** installed → preflight import fail; **`models/openvoice/`** checkpoint trees **absent**. **No** matrix **PASS**.

**Implementation note (same session):** HTTP preflight + `engine_readiness_probe` **incorrectly** called **`ensure_openvoice`** from **`backend.ml.models.model_preflight`** (stale **`torch26`** text). **Corrected** to **`backend.services.model_preflight`** in `backend/api/routes/health.py` and `scripts/engine_readiness_probe.py` so **`checks.openvoice.python_exe`** matches **`runtime/venvs/openvoice`** (ADR-054 truth).

| Gate | Result |
| --- | --- |
| **Venv provision** | **Not green** — see [`slice19g_proof_session.md`](slice19/openvoice/slice19g_proof_session.md); `import openvoice` in **`venv_openvoice`** → **`ModuleNotFoundError`**. |
| **Preflight** | Verbatim JSON: [`slice19g_preflight_openvoice.json`](slice19/openvoice/slice19g_preflight_openvoice.json) — **`checks.openvoice.ok: false`**, **`python_exe`:** `runtime/venvs/openvoice/Scripts/python.exe`. |
| **Python `real_openvoice`** | **2 skipped** (preflight not green); base URL **`http://127.0.0.1:8036`** in session. |
| **C# OpenVoice LiveBackend** | **3 skipped** (same gate + same base URL). |
| **File contract** | **Not exercised** live — no synthesis run; design path unchanged (§19F **`None` + `SynthesisService` file-ready**). |
| **Matrix** | **`openvoice` pending** — no §19A Branch A closure. |

## §19H — Isolated venv provisioning unblocker (2026-04-22)

**Bounded brief:** [VOICESTUDIO_BOUNDED_SLICE19H_OPENVOICE_VENV_PROVISIONING.md](../../design/VOICESTUDIO_BOUNDED_SLICE19H_OPENVOICE_VENV_PROVISIONING.md).

**Ruling:** **Outcome B** under **Strategy A** (stock Git pin) — **`pip install myshell-openvoice@git+…74a1d147…`** fails building **`av==10.*`** (**Cython `CompileError`** in **`av\logging.pyx`** — verbatim [`slice19h_pip_myshell_attempt.log.txt`](slice19/openvoice/slice19h_pip_myshell_attempt.log.txt)). **`import openvoice`** and **`import faster_whisper`** remain **`ModuleNotFoundError`** in **`venv_openvoice`**. Checkpoints not laid (blocked upstream of install). **Strategy B** (fork / patched metadata / ADR-044) is the **mandatory next lane** — **not** implemented in 19H.

| Gate | Result |
| --- | --- |
| **Frozen install path** | Documented in bounded brief + session §1 — `create_engine_venv.py --family openvoice` / staged base + **`myshell-openvoice`** line; manual equivalent `pip install -r config/venv_families/requirements-openvoice.txt`. |
| **Strategy** | **A** attempted; **B** deferred with evidence (single seam: **`av`** sdist). |
| **Import gates** | Preflight probe **`ModuleNotFoundError: openvoice`**; **`faster_whisper`** missing; session [`slice19h_proof_session.md`](slice19/openvoice/slice19h_proof_session.md) §2. |
| **Checkpoints** | **Not laid** — install gate first. |
| **Preflight** | [`slice19h_preflight_openvoice.json`](slice19/openvoice/slice19h_preflight_openvoice.json) — **`checks.openvoice.ok: false`** (port **8038** capture). |
| **Python `real_openvoice`** | **2 skipped** (preflight red); backend **8037** ephemeral. |
| **C# OpenVoice LiveBackend** | **3 skipped** (same). |
| **File contract** | **Static only:** `SynthesisService` + **`_extract_quality_metrics`** wave path when `audio is None` — session §7; **not** live-disproven. |
| **Matrix** | **`openvoice` pending**. |

## §19I — Strategy B (vendored + patched `myshell-openvoice`, ADR-055)

| Field | Evidence |
| --- | --- |
| **Decision** | [ADR-055](../../architecture/decisions/ADR-055-myshell-openvoice-vendored-patches.md) — vendor `runtime/vendor/myshell-openvoice` at **74a1d147**; `setup.py` drops mandatory **`faster-whisper`**; **`se_extractor`** lazy-imports **`WhisperModel`**; optional **`[whisper]`** extra. |
| **Install** | `python scripts/engines/create_engine_venv.py --family openvoice --force` → **exit 0**; **`numpy>=1.24.0,<2.0`** + staged **`-e`** to vendor (see `create_engine_venv.py` / `requirements-openvoice.txt`). |
| **Import gates** | `venv_openvoice`: `from openvoice.api import BaseSpeakerTTS, ToneColorConverter`; `from openvoice import se_extractor`; `PYTHONPATH=repo` → `from app.core.engines.openvoice_engine import OpenVoiceEngine` — **ok** (session commands in [`slice19i_proof_session.md`](slice19/openvoice/slice19i_proof_session.md)). |
| **Checkpoints** | `VOICESTUDIO_MODELS_PATH=e:\VoiceStudio\models` — layout under `openvoice/base_speakers/EN` + `openvoice/converter` with `config.json` + `checkpoint.pth` (**file-layout** for preflight; weights are **not** real OpenVoice binaries in this session). |
| **Preflight** | [`slice19i_preflight_openvoice.json`](slice19/openvoice/slice19i_preflight_openvoice.json) — **`checks.openvoice.ok: true`**; backend **`http://127.0.0.1:8040`**; overall `ok` false (other engines e.g. **rhvoice** / **chatterbox** unchanged). |
| **Python `real_openvoice`** | **2/2 FAIL** — `POST /api/voice/synthesize` **500** — `"Synthesis failed - engine returned None. Check engine logs for details."` (placeholder checkpoint tensors). |
| **C# OpenVoice LiveBackend** | **0/3** — `TestCategory=LiveBackend&FullyQualifiedName~OpenVoice`: **3 failed** (same `BackendServerException` / engine returned None). |
| **File contract (§19A Task 10)** | **Conclusion B:** `result is None` without usable **`_synth_output_file_ready(output_path)`** → `SynthesisService` raises **500** (`synthesis_service.py` ~605–636); not the happy file-ready **`None` + artifact** path. |
| **Matrix** | **No PASS** — live ladder **red**; **`openvoice` row remains pending** until real weights + green **2/2** + **3/3**. |

## §19J — Authentic-weights live proof (2026-04-22)

**Bounded brief:** [VOICESTUDIO_BOUNDED_SLICE19J_OPENVOICE_AUTHENTIC_WEIGHTS_LIVE_PROOF.md](../../design/VOICESTUDIO_BOUNDED_SLICE19J_OPENVOICE_AUTHENTIC_WEIGHTS_LIVE_PROOF.md).

**Ruling:** **Outcome B** — **authentic MyShell OpenVoice weights not present** on the proof host (`e:\VoiceStudio\models\openvoice\...\checkpoint.pth` files **2 bytes** — placeholders from structural layout sessions). **Preflight** **`checks.openvoice.ok: true`**; **synthesis** still **500** / *engine returned None*.

| Gate | Result |
| --- | --- |
| **Preflight** | [`slice19j_preflight_openvoice.json`](slice19/openvoice/slice19j_preflight_openvoice.json); **`checks.openvoice.ok: true`**; backend **`http://127.0.0.1:8041`**; overall `ok` false (other engines). |
| **Python `real_openvoice`** | **2/2 FAIL** — `POST /api/voice/synthesize` **500** (verbatim message in [slice19j_proof_session.md](slice19/openvoice/slice19j_proof_session.md)). |
| **C# OpenVoice `LiveBackend`** | **3/3 FAIL** — same `BackendServerException` / *engine returned None*. |
| **File contract** | **Conclusion B** — not the happy `None` + file-ready path; no valid worker WAV with placeholder checkpoints (same class as **§19I**). |
| **Matrix** | **`openvoice` still pending** — no **2/2 + 3/3** until real weights. |

## §19K — Real-weights closure attempt (2026-04-22)

**Bounded brief:** [VOICESTUDIO_BOUNDED_SLICE19K_OPENVOICE_REAL_WEIGHTS_CLOSURE.md](../../design/VOICESTUDIO_BOUNDED_SLICE19K_OPENVOICE_REAL_WEIGHTS_CLOSURE.md).

**Ruling:** **Outcome B** — **authentic** HuggingFace **`myshell-ai/OpenVoice`** `checkpoint.pth` files placed under `e:\VoiceStudio\models\openvoice\` (**~153 MB** EN + **~125 MB** converter); **`load_ckpt` clean** in **`venv_openvoice`** manual probe; **live ladder still red** — **not** a placeholder-`.pth` class error anymore.

| Gate | Result |
| --- | --- |
| **Preflight** | [`slice19k_preflight_openvoice.json`](slice19/openvoice/slice19k_preflight_openvoice.json); **`checks.openvoice.ok: true`**; backend **`http://127.0.0.1:8042`**. |
| **Python `real_openvoice`** | **2/2 FAIL** — **500** / *engine returned None* (see [slice19k_proof_session.md](slice19/openvoice/slice19k_proof_session.md) — **VAD + non-speech reference** on harness fixture). |
| **C# OpenVoice `LiveBackend`** | **3/3 FAIL** — same exception class. |
| **File contract (A/B)** | **N/A** (no **HTTP 200**); not the **Conclusion A** file-driven **`None`** validation. |
| **Matrix** | **`openvoice` still pending** — see [ENGINE_PARITY_MATRIX.md](ENGINE_PARITY_MATRIX.md) row. |

**Tooling (session):** `scripts/check_empty_catches.py` — skip **`models/`** (operator **torch.hub** cache may contain third-party code). Regression: [`artifacts/verify/20260422_073049/verification_report.md`](../../../artifacts/verify/20260422_073049/verification_report.md).

## §19L — Reference-audio + VAD contract + Policy A (2026-04-22)

**Bounded brief:** [VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md).

**Ruling:** **Outcome B** — **Policy A** adopted (`openvoice_reference_speech.wav` + optional **`VOICESTUDIO_OPENVOICE_PROOF_REFERENCE_WAV`**); **`preprocess-reference` 200** on speech fixture; **live ladder still red** — **not** the **19K** primary seam (**VAD** strips **440 Hz** pure tone). **Synthesis** still **500** / *engine returned None* (worker / forward pass / device class — see session §6).

| Gate | Result |
| --- | --- |
| **Preflight** | [`slice19l_preflight_openvoice.json`](slice19/openvoice/slice19l_preflight_openvoice.json); **`checks.openvoice.ok: true`**; backend **`http://127.0.0.1:8043`**. |
| **Python `real_openvoice`** | **2/2 FAIL** — **500** (request ids in [slice19l_proof_session.md](slice19/openvoice/slice19l_proof_session.md)). |
| **C# OpenVoice `LiveBackend`** | **3/3 FAIL** — same exception class. |
| **File contract (A/B)** | **N/A** (synthesis **not** **200**). |
| **Matrix** | **`openvoice` still pending** — no **2/2 + 3/3** green. |

**Regression (19L session):** [`artifacts/verify/20260422_080202/verification_report.md`](../../../artifacts/verify/20260422_080202/verification_report.md) (`verify.ps1 -Quick` + `run_verification.py` **PASS**; `dotnet build` **0 errors**).

## §19M — OpenVoice worker + synthesis path (2026-04-20)

**Bounded brief:** [VOICESTUDIO_BOUNDED_SLICE19M_OPENVOICE_WORKER_SYNTHESIS_PATH.md](../../design/VOICESTUDIO_BOUNDED_SLICE19M_OPENVOICE_WORKER_SYNTHESIS_PATH.md).

**Ruling:** **Outcome A (matrix)** — **19L** class failure was **Branch A** (no valid WAV) due to **TTS API mismatch** + **unpacked** `se_extractor` return + **Windows** stdio; **2/2 + 3/3** on **8055** after fixes. **Conclusion A** for the **return-`None` + file** contract: `SynthesisService` + `_synth_output_file_ready` matches the subprocess engine’s file-driven `None` success path (HTTP **200** in live ladder).

| Topic | Result |
| --- | --- |
| **Root cause (fixed)** | Vendored `BaseSpeakerTTS.tts(text, output_path, speaker, language, speed)` was called with the pre-myshell signature; myshell `se_extractor.get_se` returns `(se, name)` and must be unpacked before `ToneColorConverter.convert`. |
| **Windows** | `PYTHONIOENCODING=utf-8` in worker `env` + worker CLI stdio reconfigure so sentence-split `print` does not raise **charmap** under capture. |
| **Worker evidence** | [slice19m_worker_capture.md](slice19/openvoice/slice19m_worker_capture.md) — command, exit code, stderr notes, `output_path` size (e.g. **79916** bytes after fix). |
| **Matrix** | **PASS** — `pytest -m real_openvoice` **2/2** + C# OpenVoice `LiveBackend` **3/3** on **`http://127.0.0.1:8055`** (2026-04-22 session; `VOICESTUDIO_REAL_XTTS_HTTP_BASE` + `VOICESTUDIO_MODELS_PATH`). |
| **Regression (19M close-out)** | See **STATE** **Last Verified Commands** (new `verify.ps1` + `run_verification.py` after this session). |

## Explicit non-claims (Slice 19 readiness-only scope)

- Readiness slice **19** alone did **not** claim matrix **PASS** for `openvoice`.  
- Matrix **PASS** requires **§19A Branch A** evidence (Python + C# + artifacts).

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-21 | Initial proof doc for readiness-only slice 19. |
| 2026-04-21 | §19A: dedicated-port narrative (**8031**), readiness gate table, `real_openvoice` + C# LiveBackend harness; Branch B allowed when host not provisioned. |
| 2026-04-21 | §19B: live attempt on **8032** (fresh uvicorn); preflight **`openvoice` import fail** in `torch26`; Python **2 skipped**, C# **3 skipped**; matrix still pending — [`slice19b_proof_session.md`](slice19/openvoice/slice19b_proof_session.md). |
| 2026-04-21 | §19C: bounded plan [VOICESTUDIO_BOUNDED_SLICE19C_OPENVOICE_PROVISIONING.md](../../design/VOICESTUDIO_BOUNDED_SLICE19C_OPENVOICE_PROVISIONING.md); **`pip install` myshell-openvoice** into **`torch26`** **failed** (`av` / PyAV **Cython CompileError**); **`openvoice/`** model trees **absent**; Branch B — [`slice19c_proof_session.md`](slice19/openvoice/slice19c_proof_session.md). |
| 2026-04-20 | §19D: provenance doc + upstream **`install_requires`** proof; **ADR-053** — stay **`torch26`** surface; pip/checkpoint/preflight/live still red; matrix **`openvoice` pending** — [`slice19d_openvoice_package_provenance.md`](slice19/openvoice/slice19d_openvoice_package_provenance.md), [ADR-053](../../architecture/decisions/ADR-053-openvoice-advanced-tts-packaging-surface.md). |
| 2026-04-21 | §19E: full resolver dry-run + **`se_extractor` → `faster_whisper`** import-time proof; strategy **C**; **ADR-054 Proposed**; import/checkpoints/live still red; matrix **`openvoice` pending** — [`slice19e_openvoice_dependency_graph.md`](slice19/openvoice/slice19e_openvoice_dependency_graph.md), [ADR-054](../../architecture/decisions/ADR-054-openvoice-isolated-venv-proposal.md). |
| 2026-04-21 | §19F: **ADR-054 Accepted** + isolated venv + subprocess engine/worker + preflight wiring; matrix **`openvoice` pending** until operator provision + green preflight + live proofs — [VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md](../../design/VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md). |
| 2026-04-22 | Truth sync: PROOF header + regression artifact **`191826`**; §19F **`self.gpu`** + **`return None` / `SynthesisService` file-ready** note; **§19G** lane + bounded brief [VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md](../../design/VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md); session [`slice19g_proof_session.md`](slice19/openvoice/slice19g_proof_session.md). |
| 2026-04-22 | **§19G Outcome B:** `venv_openvoice` import red + checkpoints absent; **`health`/`engine_readiness_probe`** OpenVoice preflight wired to **`backend.services.model_preflight`** (fix stale **`ml`/`torch26`**); `real_openvoice` **2 skipped**, C# **3 skipped**; preflight JSON **8036** session; regression verify artifact **`20260421_195838`**. |
| 2026-04-22 | **§19H Outcome B (Strategy A):** verbatim **`av`** / **`av\logging.pyx`** **`Cython.CompileError`** on **`pip install myshell-openvoice`** — [`slice19h_pip_myshell_attempt.log.txt`](slice19/openvoice/slice19h_pip_myshell_attempt.log.txt); session [`slice19h_proof_session.md`](slice19/openvoice/slice19h_proof_session.md); preflight [`slice19h_preflight_openvoice.json`](slice19/openvoice/slice19h_preflight_openvoice.json); **Strategy B** = next ADR-led slice; regression **`20260421_203242`**. |
| 2026-04-22 | **§19I (Strategy B + ADR-055):** vendored path + import/preflight green; **live 2/2 + 3/3 red** (synthesis **500** — placeholder weights); matrix **`openvoice` still pending**; preflight JSON [`slice19i_preflight_openvoice.json`](slice19/openvoice/slice19i_preflight_openvoice.json); regression `artifacts/verify/20260422_054817/verification_report.md`. |
| 2026-04-22 | **§19J (authentic weights):** [bounded brief](../../design/VOICESTUDIO_BOUNDED_SLICE19J_OPENVOICE_AUTHENTIC_WEIGHTS_LIVE_PROOF.md); preflight **8041** [`slice19j_preflight_openvoice.json`](slice19/openvoice/slice19j_preflight_openvoice.json); **2/2 + 3/3 FAIL** (placeholders) — [slice19j_proof_session.md](slice19/openvoice/slice19j_proof_session.md); **Outcome B**; matrix **pending**; verify artifact — **STATE** **Last Verified Commands**. |
| 2026-04-22 | **§19K (real weights on disk + live ladder):** [bounded brief](../../design/VOICESTUDIO_BOUNDED_SLICE19K_OPENVOICE_REAL_WEIGHTS_CLOSURE.md); preflight **8042** [`slice19k_preflight_openvoice.json`](slice19/openvoice/slice19k_preflight_openvoice.json); **HF `myshell-ai/OpenVoice` v1 checkpoints**; **2/2 + 3/3 FAIL** (VAD / non-speech reference on harness fixture, not 2 B placeholders) — [slice19k_proof_session.md](slice19/openvoice/slice19k_proof_session.md); **Outcome B**; matrix **pending**; `check_empty_catches` **models/** skip; verify [`20260422_073049`](../../../artifacts/verify/20260422_073049/verification_report.md). |
| 2026-04-22 | **§19L (Policy A speech fixture + VAD contract; Outcome B):** [bounded brief](../../design/VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md); [`slice19l_preflight_openvoice.json`](slice19/openvoice/slice19l_preflight_openvoice.json) **8043**; **`openvoice_reference_speech.wav`** harness; **2/2 + 3/3 FAIL** (synthesis **500** — not **19K** 440 Hz seam); [slice19l_proof_session.md](slice19/openvoice/slice19l_proof_session.md); matrix **pending**; verify [`20260422_080202`](../../../artifacts/verify/20260422_080202/verification_report.md). |
| 2026-04-20 | **§19M (worker / synthesis + file contract; matrix PASS):** [bounded brief](../../design/VOICESTUDIO_BOUNDED_SLICE19M_OPENVOICE_WORKER_SYNTHESIS_PATH.md); [slice19m_worker_capture.md](slice19/openvoice/slice19m_worker_capture.md); [slice19m_proof_session.md](slice19/openvoice/slice19m_proof_session.md); **Branch A** (19L) + **Conclusion A** for `None`+file; code fixes: `openvoice_engine.py` TTS+SE+convert, `openvoice_subprocess_engine.py` `PYTHONIOENCODING`, `openvoice_worker_synthesize.py` stdio; **2/2 + 3/3** on **8055**; [ENGINE_PARITY_MATRIX](ENGINE_PARITY_MATRIX.md) **`openvoice` → PASS**. |
