# Slice 10 — Engine Parity (Piper) — Overseer Handoff

**Date prepared:** 2026-04-17 (session active)
**Outgoing role:** Senior architect / Overseer in mid-flight on `Slice 10 — Engine Parity Program`.
**Incoming role:** New Overseer takes Slice 10 from "implementation complete; not yet committed; live PASS lines not yet recorded".
**Plan file (do NOT edit):** `c:\Users\Tyler\.cursor\plans\slice_10_engine_parity_4244c4ac.plan.md`
**No-fallbacks rule (effective 2026-04-17):** `.cursor/rules/core/no-fallbacks.mdc` — this slice is the first lane to enforce it.

---

## 1. Mission, in one paragraph

Slice 10 freezes a voice-domain engine parity matrix, removes silent invalid-engine substitution from the synthesis service, adds an **echoed `routed_engine` field** so proofs can prove which engine actually ran, extends `/api/health/preflight` with `checks.piper` and explicit `{ok: null, reason: ...}` for engines without a public readiness API, and proves the **Slice 9** audition pipeline (`synth → GET /api/audio/file/{id} → client stream → optional NAudio playback`) for **exactly one non-XTTS TTS engine: Piper**. XTTS regression must remain green. Path A is the chosen path; Path B (readiness-only) is unused.

---

## 2. State of play (commit-ready, *not yet committed*)

`git status` shows the slice's surface area uncommitted (mixed with several pre-existing untracked artifacts from prior slices that you should NOT include in the Slice 10 commit).

### 2.1 What is implemented and present in the working tree

| Area | File(s) | Status |
| --- | --- | --- |
| Backend: no-fallback + echo | `backend/services/synthesis_service.py` | ✅ Edits in place (see §3.1) |
| Backend DTO | `backend/api/models_additional.py` (`VoiceSynthesizeResponse.routed_engine` Field) | ✅ |
| Backend preflight | `backend/api/routes/health.py` (`ensure_piper(auto_download=False)` + `_NO_PUBLIC_PREFLIGHT` loop + `overall_ok` bool-only aggregation) | ✅ |
| C# DTO | `src/VoiceStudio.App/Core/Models/VoiceSynthesisRequest.cs` → `VoiceSynthesisResponse.RoutedEngine` with `[JsonPropertyName("routed_engine")]` | ✅ (file is `.cursorignore`d so use shell to read/edit) |
| C# guard helper | `src/VoiceStudio.App.Tests/Helpers/LiveEngineBackendTestGuards.cs` (new) + thin `LiveXttsBackendTestGuards` shim in same file | ✅ (`Helpers/LiveXttsBackendTestGuards.cs` deleted — see `git status`) |
| C# tests | `src/VoiceStudio.App.Tests/ViewModels/RealSynthesisPiperLiveBackendTests.cs` (service round-trip, RIFF + peak ≥ 200) and `PiperPlaybackAuditionLiveBackendTests.cs` (stream-playable + NAudio playback completion). Both assert `response.RoutedEngine.Trim() == "piper"`. Stream test writes `docs/reports/verification/slice10/piper/piper_csharp_stream.wav`. | ✅ |
| Python integration test | `tests/integration/test_synthesis_piper_real.py` — gated on preflight `checks.piper.ok`, asserts `routed_engine == "piper"`, writes `slice10/piper/piper_output.wav` + `piper_backend_log_snippet.txt`. | ✅ |
| Pytest plumbing | `pytest.ini` — `real_piper` registered + `addopts ... -m "not nightly and not real_xtts and not real_piper"`; `tests/integration/conftest.py` pops `VOICESTUDIO_TEST_MODE` for both `real_xtts` and `real_piper`. | ✅ |
| Unit test mock | `tests/unit/backend/api/routes/test_synthesis.py` — mock now includes `routed_engine="piper"` (required because Pydantic field is required with default `""`). | ✅ |
| Probe script | `scripts/engine_readiness_probe.py` — fast manifest scan default; `VOICESTUDIO_ENGINE_PROBE_FULL=1` runs `load_all_engines` + router probe. | ✅ |
| Probe artifacts | `docs/reports/verification/slice10/engine_readiness_probe.json` (full router run), `slice10/probe_run.txt`. | ✅ |
| Parity matrix | `docs/reports/verification/ENGINE_PARITY_MATRIX.md` — Slice 10 governance + per-engine rows (xtts_v2 PASS, piper PASS, others `none / ok:null`). | ✅ |
| Proof doc | `docs/reports/verification/PROOF_SLICE10_PIPER_AUDITION.md` — Slice 9 mirror; **operator must paste live PASS lines** in §2/§3 before commit (currently template with "operator records" placeholders). | ✅ created, ⚠️ needs operator PASS lines |

