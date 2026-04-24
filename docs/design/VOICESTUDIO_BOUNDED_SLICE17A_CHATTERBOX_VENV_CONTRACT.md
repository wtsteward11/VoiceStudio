# Bounded Slice 17A — Chatterbox environment contract (execution row)

**Engine id:** `chatterbox` (manifest [`engines/audio/chatterbox/engine.manifest.json`](../../engines/audio/chatterbox/engine.manifest.json)).

**Authoritative runtime:** Chatterbox is **`venv_advanced_tts`** (`venv_family` in the manifest). The Python executable for that family (via `VenvFamilyManager` / `get_python_executable(VenvFamily.ADVANCED_TTS)`) is the **source of truth** for import checks and Hugging Face cache resolution used by `ensure_chatterbox`.

**Proof host lesson (Slice 17):** Red `checks.chatterbox` was **dependency mismatch / missing transitive imports** (e.g. `conformer`) in environments that did not match the advanced TTS venv surface — **not** a VoiceStudio HTTP or synthesis routing defect.

**Judgment surface:** Readiness is assessed against **`venv_advanced_tts`** subprocess probes (import + `huggingface_hub` with `auto_download=False` policy). A backend started from a different interpreter may not itself import `chatterbox.tts`; preflight still reports truth for the **intended** Chatterbox runtime.

**Pins:** Exact versions are **not** fixed in this doc — record **torch**, **torchaudio**, **numpy**, and **chatterbox-tts** from `pip show` / resolver output **inside `venv_advanced_tts`** after a successful install (do not invent versions here).

**Closure (unchanged from Slice 17):** Matrix **PASS** only with green preflight + non-skipped `real_chatterbox` + C# proofs + artifacts under `docs/reports/verification/slice17/chatterbox/` + regression bar.

**Does not claim:** Other engines; RHVoice; that default CI `.venv` is the Chatterbox runtime; GPU mandatory when CPU is supported.
