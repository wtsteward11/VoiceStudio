# Runtime truth lane — operator workflow (Tasks 91–94)

**Date:** 2026-04-26  
**Status:** First operator pass **recorded** (2026-04-26) — **§ Verdict** = **partial**; **Task 94** = **R1**  
**Companion control plane:** [.cursor/STATE.md](../../../.cursor/STATE.md) **ACTIVE WINDOW**  
**Disambiguation:** This is **product / host / human path** proof, **not** the same bar as a GAP-008 MainWindow slice (seam + spine + MSTest).

## Authority (do not contradict)

- **Current GAP-008 MainWindow spine:** **N=212** after [Slice 27](../../design/VOICESTUDIO_BOUNDED_GAP008_SLICE27_MAINWINDOW_PANEL_REGION_FOCUS_SHELL.md) — [GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md](GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md) § *Spine size after Slice 27*.
- **Slice 28 (Task 90 + GAP-008):** Runtime-truth **product gate** is **satisfied** (**§ Verdict** + **Task 94 = R1**). **Slice 28+** `MainWindow*ShellBridge` code remains **charter-gated** — **Accepted** `VOICESTUDIO_BOUNDED_GAP008_SLICE28_*.md` **before** extraction (same as pre-freeze GAP-008 law; the prior **pause** is released **only** in the sense that **R2**-style indefinite deferral is **not** required).

## Task 93 — No fixes on the first pass

The **first** execution of the checklist below was **measurement only** — **no** application or test code was changed to improve outcomes.

- Blockers are recorded under **§ Blocker inventory** with classification (**product** / **environment** / **proof-only**).
- Follow-up **fixes** (if any) are **out of scope** for this report commit; own as separate change sets if needed.

## Recommended engine (Task 96 — not RHVoice on Path B)

1. **Full router probe** (session 2026-04-26): `VOICESTUDIO_ENGINE_PROBE_FULL=1` `python scripts/engine_readiness_probe.py` — output: [slice12/engine_readiness_probe.json](slice12/engine_readiness_probe.json) (includes `router.engines.piper` / `rhvoice` preflight).
2. **`piper`:** `preflight_assets.ok: true` — `en_US-amy-medium` under `E:\VoiceStudio\models\piper\`.
3. **`rhvoice`:** `preflight_assets.ok: false` — CLI not on PATH (Path B) — **not** used as proof engine.

## Operator checklist (single bounded workflow)

Execute in order; check each box in your session.

1. **Cold start:** **Not executed** in this session (automation/headless). **Proof-only** gap: full product proof still needs a human **WinUI** cold launch.
2. **Backend ready:** `GET http://127.0.0.1:8000/api/health` **200** — `engines_ready: true` (body captured 2026-04-26 session; backend already running on host). **PASS** (HTTP).
3. **Profile / path:** `pytest` **`real_piper`** creates profile + consent path via HTTP (`tests/integration/test_synthesis_piper_real.py`) — **Piper**-consistent. **PASS** (HTTP).
4. **Synthesize:** `POST /api/voice/synthesize` exercised by **`test_real_piper_synthesize_returns_audible_wav`** and **`test_real_piper_primary_audio_file_route_content_type`**. **PASS** (HTTP).
5. **Output / playback:** `GET /api/audio/file/...` returned **WAV** body; in-app **playback** not tested (headless). External decode: test asserts RIFF, duration, non-silent PCM. **PASS** (HTTP; UI playback N/A).
6. **Artifact check:** [slice10/piper/piper_output.wav](slice10/piper/piper_output.wav) (from test) — **&gt; 1 KiB**, RIFF header, audible energy per test assertions. **PASS**.

## Host and environment (Task 92)

| Field | Value |
|--------|--------|
| OS / build | Windows **10.0.26200** (pytest metadata) |
| .NET / Windows App SDK | **dotnet 8.0.420** (host); WinUI not exercised this session |
| Python / `.venv` | **3.11.9** — `E:\VoiceStudio\.venv\Scripts\python.exe` |
| `VOICESTUDIO_*` env vars used | *None set for this pass* (default live backend `http://127.0.0.1:8000` in tests) |
| Backend URL / port | `http://127.0.0.1:8000` (live) |
| git **workspace** (`HEAD` / `origin/main`) | **`756f3d712cfeeff53619abfb391896218725feb1`** (post `git fetch`; matches `origin/main` at run time) |
| Running process **`/api/health` `git_commit`** | **`1984ca0f`** — **older** than workspace `HEAD` (**environment** note: restart backend to align binary with current tree if strict reproducibility needed) |
| Engine id used | **`piper`** (routed) |
| `engine_readiness_probe` | [slice12/engine_readiness_probe.json](slice12/engine_readiness_probe.json) — `router.engines.piper.preflight_assets.ok: true` |