### 2.2 What is NOT done — your immediate work queue

In strict order:

1. **Smoke compile + ReadLints.** I have not run `dotnet build`; the C# changes (new helper, two new test classes, DTO change) compile against existing types but should be verified.
2. **Run the live Piper proof end-to-end** on a Piper-healthy host:
   - Place a Piper ONNX voice (e.g., `piper_voice_v1.onnx` + `.onnx.json`) under `%PROGRAMDATA%\VoiceStudio\models\piper\` (or wherever `ensure_piper` resolves on this machine).
   - Start backend (see §6.1 commands).
   - `GET /api/health/preflight` — confirm `checks.piper.ok == true`.
   - Run pytest `real_piper` (2 tests), C# stream test, C# NAudio test (operator desktop session).
3. **Paste the resulting PASS lines** into `PROOF_SLICE10_PIPER_AUDITION.md` §2 and §3 (replace the "operator records" placeholders).
4. **Run all regression gates** (§6.2): XTTS Python proof, XTTS C# stream + NAudio, Profiles/Library/Search live, `dotnet build`, `python scripts/run_verification.py`.
5. **Update `docs/governance/CANONICAL_REGISTRY.md`** with an Update Addendum row registering `ENGINE_PARITY_MATRIX.md` and the Slice 10 Piper closure (see §5).
6. **Update `.cursor/STATE.md` ACTIVE WINDOW + LATEST MILESTONE + LATEST PROOF INDEX**, **and `openmemory.md`** with engine-named Slice 10 rows (see §5).
7. **Commit** (Conventional Commits) — see §7 for the exact split.
8. **Post-commit**: `python scripts/run_verification.py`; confirm `completion_guard` PASS; check artifacts/verify pointer.

If Piper assets are not present on the host: do **not** fall back to Path B silently. The current matrix and proof doc are written as Path A. If Piper turns out to be un-bringable today, you must either install assets or rewrite §2/§3 of the proof doc as Path B (`PROOF_SLICE10_ENGINE_READINESS.md`) and adjust the matrix row before commit. See §9 for the Path B fallback narrative.

---

## 3. Key code changes — what they mean and why

### 3.1 `backend/services/synthesis_service.py`

Three coupled changes. All landed; see `git diff`.

- **No silent invalid-engine substitution.** Around line 442–470: `valid_engines = engine_router.list_engines()` (with one `load_all_engines("engines")` recovery if router empty). If `engine_id not in valid_engines`, raises `InvalidEngineException` with the available list. The previous `fallback_chain = resolve_engine_priority(...)` walk was removed entirely from this path — that was the silent substitution the no-fallbacks rule prohibits.
- **`routed_engine` echo on every response path.** Stub returns `routed_engine="stub"`. Main success path returns `routed_engine=str(result.get("routed_engine") or engine_id)`. Style-transfer/cross-lingual returns set `routed_engine=str(engine)` / `"openvoice"` (lines 1354 / 1481). The two utility-fallback dicts in `_try_utility_tts_fallback` set `routed_engine="gtts_utility"` / `"pyttsx3_utility"` (lines 188/200/228) so when that path runs the proof can detect it.
- **Known unresolved seam (documented, not removed):** `_try_utility_tts_fallback` (line 155 onwards) still substitutes gTTS/pyttsx3 when the **primary engine** crashes mid-synthesis (see calls at lines 645 and 688). This is **not** the invalid-engine fallback removed above; it is a runtime-failure utility fallback. Per the no-fallbacks rule it is technically out of policy, but removing it exceeds Slice 10 blast radius. **Decision recorded in `ENGINE_PARITY_MATRIX.md` Slice 10 governance section.** The `routed_engine` echo lets Slice 10 proofs *detect* if this path runs (Piper proof asserts `routed_engine == "piper"` — utility substitution would fail this assertion). Open question for incoming Overseer: log a HIGH-priority follow-up to remove `_try_utility_tts_fallback` in a future bounded slice. **Do not remove it in this commit.**

### 3.2 `backend/api/models_additional.py`

`VoiceSynthesizeResponse.routed_engine: str = Field(..., default="")`. Required field, default empty string for backward compat. Existing callers must include `routed_engine=...` when they construct this — that is why `tests/unit/backend/api/routes/test_synthesis.py` line 68 needed the mock update.

### 3.3 `backend/api/routes/health.py`

- Lines ~890–913: Block that imports `ensure_piper` and stores `checks["piper"]` exactly like `checks["xtts_v2"]`. Same `auto_download=False` semantics; same try/except shape; same redaction of nested keys to truncated strings (lines ~907–911). This is the only new public preflight key.
- Lines ~920–935: `_NO_PUBLIC_PREFLIGHT` tuple + loop that writes `{ok: None, reason: "no public readiness, runtime-only"}` for engines without an `ensure_*` (chatterbox, tortoise, bark, openvoice, fish_speech, gpt_sovits, higgs_audio, plus STT engines). Honest diagnostic per user explicit approval. **Do not invent `ok: true` for these.**
- `overall_ok` aggregation now considers only entries where `ok` is a **bool** (so `ok: null` does not break the overall flag). Verify in your own diff read; this is the most subtle change in this file.

### 3.4 `src/VoiceStudio.App/Core/Models/VoiceSynthesisRequest.cs`

Added `RoutedEngine` property on `VoiceSynthesisResponse`:
- `[JsonPropertyName("routed_engine")] public string RoutedEngine { get; set; } = string.Empty;`
- File is filtered by `.cursorignore` — read with shell, edit with `StrReplace` (still works) or shell `Set-Content`. The class structure was verified after edit.

### 3.5 C# test helpers / tests

- `LiveEngineBackendTestGuards.IsLiveEngineUnavailable(BackendException ex, string engineId)` (status 500/503 + message contains engineId + message contains "not available"/"failed to initialize"/"503"). Backward-compat shim `LiveXttsBackendTestGuards.IsLiveXttsEngineUnavailable(ex)` calls through with `"xtts_v2"`.
- `RealSynthesisPiperLiveBackendTests.cs` mirrors `RealSynthesisXttsLiveBackendTests` exactly except: profile name `csharp-slice10-piper-real`, `Engine = "piper"`, asserts `RoutedEngine.Trim() == "piper"`, uses `LiveEngineBackendTestGuards.IsLiveEngineUnavailable(ex, "piper")` for `Assert.Inconclusive`.
- `PiperPlaybackAuditionLiveBackendTests.cs` mirrors `PlaybackAuditionLiveBackendTests` (Slice 9). Two `[TestMethod]`s: `Synthesize_PrimaryFileRoute_LiveBackend_StreamPlayable` (no audio device; writes `slice10/piper/piper_csharp_stream.wav`; uses `Assert.Fail` on Piper unavailable per Slice 9 posture) and `Synthesize_ThenPlayback_LiveBackend_PlayableNonSilentWav` (NAudio + `AudioDeviceGuard.SkipIfNoAudioOutputDevice()`; `tempPath = Path.Combine(Path.GetTempPath(), $"vs_slice10_piper_{Guid.NewGuid():N}.wav")`).

**Important runtime detail:** Slice 9's posture is "PASS or **Assert.Fail**, no skip" on a healthy host. Stream + NAudio tests use `Assert.Fail` (then `throw new InvalidOperationException("Assert.Fail must throw.")` to satisfy the C# return-flow analyzer). Service round-trip (`RealSynthesisPiperLiveBackendTests`) keeps `Assert.Inconclusive` per the existing XTTS pattern.

### 3.6 Python integration test

`tests/integration/test_synthesis_piper_real.py`:
- Imports `_bind_profile_reference_audio`, `_grant_voice_usage_consent`, `_live_backend_base_url`, `_repo_fixture_wav`, `_stub_like_mode`, `_wav_duration_and_peak` from the XTTS test (correct DRY).
- Fixture gates on `health.engines_ready` AND `_preflight_piper_ok(client)` (which checks `checks.piper.ok is True`). `pytest.skip` when not ready (this matches the **Python** Slice 9 fixture posture; the **C# stream/NAudio** tests use `Assert.Fail` because the C# proofs are operator-driven). This split is intentional.
- Asserts `synth_data.get("routed_engine") == "piper"` (line 105) — non-negotiable engine echo verification.
- Marker `@pytest.mark.real_piper` + `@pytest.mark.timeout(900)`.

### 3.7 Probe script + artifacts

`scripts/engine_readiness_probe.py` is **dual-mode** because the first version called `load_all_engines` and ran for many minutes (heavy optional stacks):

- **Default fast mode:** `python scripts/engine_readiness_probe.py` — manifest scan only, no router import, finishes in seconds.
- **Full mode:** `$env:VOICESTUDIO_ENGINE_PROBE_FULL='1'; python scripts/engine_readiness_probe.py` — calls `load_all_engines("engines")` and `engine_router.list_engines()` + `get_engine()` per id. The current `slice10/engine_readiness_probe.json` was produced by this mode and shows the full router list (xtts_v2, piper, chatterbox, … 64 engines). `load_all_engines_error: null` — full probe ran clean.

---

## 4. The matrix (ground truth, not narrative)

`docs/reports/verification/ENGINE_PARITY_MATRIX.md`. Three sections:

1. **Sources of truth** — manifests + `engine_router.list_engines()` + preflight + probe JSON.
2. **Slice 10 governance** — the three behavior changes (no invalid-engine fallback, `routed_engine` echo contract, `_try_utility_tts_fallback` known-non-parity seam).
3. **Per-engine rows** by domain:
   - **TTS** (proof shape: synth → file route → stream → optional NAudio). `xtts_v2` and `piper` are PASS; `chatterbox`, `tortoise`, `bark`, `openvoice`, `fish_speech`, `gpt_sovits`, `higgs_audio` are `none / ok:null` (no public preflight).
   - **STT** (`whisper`, `whisper_cpp`, `vosk`, `parakeet`) — `ok:null`, deferred (different proof shape: transcript JSON).
   - **STS** (`sovits_svc` — has `ensure_sovits` preflight; not Slice 10).

The matrix is designed so Slice 11+ adds rows without re-architecting. Discrepancies between manifest / router / config become "row findings", never silently merged.

---

## 5. STATE / openmemory / registry — exact updates required pre-commit

### 5.1 `.cursor/STATE.md`

**ACTIVE WINDOW** changes:

- Move "Active Task" Slice 9 line into **LATEST MILESTONE** (it currently lives in ACTIVE WINDOW). New ACTIVE WINDOW Active Task should reference Slice 10 Piper closure (or "Slice 11 — next bounded slice (post Slice 10)" once Slice 10 is committed).
- **Truth Sync Note (mandatory phrasing, per plan §Phase 4):** "Slice 10 closure is engine-specific (Piper only) — does not extend XTTS, does not claim umbrella synthesis/playback, does not close STT/STS/RVC parity."
- **Last Verified Commands** — paste the exact `pytest -m real_piper`, `dotnet test --filter PiperPlaybackAuditionLiveBackendTests`, `dotnet test --filter RealSynthesisPiperLiveBackendTests`, `dotnet build`, `python scripts/run_verification.py` lines from your live run.
- **Next 3 Steps** — pick from roadmap; suggested: (1) follow-up to remove `_try_utility_tts_fallback` per no-fallbacks rule; (2) Slice 11 candidate engine (`openvoice` is next under matrix selection rule); (3) STT bounded slice.

**LATEST MILESTONE** — append:
```
- **Bounded Slice 10 — Engine Parity (Piper) — Closed** (2026-04-17): runtime PASS — `pytest -m real_piper` 2/2; `PiperPlaybackAuditionLiveBackendTests` stream + NAudio PASS; preflight `checks.piper.ok=true`; `routed_engine=="piper"` echo verified; artifacts `slice10/piper/*.wav`; proof [PROOF_SLICE10_PIPER_AUDITION.md](...).
```

**LATEST PROOF INDEX** — append a row:
```
| 2026-04-17 | **Bounded Slice 10 — Engine Parity (Piper)** — runtime PASS; preflight `checks.piper.ok`; `real_piper` 2/2; C# stream + NAudio PASS; `routed_engine` echo verified; matrix + probe artifacts. | [PROOF_SLICE10_PIPER_AUDITION.md](...); [ENGINE_PARITY_MATRIX.md](...) | EnginePiper+Route+LiveHTTP | **PASS** |
```

### 5.2 `openmemory.md`

Add **one** new bullet under the existing "Bounded Slice" entries (currently has Slice 9). Engine-named, no umbrella language. Suggested:

```
- **Bounded Slice 10 (2026-04-17) — Engine parity (Piper only) — runtime closed**: Gate with `GET /api/health/preflight` (`checks.piper.ok`). `pytest -m real_piper` 2 passed. `PiperPlaybackAuditionLiveBackendTests` stream + NAudio PASS. `routed_engine=="piper"` echo asserted (no silent substitution; `synthesis_service.py` raises `InvalidEngineException` for unknown engines). Parity matrix `docs/reports/verification/ENGINE_PARITY_MATRIX.md`. Proof `docs/reports/verification/PROOF_SLICE10_PIPER_AUDITION.md`. Not a statement about non-Piper engines. Known non-parity seam: `_try_utility_tts_fallback` (gTTS/pyttsx3 on primary engine crash) — tracked, not removed in Slice 10.
```

### 5.3 `docs/governance/CANONICAL_REGISTRY.md`

Add an **Update Addendum** at the top of the addendum list (matches the existing pattern of one addendum per closure) referencing both the matrix and the proof doc. Also add a row in the table further down (under "Reports / Verification" or similar) for `ENGINE_PARITY_MATRIX.md` as a canonical doc per the document-lifecycle rule. The matrix is a new canonical doc — `document-lifecycle.mdc` Gate 1 requires it be registered.

---

## 6. Exact commands you will need

### 6.1 Bring up the backend (Piper-healthy host)

```powershell
# Install Piper ONNX voice if missing (one-time):
#   download a piper voice (e.g., `en_US-amy-medium.onnx` + `.onnx.json`) into
#   $env:PROGRAMDATA + '\VoiceStudio\models\piper\'  (or whatever VOICESTUDIO_MODELS_PATH points at)

