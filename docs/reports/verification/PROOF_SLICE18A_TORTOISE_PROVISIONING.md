# PROOF — Bounded Slice 18A — Tortoise backend-runtime provisioning

**Date:** 2026-04-20  
**Parent contract:** [VOICESTUDIO_BOUNDED_SLICE18_TORTOISE_SUPPORT_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE18_TORTOISE_SUPPORT_CONTRACT.md)  
**Prior slice:** [PROOF_SLICE18_TORTOISE_AUDITION.md](PROOF_SLICE18_TORTOISE_AUDITION.md) (Outcome B — first blocker `ModuleNotFoundError: tortoise`)

## Authoritative runtime surface (frozen)

**Tortoise readiness and `ensure_tortoise` are judged only in the same Python interpreter that runs the FastAPI backend** — the repo-root virtualenv at `E:\VoiceStudio\.venv\Scripts\python.exe` (`sys.executable` when operators use the standard dev layout). Not a Chatterbox-style family venv alone, not a sidecar process, unless an ADR moves Tortoise to a subprocess (see Outcome B).

## Outcome

**Outcome B (tighter second blocker isolated):** Installing `tortoise-tts` into the backend `.venv` is **not** compatible with keeping **Coqui TTS / XTTS** (`coqui-tts`) healthy in the **same** interpreter on this host.

### What was attempted

1. **`pip install "tortoise-tts>=2.4.0"`** into `E:\VoiceStudio\.venv` — resolved **`tortoise-tts` 3.0.0**, which **requires `transformers==4.31.0`** (pip enforced downgrade).
2. **Verification immediately after install:**  
   `python -c "import tortoise; from tortoise.api import TextToSpeech"` → **succeeded** (with `transformers` 4.31.0).
3. **Conflict:** `coqui-tts` **0.27.2** requires **`transformers>=4.52.1,<4.56`** — incompatible with Tortoise’s pin.
4. **Restoration for XTTS:** `pip install "transformers>=4.52.1,<4.56"` (restored **4.55.4** + matching `tokenizers`).
5. **Verification after restoration:**  
   `python -c "from tortoise.api import TextToSpeech"` → **failed** with:

```text
ImportError: cannot import name 'LogitsWarper' from 'transformers'
```

(`LogitsWarper` was removed/relocated in newer `transformers`; Tortoise 3.0.0 targets the 4.31 API surface.)

6. **`pip uninstall -y tortoise-tts`** (and Tortoise-only deps pulled for the experiment: `rotary-embedding-torch`, `unidecode`) to return to a single coherent stack for **coqui-tts** / **transformers 4.55.4**. **`ModuleNotFoundError: tortoise`** is again the honest import state for preflight.

### Second blocker (exact)

**Cannot satisfy both `tortoise-tts` (pip pin `transformers==4.31.0`) and `coqui-tts` / XTTS (`transformers>=4.52.1`) in one backend interpreter without forking/patching Tortoise or isolating Tortoise in a dedicated venv with a subprocess engine path (contract change — compare Chatterbox `venv_advanced_tts`).**

No matrix PASS for `tortoise`; no fake runtime proof.

## Probe artifact (post-session)

- **Path:** [`slice18/engine_readiness_probe.json`](slice18/engine_readiness_probe.json)  
- **timestamp_utc:** `2026-04-20T17:38:45.988793+00:00`  
- **tortoise.preflight_assets:** `ok: false` — still  
  `tortoise-tts not importable (ModuleNotFoundError: No module named 'tortoise')...`  
  after uninstall (consistent with Outcome B narrative: package not retained).

## Branch selection

| Branch | Result |
| --- | --- |
| **A** (preflight + import + cache green) | **Not met** — dependency schism blocks retaining `tortoise-tts` alongside coqui in `.venv`. |
| **B** | **Met** — second blocker documented above; **no** `pytest -m real_tortoise` / C# Tortoise PASS until architecture or upstream resolves the `transformers` conflict. |

## requirements_engines.txt reconciliation

The legacy comment *“Use separate venv - conflicts with Torch 2.9 stack”* is **superseded** by this proof: the **observed** conflict on the proof host is **`transformers` / `tokenizers` API and version pins** between **`tortoise-tts`** and **`coqui-tts`**, not merely a torch minor-version label. See updated comment in [requirements_engines.txt](../../../requirements_engines.txt).

## Regression bar (Slice 18A session)

Recorded 2026-04-20 after documentation + probe refresh:

1. `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → **0 errors**
2. `python scripts/run_verification.py` → **PASS** (`.buildlogs/verification/last_run.json`)
3. `.\scripts\verify.ps1 -Quick` → **VERIFICATION PASSED** [`artifacts/verify/20260420_124814/verification_report.md`](../../../artifacts/verify/20260420_124814/verification_report.md)

## Changelog

| Date | Notes |
| --- | --- |
| 2026-04-20 | Initial proof: Outcome B — second blocker = coqui vs tortoise `transformers` incompatibility in one backend venv; tortoise package not kept installed. |
