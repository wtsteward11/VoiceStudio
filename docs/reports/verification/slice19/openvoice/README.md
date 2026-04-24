# Slice 19A — OpenVoice runtime parity (artifacts)

Operator session: start **one** Uvicorn on a dedicated port (**8031** narrative default, or another free port — document the same URL everywhere).

**Slice 19B note (2026-04-21):** A long-lived listener on **8031** may be **stale** (preflight showed `checks.openvoice.ok: null` with legacy `no public ensure_*` text while current `health.py` uses `ensure_openvoice()`). For proofs, either **restart** that process on current code or use a **fresh** port (session used **8032**) and record the URL in [`slice19b_proof_session.md`](slice19b_proof_session.md).

**Slice 19C — provisioning (2026-04-21, historical `torch26` attempt):** Bounded lane documented install into **`runtime/venvs/torch26`** — **superseded for OpenVoice authority** by **Slice 19F** (`venv_openvoice`). Historical steps: [VOICESTUDIO_BOUNDED_SLICE19C_OPENVOICE_PROVISIONING.md](../../../design/VOICESTUDIO_BOUNDED_SLICE19C_OPENVOICE_PROVISIONING.md).

**Slice 19D — packaging + runtime surface (2026-04-20):** Authoritative provenance + upstream **`install_requires`** (mandatory **`faster-whisper`/`av`** chain) — [`slice19d_openvoice_package_provenance.md`](slice19d_openvoice_package_provenance.md); **[ADR-053](../../../../architecture/decisions/ADR-053-openvoice-advanced-tts-packaging-surface.md)** (amended by **ADR-054** for OpenVoice interpreter); proof §19D [PROOF_SLICE19_OPENVOICE_AUDITION.md](../../PROOF_SLICE19_OPENVOICE_AUDITION.md).

**Slice 19E — dependency unblocker (2026-04-21):** Full **`pip --dry-run`** graph + **`se_extractor` → `faster_whisper`** import-time proof; strategy **C**; **[ADR-054](../../../../architecture/decisions/ADR-054-openvoice-isolated-venv-proposal.md)** — **Accepted** in **19F** — [`slice19e_openvoice_dependency_graph.md`](slice19e_openvoice_dependency_graph.md); proof §19E [PROOF_SLICE19_OPENVOICE_AUDITION.md](../../PROOF_SLICE19_OPENVOICE_AUDITION.md).

**Slice 19F — isolated venv + subprocess (2026-04-21):** Operator provisions **`runtime/venvs/openvoice`** per [VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md](../../../design/VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md). Checkpoint trees: **`<VOICESTUDIO_MODELS_PATH>/openvoice/base_speakers`** and **`.../openvoice/converter`**. Preflight gate: **`checks.openvoice.ok: true`**. Artifact (expected red until provisioned): [`slice19f_preflight_openvoice.json`](slice19f_preflight_openvoice.json).

**Slice 19H — venv provisioning unblocker (2026-04-22):** Bounded lane [VOICESTUDIO_BOUNDED_SLICE19H_OPENVOICE_VENV_PROVISIONING.md](../../../design/VOICESTUDIO_BOUNDED_SLICE19H_OPENVOICE_VENV_PROVISIONING.md); session [`slice19h_proof_session.md`](slice19h_proof_session.md); preflight [`slice19h_preflight_openvoice.json`](slice19h_preflight_openvoice.json). Goal: **importable `venv_openvoice`** + checkpoints + green preflight, then same live ladder as 19G.

**Slice 19J — authentic-weights live proof (2026-04-22, closed Outcome B):** [VOICESTUDIO_BOUNDED_SLICE19J_OPENVOICE_AUTHENTIC_WEIGHTS_LIVE_PROOF.md](../../../design/VOICESTUDIO_BOUNDED_SLICE19J_OPENVOICE_AUTHENTIC_WEIGHTS_LIVE_PROOF.md); [`slice19j_proof_session.md`](slice19j_proof_session.md); [`slice19j_preflight_openvoice.json`](slice19j_preflight_openvoice.json) — backend **`http://127.0.0.1:8041`**; **2/2 + 3/3** red (placeholder **2 B** `checkpoint.pth` on host); matrix **`openvoice` pending**; proof [PROOF §19J](../../PROOF_SLICE19_OPENVOICE_AUDITION.md).

**Slice 19G — isolated runtime proof (2026-04-22, closed Outcome B):** Bounded lane [VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md](../../../design/VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md); session [`slice19g_proof_session.md`](slice19g_proof_session.md); verbatim preflight [`slice19g_preflight_openvoice.json`](slice19g_preflight_openvoice.json) (**`checks.openvoice.ok: false`**, dedicated backend **`http://127.0.0.1:8036`**). **Also:** `GET /api/health/preflight` + `engine_readiness_probe` now use **`backend.services.model_preflight.ensure_openvoice`** (ADR-054), not stale **`ml`/`torch26`**. Matrix **`openvoice` pending**.

**Slice 19L — reference-audio + VAD contract (2026-04-20, Policy A):** [VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md](../../../design/VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md) — **OpenVoice** live proofs use **`tests/fixtures/audio/openvoice_reference_speech.wav`** (not **`test_440hz_2s.wav`** for **`vad=True`**). Optional **`VOICESTUDIO_OPENVOICE_PROOF_REFERENCE_WAV`**. Session [`slice19l_proof_session.md`](slice19l_proof_session.md), preflight [`slice19l_preflight_openvoice.json`](slice19l_preflight_openvoice.json); proof [PROOF §19L](../../PROOF_SLICE19_OPENVOICE_AUDITION.md).

```powershell
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:8031"
# Same shell for:
#   python -m pytest tests/integration/test_synthesis_openvoice_real.py -m real_openvoice -v --tb=short
#   dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~OpenVoice&TestCategory=LiveBackend"
```

**Readiness gate (before any `real_openvoice` or C# LiveBackend run):**

1. `runtime/venvs/openvoice` (path resolved for **`VenvFamily.OPENVOICE` / `venv_openvoice`**) exists.
2. `GET {base}/api/health/preflight` → `checks.openvoice.ok: true`.
3. Checkpoint trees under models root: `openvoice/base_speakers` and `openvoice/converter` per `ensure_openvoice`.

Successful runs write `openvoice_output.wav` and `openvoice_backend_log_snippet.txt` here (pytest), plus `openvoice_csharp_stream.wav` from C# stream proof when applicable.