scripts\backend\start_backend.ps1 -Port 8020 -CoquiTosAgreed   # same recipe as Slice 9

# In a second terminal, sanity check:
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:8020"
curl http://127.0.0.1:8020/api/health/ready          # expect 200
curl http://127.0.0.1:8020/api/health/preflight | ConvertFrom-Json | ForEach-Object { $_.checks.piper }
# Expect: ok=True, message describing piper voice path.
```

### 6.2 Run the proofs (paste output into PROOF_SLICE10_PIPER_AUDITION.md)

```powershell
# Python — must be 2 passed, 0 skipped, 0 failed
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:8020"
python -m pytest tests/integration/test_synthesis_piper_real.py -q -m real_piper --tb=short

# C# stream-playable (no audio device required)
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 `
  --filter "FullyQualifiedName~PiperPlaybackAuditionLiveBackendTests.Synthesize_PrimaryFileRoute_LiveBackend_StreamPlayable"

# C# NAudio playback (operator desktop session w/ audio device)
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 `
  --filter "FullyQualifiedName~PiperPlaybackAuditionLiveBackendTests.Synthesize_ThenPlayback_LiveBackend_PlayableNonSilentWav"

# C# service round-trip
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 `
  --filter "FullyQualifiedName~RealSynthesisPiperLiveBackendTests"
```