## Commands and UI steps (Task 92)

1. `git fetch origin`
2. `git rev-parse HEAD` → `git rev-parse origin/main` (both `756f3d712cfeeff53619abfb391896218725feb1` this session)
3. `python scripts/engine_readiness_probe.py` (fast manifest scan) then `VOICESTUDIO_ENGINE_PROBE_FULL=1 python scripts/engine_readiness_probe.py` (full router; slow)
4. `Invoke-WebRequest http://127.0.0.1:8000/api/health` (200, JSON)
5. `python -m pytest tests/integration/test_synthesis_piper_real.py -m real_piper -v --tb=short` — **2 passed** (~30.5s)

## Evidence paths (Task 92)

| Artifact | Path |
|----------|------|
| Output WAV | [slice10/piper/piper_output.wav](slice10/piper/piper_output.wav) (last write from `test_real_piper_synthesize_returns_audible_wav`) |
| Log / snippet | [slice10/piper/piper_backend_log_snippet.txt](slice10/piper/piper_backend_log_snippet.txt) (test helper) |
| Screenshot (optional) | *N/A* (headless) |

## Verdict (after first pass only)

**Task 93 respected** — no code or config changes were made to force pass.

| Field | Value |
|--------|--------|
| **Result** | **partial** |
| **One-paragraph summary** | The **control-plane + HTTP data plane** path is **viable** on this host: live backend on **:8000** with **`engines_ready: true`**, **Piper** preflight **ok** (not **RHVoice**), and **`real_piper`** integration tests **PASS** with real **WAV** bytes &gt; 1 KiB and non-silent PCM. **`rhvoice`** preflight **fails** Path B as expected. **Gap:** no **WinUI** cold start or in-app playback in this session — so this is **not** a full “operator loves the app” proof; a human pass can close that gap without contradicting the API result. The **running** backend’s reported **`git_commit` (1984ca0f)** lags **workspace `HEAD` (756f3d71)** — restart for strict commit parity if required. |

## Blocker inventory (Task 92–93)

| # | Symptom | Class | Notes |
|---|---------|--------|--------|
| 1 | WinUI cold launch + in-ui playback not run | **proof-only** | Headless session; API path substituted per checklist “discover in repo” |
| 2 | Health JSON `git_commit` ≠ workspace `HEAD` | **environment** | Backend process not restarted from current tree; not a synthesis logic failure |

## Task 94 — Path decision (after evidence)

| Path | Meaning | When to choose |
|------|--------|----------------|
| **R1** | Resume **GAP-008** toward **Slice 28** (charter + code) | Workflow **viable** or **viably partial** with documented deps; MainWindow still priority |
| **R2** | **Repoint** lane to higher-leverage product/runtime work | First pass shows blockers &gt; shell slice leverage |

| Selected | **R1** |
|----------|--------|
| **Date** | 2026-04-26 |
| **Rationale (3–5 sentences, cite this report)** | **Piper** synthesis via live HTTP is **proven** (**§ Verdict partial** = API green, UI follow-up optional). **RHVoice** correctly excluded (Path B). No evidence that product priority should **leave** MainWindow bounded decomposition: leverage remains **charter + land Slice 28+**, not repointing to a different runtime-only epic. Reopen GAP-008 **Slice 28** path with **Accepted** `VOICESTUDIO_BOUNDED_GAP008_SLICE28_*.md` before code. |

## Tasks 95–96 — Compliance (this change set)

- **Task 95:** No edits to **closed** CI verify-harness / **GOV closure** rows without hosted `workflow_dispatch` + `run_full_chain: true`.
- **Task 96:** No `engines/audio/rhvoice/` edits; default proof engine was **Piper**, not **RHVoice**.

## Related references

- [PHASE1_GOLDEN_PATH_STATUS.md](PHASE1_GOLDEN_PATH_STATUS.md), [GOLDEN_PATH_PROOF_STATUS.md](GOLDEN_PATH_PROOF_STATUS.md) (historical golden path context)
- [AGENTS.md](../../../AGENTS.md) — build / test commands
- Live-backend patterns: `pytest` `-m` `real_piper` (this pass)

## Changelog

- **2026-04-26 (pm):** First pass recorded: **git** 756f3d71; **full** `engine_readiness_probe` JSON; **health** + **`real_piper`** **2/2**; **§ Verdict** **partial**; **Task 94** **R1**; [STATE.md](../../../.cursor/STATE.md) / tracker sync — **R1** clears **indefinite** Task-90 **pause**; **charter** still required for **Slice 28** code.
- **2026-04-26 (am):** Initial report — Tasks **89–96** plan; empty evidence; **Verdict** and **Task 94** = **PENDING**.