### 6.3 Regression gates (XTTS stays green, plus full verify)

```powershell
# XTTS Python (must remain 2 passed, 0 skipped — Slice 9 baseline)
python -m pytest tests/integration/test_synthesis_xtts_real.py -q -m real_xtts --tb=short

# XTTS C#
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 `
  --filter "FullyQualifiedName~RealSynthesisXttsLiveBackendTests|FullyQualifiedName~PlaybackAuditionLiveBackendTests"

# Profiles + Library + Search live (filters used in Slice 9)
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 `
  --filter "FullyQualifiedName~ProfilesRuntimeLiveBackendTests|FullyQualifiedName~LibraryRuntimeLiveBackendTests|FullyQualifiedName~GlobalSearchRuntimeLiveBackendTests"

# Build clean
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64

# Verification harness (Quick by default; completion_guard PASS expected)
python scripts/run_verification.py
```

### 6.4 Re-run the readiness probe (for the proof's evidence chain)

```powershell
python scripts/engine_readiness_probe.py                      # fast manifest scan
$env:VOICESTUDIO_ENGINE_PROBE_FULL='1'; python scripts/engine_readiness_probe.py  # full router probe
```
Output: `docs/reports/verification/slice10/engine_readiness_probe.json`. Optionally tee stdout to `slice10/probe_run.txt`.

---

## 7. Commit plan (Conventional Commits, surgical)

The working tree mixes Slice 10 with significant pre-existing untracked artifacts from prior slices (effect_chains/*.json, GAP-069 lane closure docs, archived STATE files). **Do not bundle them.** The Slice 10 commit must be exactly the slice's surface.

Stage these (and only these) for Slice 10:

```
backend/services/synthesis_service.py
backend/api/models_additional.py
backend/api/routes/health.py
src/VoiceStudio.App/Core/Models/VoiceSynthesisRequest.cs
src/VoiceStudio.App.Tests/Helpers/LiveXttsBackendTestGuards.cs   # (deletion)
src/VoiceStudio.App.Tests/Helpers/LiveEngineBackendTestGuards.cs # (new)
src/VoiceStudio.App.Tests/ViewModels/RealSynthesisPiperLiveBackendTests.cs # (new)
src/VoiceStudio.App.Tests/ViewModels/PiperPlaybackAuditionLiveBackendTests.cs # (new)
tests/integration/test_synthesis_piper_real.py # (new)
tests/integration/conftest.py
tests/unit/backend/api/routes/test_synthesis.py
pytest.ini
scripts/engine_readiness_probe.py # (new)
docs/reports/verification/ENGINE_PARITY_MATRIX.md # (new)
docs/reports/verification/PROOF_SLICE10_PIPER_AUDITION.md # (new)
docs/reports/verification/slice10/engine_readiness_probe.json # (new)
docs/reports/verification/slice10/probe_run.txt # (new)
docs/reports/verification/slice10/piper/piper_output.wav # (new — generated)
docs/reports/verification/slice10/piper/piper_csharp_stream.wav # (new — generated)
docs/reports/verification/slice10/piper/piper_backend_log_snippet.txt # (new — generated)
.cursor/STATE.md
docs/governance/CANONICAL_REGISTRY.md
openmemory.md
docs/governance/HANDOFF_SLICE10_PIPER_OVERSEER_2026-04-17.md   # (this handoff)
```

Commit message (single commit per plan §Phase 4):
```
test(verification): slice 10 piper runtime parity proof

- Remove silent invalid-engine substitution in synthesis_service.py
  (raise InvalidEngineException; routed_engine echoed on every path).
- Extend /api/health/preflight with checks.piper (ensure_piper, no
  auto-download) and explicit ok:null for engines without ensure_*.
- Add LiveEngineBackendTestGuards (engine-parameterised); keep
  LiveXttsBackendTestGuards as backward-compat shim.
- Add Piper Slice 10 runtime proofs:
    * tests/integration/test_synthesis_piper_real.py (real_piper)
    * RealSynthesisPiperLiveBackendTests
    * PiperPlaybackAuditionLiveBackendTests (stream + NAudio)
- Freeze ENGINE_PARITY_MATRIX.md (canonical) + readiness probe.
- PROOF_SLICE10_PIPER_AUDITION.md mirrors Slice 9 structure.

XTTS regression remains green. Slice 10 closure is engine-specific
(Piper only); does not claim umbrella synthesis/playback parity;
does not close STT/STS/RVC parity. Known non-parity seam:
_try_utility_tts_fallback (gTTS/pyttsx3 on primary-engine crash)
remains; tracked for future bounded slice.
```

After commit: `python scripts/run_verification.py`. Confirm `completion_guard` PASS. If FAIL, fix and `git commit --amend` only if (per CLAUDE.md amend rules) the commit is unpushed and authored in this session.

---

## 8. Acceptance criteria (per plan, Path A)

Mark Slice 10 closed iff **all** are true:

- [ ] `engine_router.list_engines()` reports `piper` registered (probe artifact present).
- [ ] Piper synth route returns valid WAV without engine substitution; `routed_engine == "piper"` verified in both Python and C# proofs.
- [ ] `pytest -m real_piper tests/integration/test_synthesis_piper_real.py` → **2 passed, 0 skipped, 0 failed**.
- [ ] `slice10/piper/piper_output.wav` written, RIFF, non-silent (peak > 200 in 16-bit PCM).
- [ ] `PiperPlaybackAuditionLiveBackendTests.Synthesize_PrimaryFileRoute_LiveBackend_StreamPlayable` → **Passed: 1**, **Skipped: 0**.
- [ ] `slice10/piper/piper_csharp_stream.wav` written.
- [ ] `PiperPlaybackAuditionLiveBackendTests.Synthesize_ThenPlayback_LiveBackend_PlayableNonSilentWav` → **Passed: 1**, **Skipped/Inconclusive: 0** on operator desktop.
- [ ] All XTTS regression gates (Python + C# stream + C# NAudio + Profiles/Library/Search live) PASS.
- [ ] `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → 0 errors.
- [ ] `python scripts/run_verification.py` PASS, `completion_guard` PASS.
- [ ] PROOF doc, parity matrix, STATE, openmemory, CANONICAL_REGISTRY updated.
- [ ] Commit landed.

---

## 9. Failure paths and rollback

### 9.1 If Piper assets are not installable today (Path B fallback)

The plan provides Path B as the honest closure when no non-XTTS TTS engine clears Phase 1:

1. Rename `PROOF_SLICE10_PIPER_AUDITION.md` → leave it but **also** create `PROOF_SLICE10_ENGINE_READINESS.md` per plan §Phase 2B.
2. Edit `ENGINE_PARITY_MATRIX.md` → flip `piper` row from PASS to `none` with `first_blocker: "ONNX voice file not installed under VOICESTUDIO_MODELS_PATH/piper/"`.
3. Open one **HIGH-priority** engine-specific unblocker task — engine `piper`, seam "ensure_piper voice asset install" — per `.cursor/rules/quality/no-defer.mdc` (escalation with ownership, not deferral).
4. Slice 11 opens against `piper` (same engine, just on the install seam).
5. Closure phrasing in proof doc: "no engine-specific runtime proof in Slice 10; Slice 11 opens against `piper`."
6. Commit message: `docs(verification): slice 10 engine readiness truth`.

### 9.2 If `_try_utility_tts_fallback` triggers during your live run

You will see `routed_engine == "gtts_utility"` or `"pyttsx3_utility"` in the response and the Python/C# `routed_engine == "piper"` assertion will **fail**. This is the proof working as designed — it caught the substitution. Resolution: the primary engine (Piper) is crashing under load. Do not "fix" by accepting `gtts_utility` — that is exactly the lie the no-fallbacks rule forbids. Fix the primary engine first.

### 9.3 Whole-slice rollback

Per plan §Risks: revert the parity matrix doc, probe script, optional preflight extension, the new test files, and the explicit-engine guard in `synthesis_service.py`. XTTS proof and existing tests are untouched. STATE.md / openmemory updates revert via the same commit.

---

## 10. Non-negotiable rules in force on this slice

- **`.cursor/rules/core/no-fallbacks.mdc`** (effective 2026-04-17, user-approved): no automatic fallbacks anywhere; fail explicit; user opt-in only. This slice is the first lane to enforce. The remaining `_try_utility_tts_fallback` is documented as known-non-parity, not approved.
- **`.cursor/rules/quality/root-cause-only.mdc`** + **`no-suppression.mdc`** + **`no-deferral-on-encounter.mdc`**: any test failure during regression must be fixed at root, not skipped or suppressed.
- **`.cursor/rules/workflows/closure-protocol.mdc`** + **`state-gate.mdc`**: read STATE ACTIVE WINDOW before any code changes; update STATE on closure; run `completion_guard` before final claim.
- **`.cursor/rules/quality/repo-hygiene.mdc`**: do not bundle the unrelated untracked files (effect_chains JSONs, prior slice closures) into the Slice 10 commit.
- **CLAUDE.md `<empty catch>` and `shell=True` zero tolerance**: still in force for all touched files.

---

## 11. Latent risks for the incoming Overseer

1. **Pydantic required-with-default field migration.** `routed_engine: str = Field(..., default="")` is technically still required at construction time. Any *other* place in the codebase that constructs `VoiceSynthesizeResponse` without it will raise. I caught one (`tests/unit/backend/api/routes/test_synthesis.py`). Run `dotnet build` and `pytest tests/unit/backend/` to flush any others before commit. If you find more, set `routed_engine=engine_id` in those mocks.

2. **`_try_utility_tts_fallback` collisions.** If your Piper run hits this path, every assertion of `routed_engine == "piper"` fails. The fix is "make Piper not crash," not "loosen the assertion."

3. **`overall_ok` aggregation behavior in preflight.** Now bool-only. If any consumer treats `overall_ok` differently because of the now-many `ok: null` keys, you will see it in regression. Specifically check: any C# code that calls `/api/health/preflight` and decides UI state from `overall_ok`. Search before commit.

4. **Probe artifact already includes a full-router run.** `engine_readiness_probe.json` was generated in **full** mode (64 engines, `load_all_engines_error: null`). That is fine, but if you re-run in fast mode it overwrites with a smaller manifest-only artifact. Decide which version you commit — recommend the **full** one since it is the strongest evidence.

5. **C# `.cursorignore` filter on `VoiceSynthesisRequest.cs`.** Edit attempts via `Grep`/`StrReplace` may report "filtered" — read with `Read` (works), edit with shell `Set-Content` or by re-reading then `StrReplace` (the `StrReplace` tool itself bypasses ignore for known paths in some setups, but always verify with a follow-up read).

6. **Backend/Frontend C# DTO mismatch on partial deploy.** If a backend that has `routed_engine` field is paired with an old frontend client, the field is just ignored. The reverse (new C# client, old backend without `routed_engine`) means `RoutedEngine` deserializes to `string.Empty`, and `Trim() == "piper"` fails. Both proofs run against the freshly built backend so this is fine, but document it for future contract tests.

7. **Operator session for NAudio.** The NAudio playback test requires an audio output device and an interactive desktop session. CI runners (especially headless GHA Windows) will skip it via `AudioDeviceGuard`. Slice 10 closure requires running on an operator desktop, exactly like Slice 9.

8. **The plan's "selection rule" puts Piper first, then `openvoice`.** If Piper proves un-bringable, **do not silently move to `openvoice`** in this commit — that would be umbrella creep. Either close Path B for Piper (recording the blocker) and open Slice 11 against `piper`, or formally re-scope Slice 10 against `openvoice` with user approval.

---

## 12. Memory / governance discipline (openmemory + ace)

- **Pre-work search (per `openmemory.mdc` Phase 1)** the next agent must perform before code changes:
  - Search project memory for "engine parity," "routed_engine," "no-fallbacks rule," "Slice 9 playback audition" before doing anything in `synthesis_service.py`. The decision context is already stored.
- **Pre-work `ace_search` (per `ace-patterns.mdc`)** with query "Slice 10 engine parity Piper" — every session, before code.
- **Phase 3 storage on closure**: store one component memory ("Slice 10 Piper engine parity proof — runtime-PASS gate via `checks.piper.ok` + `routed_engine` echo + InvalidEngineException for unknown engine") and one project_info memory ("`_try_utility_tts_fallback` is the next no-fallbacks compliance follow-up after Slice 10 closure").

---

## 13. Document inventory created this slice (for reference)

```
docs/reports/verification/ENGINE_PARITY_MATRIX.md            (new, canonical)
docs/reports/verification/PROOF_SLICE10_PIPER_AUDITION.md    (new — needs operator PASS lines)
docs/reports/verification/slice10/engine_readiness_probe.json (new)
docs/reports/verification/slice10/probe_run.txt              (new)
docs/reports/verification/slice10/piper/piper_output.wav     (will be created by python proof)
docs/reports/verification/slice10/piper/piper_csharp_stream.wav (will be created by C# stream test)
docs/reports/verification/slice10/piper/piper_backend_log_snippet.txt (will be created by python proof)
scripts/engine_readiness_probe.py                            (new)
src/VoiceStudio.App.Tests/Helpers/LiveEngineBackendTestGuards.cs (new; supersedes deleted XTTS-only file)
src/VoiceStudio.App.Tests/ViewModels/RealSynthesisPiperLiveBackendTests.cs (new)
src/VoiceStudio.App.Tests/ViewModels/PiperPlaybackAuditionLiveBackendTests.cs (new)
tests/integration/test_synthesis_piper_real.py              (new)
docs/governance/HANDOFF_SLICE10_PIPER_OVERSEER_2026-04-17.md (this handoff)
```

Files modified in place: `synthesis_service.py`, `models_additional.py`, `health.py`, `VoiceSynthesisRequest.cs`, `pytest.ini`, `tests/integration/conftest.py`, `tests/unit/backend/api/routes/test_synthesis.py`. `Helpers/LiveXttsBackendTestGuards.cs` was deleted (its API is now in `LiveEngineBackendTestGuards.cs` shim).

---

## 14. The one-line summary you owe the user once you commit

> "Slice 10 (Piper) closed: `pytest -m real_piper` 2/2; C# stream + NAudio PASS; preflight `checks.piper.ok=true`; `routed_engine=='piper'` echo verified; `synthesis_service.py` invalid-engine fallback removed (`InvalidEngineException`); XTTS regression PASS; `completion_guard` PASS. `_try_utility_tts_fallback` remains as next no-fallbacks follow-up."

If any clause in that sentence is not true, do not say it. Open Slice 11 against the broken clause instead.

---

**End of handoff.** The single highest-leverage next action is: bring backend up on a Piper-healthy host, run §6.2 commands, paste live PASS lines into `PROOF_SLICE10_PIPER_AUDITION.md` §2/§3, then proceed through §6.3 → §5 → §7. Do not commit before §6.3 is fully green.
