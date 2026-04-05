# VoiceStudio Professional-Grade Audit

**Date:** 2026-03-28
**Auditor Role:** Project Intelligence Analyst (Role 8) — Read-only
**Standard:** Multibillion-dollar production software
**Benchmark:** ElevenLabs, Descript, Adobe Audition, Resemble AI, professional DAWs
**Methodology:** Forensic code-level audit of every layer (UI → client → API → service → engine)

---

## Executive Summary

VoiceStudio is architecturally ambitious and governance-rich. The codebase contains **934 C# files**, **153 API routes**, **131 backend services**, **70 engine manifests**, **843 Python test files**, and **295 C# test files**. The governance system (49 rules, 50 ADRs, 9 roles, 73 skills) is enterprise-grade.

**However, there is a critical gap between architectural breadth and functional depth.** The project has the skeleton of a multibillion-dollar product but the connective tissue of a late-stage prototype. Core TTS synthesis works end-to-end. Most other features are wired at the interface level but have gaps in the actual data flow, persistence, or user-facing completeness that would prevent a paying professional user from relying on them.

**Honest verdict:** VoiceStudio is approximately **35-40% product-complete** for professional use. The architecture is 80%+. The governance is 90%+. The inner feature wiring — the part users actually touch — is where the deficit lives.

---

## Section 1: What Works (Verified Connected End-to-End)

These workflows have been forensically traced from UI click through backend to engine and back:

### 1.1 Voice Synthesis (TTS) — CONNECTED
- `VoiceSynthesisViewModel` → `IVoiceSynthesisService` → `BackendClient` → `/api/voice/synthesize` → `EngineRouter` → XTTS/Chatterbox/Bark/Piper engine → WAV file → audio download → `AudioPlayerService` (NAudio) → playback
- WebSocket streaming path also exists (`/api/voice/synthesize/stream`)
- Quality metrics, engine selection, profile-based voice selection all wired
- **Gap:** `Features/Synthesis/SynthesisViewModel.cs` has **stub playback** (`Task.Delay` simulating duration) — duplicate code path that doesn't work

### 1.2 Audio Playback — CONNECTED
- `AudioPlayerService` uses NAudio (`WaveOutEvent`, `AudioFileReader`)
- Registered in DI, injected across multiple ViewModels
- Plays local files, streams, URLs, backend audio IDs

### 1.3 Engine Integration (Core 5) — CONNECTED (environment-dependent)
- XTTS: Real `TTS.api.TTS` → `tts_to_file()` with CUDA support
- Whisper: Real `faster_whisper.WhisperModel` → `model.transcribe()`
- Piper: Real subprocess or `piper_tts` package
- Chatterbox: Real `ChatterboxTTS.from_pretrained()`
- Bark: Real `generate_audio()` from bark package
- **Gap:** All depend on correct Python packages + model weights being installed locally

### 1.4 Recording — CONNECTED
- NAudio microphone capture → backend upload → project audio persistence
- Device selection, duration tracking, error handling all present

### 1.5 Backend Health/Startup — CONNECTED
- `BackendProcessManager` starts Python backend process
- Port detection, reuse vs spawn decision, health checks
- Deterministic `startup_decision.json` artifact

---

## Section 2: What Is Partially Wired (Gaps in the Chain)

### 2.1 Voice Cloning Wizard — PARTIAL
**What works:** Full wizard UI with steps, audio upload (multipart), validation, job creation, polling, finalization, profile creation event
**What's broken:** `create_profile_from_request` in `profile_service.py` creates **metadata only** — it does not bind the uploaded reference audio to the profile. The cloned "profile" has a name and language but **no reference audio path** for the engine to clone from. This is a **critical gap** for the product's headline feature.

### 2.2 Training — PARTIAL (simulation fallback)
**What works:** Dataset CRUD, job creation, progress tracking, WebSocket/polling, quality history
**What's broken:** `run_training` imports `backend.training.facade` → `_execute_real_training`; on `ImportError` it falls back to `_simulate_training`. Training may produce fake results without the user knowing unless they check `simulation_mode`. The VM does expose `IsSimulationMode`/`SimulationReason` but this is not a product-grade UX for "your training didn't actually happen."

### 2.3 Project Save/Load — PARTIAL (fragmented)
**What works:** Backend project CRUD (`/api/projects`), timeline project operations, `JsonProjectRepository` for local storage
**What's broken:** 
- Two parallel persistence systems (API vs local JSON) — no unified "Save Project" that captures everything
- `ProjectWorkflowCoordinator.SaveProjectAsync` only saves **mixer state** — not timeline, not synthesis history, not profiles
- No single project file format that captures full session state

### 2.4 Export — PARTIAL
**What works:** Backend `/api/audio/export` endpoint with format conversion, Library UI file copy for local assets
**What's broken:** Library export uses `File.Copy` for local paths only — server-only assets cannot be exported through the traced code path. No universal "Export As..." flow that handles all asset types.

### 2.5 Timeline — PARTIAL (in-memory global)
**What works:** Track/clip CRUD, synthesis integration, undo/redo, export with ffmpeg
**What's broken:** Backend uses **single global in-memory `_timeline_state`** — code explicitly comments "replace with database/service in production." Not multi-session, not persistent across restarts, not per-project.

### 2.6 Effects Processing — PARTIAL
**What works:** Effect chain CRUD, preset management, mixer state persistence (JSON-backed)
**What's broken:** Live audio meters return **stored state only** — no real-time audio analysis. `/meters/simulate` generates **fake data** for testing. Effects processing path uses `PostFXProcessor` when available but this is import-gated.

### 2.7 Prosody Control — STUB
**What's in the code:** Route exists at `/api/voice/prosody-control`
**What actually happens:** `processed_audio = audio.copy()` — the audio is **unchanged**. Prosody parameters are stored as metadata only. Code comment: "In production, use more sophisticated pitch shifting."

### 2.8 Batch Processing — PARTIAL (logic gap)
**What works:** Job queue, background processing with `asyncio.create_task`, progress tracking
**What's broken:** When engines write to `output_path` and return `None` (common pattern), batch marks the job as **failed** even though the WAV file exists. The voice synthesis route handles this case; batch does not.

---

## Section 3: What Is Missing vs Professional-Grade Software

### 3.1 vs ElevenLabs (Voice AI Leader)

| Feature | ElevenLabs | VoiceStudio | Gap |
|---------|------------|-------------|-----|
| Voice cloning from 60s audio | Yes, production-grade | Profile metadata only; reference audio not bound | **CRITICAL** |
| 70+ language TTS | Yes | Engine-dependent (XTTS supports ~16) | **MAJOR** |
| Ultra-low latency streaming (~75ms) | Yes | WebSocket path exists, latency unoptimized | **MODERATE** |
| Emotional voice control | Yes, fine-grained | EmotionControlViewModel exists, wiring unclear | **MODERATE** |
| Voice consistency across long content | Yes (3+ min) | No long-form consistency mechanism | **MAJOR** |
| API for developers | Full REST + SDK | FastAPI backend exists but no public SDK | **MODERATE** |
| Voice library / marketplace | Yes | Stub marketplace route exists | **MAJOR** |
| Audio watermarking for consent | Yes | No watermarking implementation found | **MODERATE** |

### 3.2 vs Descript (AI Audio/Video Editor)

| Feature | Descript | VoiceStudio | Gap |
|---------|----------|-------------|-----|
| Text-based audio editing | Yes — edit transcript, audio follows | No text-based editing; traditional timeline only | **CRITICAL** |
| Automatic transcription (95% accuracy, 30+ languages) | Yes | Whisper integration exists, UI wired | Present (partial) |
| Filler word removal | Yes, automatic | No filler detection/removal | **MODERATE** |
| Studio Sound (noise reduction, enhancement) | Yes, one-click | Audio analysis routes exist; no one-click enhance | **MODERATE** |
| AI regenerate (fix words without re-recording) | Yes | No equivalent feature | **MAJOR** |
| Automatic captions | Yes, time-synced | No caption generation | **MODERATE** |
| Collaborative editing (Rooms) | Yes, real-time | No collaboration features | **MAJOR** |
| Video editing integration | Yes, native | Video routes exist but minimal wiring | **MODERATE** |

### 3.3 vs Professional DAWs (Adobe Audition, Pro Tools, Logic)

| Feature | Professional DAW | VoiceStudio | Gap |
|---------|-----------------|-------------|-----|
| Non-destructive editing | Yes, fundamental | Timeline has undo; no non-destructive edit history | **MAJOR** |
| Multitrack recording | Yes, unlimited tracks | Single recording → upload flow | **CRITICAL** |
| Real-time effects preview | Yes | Effects apply to stored data only, no live preview | **CRITICAL** |
| VST/AU/CLAP plugin support | Yes | Python PostFXProcessor only; no VST host | **CRITICAL** |
| Waveform editing (cut, copy, paste, fade) | Yes, pixel-precise | No waveform editing in synthesis panel | **CRITICAL** |
| Spectral editing | Yes (Audition, RX) | Spectrogram visualization only, no editing | **MAJOR** |
| MIDI support | Yes | None | **MAJOR** |
| Automation lanes | Yes | No parameter automation in timeline | **MAJOR** |
| Time-stretching / pitch-shifting | Yes, real-time | Prosody route is a stub (audio.copy()) | **CRITICAL** |
| Sample-accurate editing | Yes | No sample-level precision | **MAJOR** |
| Latency compensation | Yes | No latency management | **MODERATE** |
| Metering (VU, LUFS, true peak) | Yes, real-time | Meter API returns stored data, not live analysis | **CRITICAL** |
| Normalization (LUFS targeting) | Yes | No loudness normalization | **MAJOR** |
| Bounce/mixdown | Yes | Timeline export exists but limited | **MODERATE** |

### 3.4 vs Resemble AI (Enterprise Voice)

| Feature | Resemble AI | VoiceStudio | Gap |
|---------|-------------|-------------|-----|
| Speech-to-speech (STS) | Yes | No STS pipeline | **MAJOR** |
| Emotional depth control | Yes, fine-grained | Emotion VM exists, depth unclear | **MODERATE** |
| Consent management + watermarking | Yes, built-in | No consent workflow for cloned voices | **MAJOR** |
| Enterprise SSO/RBAC | Yes | Auth middleware exists but minimal | **MODERATE** |
| Localization API (dubbing) | Yes | Multilingual route exists, wiring unverified | **MODERATE** |

---

## Section 4: Architecture and Infrastructure Gaps

### 4.1 Duplicate/Conflicting Code Paths

| Issue | Location | Impact |
|-------|----------|--------|
| **Two synthesis service implementations** | `backend/services/synthesis_service.py` vs `backend/voice/services/synthesis_service.py` | Route uses one; other services use the other. Behavior may diverge. |
| **Two SynthesisViewModel types** | `Views/Panels/VoiceSynthesisViewModel` vs `Features/Synthesis/SynthesisViewModel` | Features version has stub playback; panels version is production. Dead code confusion risk. |
| **Two TimelineViewModel types** | `Views/Panels/TimelineViewModel` vs `Features/Timeline/TimelineViewModel` | Features version is unreferenced orphan. |
| **Two project persistence systems** | API (`/api/projects`) vs `JsonProjectRepository` (local) | No unification; dual-write risk. |
| **ADR-045 number collision** | `ADR-045-mcp-integration-strategy.md` AND `ADR-045-orchestrator-architecture.md` | Plus duplicate `ADR-049-mcp-integration-strategy.md`. Governance confusion. |

### 4.2 Backend Infrastructure Gaps

| Gap | Detail | Professional Standard |
|-----|--------|----------------------|
| **No database** | All persistence is JSON files + `PersistentStore` (JSON on disk) | PostgreSQL/SQLite with migrations, ACID transactions |
| **Global in-memory state** | Timeline uses module-level `_timeline_state`; mixer uses in-memory stores | Per-session, database-backed state |
| **No job queue** | Batch uses `asyncio.create_task` (in-process, lost on restart) | Celery, Redis Queue, or similar durable queue |
| **No background worker** | Training runs in request handler thread/task | Dedicated worker process with heartbeat |
| **Test mode leaks** | `VOICESTUDIO_TEST_MODE` generates fake WAV in synthesis | Professional: test mode is environment-gated, never reachable in production builds |
| **Fake telemetry defaults** | `engine.py` returns hardcoded `engine_ms=12.3, vram_pct=42.0` on failure | Return error or null; never fake metrics |
| **No WebSocket auth** | WS routes do not appear to validate auth consistently | WebSocket connections must authenticate |

### 4.3 Frontend Infrastructure Gaps

| Gap | Detail | Professional Standard |
|-----|--------|----------------------|
| **274 skipped tests** | 9.8% of C# test suite is skipped | < 1% skip rate; each skip has a tracked issue |
| **MainWindow ~2400 lines** | God object risk documented in coherence audit | < 500 lines; decompose into shell + coordinators |
| **No undo across panels** | Each panel may have its own undo; no global undo | Unified undo/redo stack for the entire session |
| **No keyboard shortcut conflicts resolution** | 81 ViewModels may register competing shortcuts | Central shortcut registry with conflict detection |
| **No drag-and-drop between panels** | Library → Timeline, Profile → Synthesis not traced | Drag-and-drop is fundamental DAW UX |
| **No accessibility audit** | No evidence of screen reader testing, high contrast mode, keyboard-only navigation | WCAG 2.1 AA compliance minimum |

### 4.4 Engine Layer Gaps

| Gap | Detail | Professional Standard |
|-----|--------|----------------------|
| **No model download manager** | Engines expect models pre-installed; no UI for download progress | In-app model browser with download, progress, verification |
| **No GPU management UI** | GPU detection in code but no UI for VRAM monitoring or device selection | GPU dashboard with real-time VRAM, device selection |
| **No engine benchmarking UI** | Quality metrics in code but no comparative benchmark flow | Side-by-side engine comparison with MOS scoring |
| **No model versioning** | No mechanism to pin or rollback model versions | Model registry with version selection |
| **Engine fallback chain is hardcoded** | `ENGINE_FALLBACK_CHAIN` is a constant list | User-configurable priority with manual override |
| **Chatterbox torch incompatibility** | Requires torch >= 2.6 but main venv uses 2.2.2+cu121 | Resolved dependency or clear venv isolation |

---

## Section 5: Data Flow and Persistence Gaps

### 5.1 What Should Persist But Doesn't

| Data | Current State | Required State |
|------|---------------|----------------|
| Timeline state | Global in-memory variable | Per-project DB with autosave |
| Synthesis history | Audio files on disk; no query | Searchable history with metadata, tags, favorites |
| Training results | JSON files | Versioned model registry with lineage |
| Effect chains applied to clips | JSON store, not linked to clips | Per-clip effect instance with undo |
| User preferences per workspace | Settings API (flat) | Workspace-scoped settings hierarchy |
| Session recovery data | None | Crash recovery with autosave state |

### 5.2 Cross-Feature Data Flow Gaps

These are the "compounding functionality" connections that make professional software cohesive:

| Source → Destination | Expected Flow | Current State |
|---------------------|---------------|---------------|
| **Synthesis → Timeline** | Drag synthesized audio onto timeline as clip | Manual: user must save audio, then import |
| **Cloning → Profile → Synthesis** | Clone voice, auto-create profile, use in synthesis | Profile created without reference audio binding |
| **Recording → Library → Timeline** | Record, auto-add to library, drag to timeline | Recording uploads to backend; library shows it; timeline import is separate |
| **Transcription → Text-Based Edit → Audio** | Transcribe, edit text, regenerate audio segments | Transcription exists; no text-based edit; no segment regeneration |
| **Training → Profile Update** | Train model, quality improves, profile auto-updates | Training creates jobs; no auto-profile-update pipeline |
| **Effects → Export** | Apply effects chain, export with effects baked in | Effects processing exists; export doesn't automatically apply |
| **Batch → Quality Dashboard** | Batch synthesize, quality scores populate dashboard | Batch has quality fields; dashboard data flow not traced |
| **Timeline → Master → Export** | Multi-track to stereo master with effects, export as production file | Timeline export exists; master bus processing is metadata-only |

---

## Section 6: Security and Production Hardening Gaps

| Category | Gap | Severity |
|----------|-----|----------|
| **Voice consent** | No consent verification for voice cloning (ethical/legal requirement) | CRITICAL |
| **Audio watermarking** | No imperceptible watermark on generated audio | MAJOR |
| **Rate limiting in tests** | Rate limiter is `skipped in test env` | MODERATE |
| **API authentication** | Auth is `require_auth_if_enabled` — optional by design | MAJOR for any deployment |
| **CORS policy** | CORS is enabled in middleware but configuration not audited | MODERATE |
| **Model provenance** | No tracking of which model version produced which output | MAJOR for enterprise |
| **Audit trail** | No audit log of who synthesized what, when | MAJOR for enterprise |

---

## Section 7: UX and Polish Gaps (vs Billion-Dollar Standard)

| Area | Gap | Impact |
|------|-----|--------|
| **First-run experience** | FirstRunWizard.xaml exists; onboarding flow completeness unverified | Users need guided setup (model download, GPU config, API keys) |
| **Loading states** | Most VMs have `IsLoading`; no skeleton screens or shimmer animations | Modern apps show content placeholders |
| **Error messages** | Some backend errors surface raw HTTP status codes | Every error needs actionable user-facing text |
| **Keyboard shortcuts** | KeyboardShortcutsView exists; comprehensiveness and discoverability unclear | Pro tools: fully customizable, printable cheat sheet |
| **Dark/light theme** | ThemeEditorView exists; actual theme implementation depth unclear | Full theme with per-component token system |
| **Responsive layout** | Panel system exists; responsive behavior at different window sizes unclear | Fluid layout with minimum viable sizes per panel |
| **Tooltips and help** | HelpView exists; inline contextual help unclear | Pro tools: tooltips on every control, contextual help links |
| **Progress for long operations** | Progress tracking in synthesis; not universal | Every operation > 2s needs progress indicator with cancel |
| **Notification system** | Toast service exists | Notification center with history, snooze, action buttons |
| **Search** | GlobalSearchView exists | Universal command palette + content search + filter |

---

## Section 8: Missing Features for Professional Parity

### 8.1 CRITICAL Missing (Must Have for Professional Use)

1. **Text-based audio editing** — Edit transcript, audio follows (Descript's core innovation)
2. **Real-time effects preview** — Hear effects before committing
3. **Waveform editing** — Cut, copy, paste, fade, crossfade at sample level
4. **Multitrack recording** — Record multiple sources simultaneously
5. **VST/plugin hosting** — Load third-party audio plugins
6. **Real-time metering** — VU, LUFS, true peak with live audio
7. **Time-stretching / pitch-shifting** — Real DSP, not `audio.copy()`
8. **Voice cloning that works** — Reference audio must bind to profile
9. **Unified project persistence** — One "Save" that captures everything
10. **Database-backed state** — Replace all in-memory and JSON stores

### 8.2 MAJOR Missing (Expected at Professional Tier)

11. **Non-destructive editing** — Edit history with full undo to any point
12. **Spectral editing** — Edit frequency content visually
13. **Parameter automation** — Automate any control over time
14. **AI regenerate** — Fix a word/sentence without re-recording everything
15. **Filler word detection and removal** — Automatic cleanup
16. **LUFS normalization** — Target loudness for broadcast/podcast
17. **Durable job queue** — Survive process restarts
18. **Collaborative editing** — Multi-user real-time editing
19. **Speech-to-speech** — Voice conversion in real-time
20. **Model download manager** — Browse, download, verify models in-app

### 8.3 MODERATE Missing (Differentiators for Enterprise)

21. **Voice consent management** — Legal compliance for cloning
22. **Audio watermarking** — Provenance tracking
23. **Audit trail** — Who generated what, when
24. **Public API/SDK** — Developer integration
25. **Automatic captions** — Generate subtitles from audio
26. **Video integration** — Audio-for-video workflow
27. **Localization/dubbing** — Multi-language voice-over
28. **A/B quality comparison** — Side-by-side engine evaluation UI
29. **Voice marketplace** — Share/sell voice profiles
30. **Mobile companion** — Remote monitoring/playback

---

## Section 9: Quantified Assessment

### 9.1 Feature Completeness by Category

| Category | Panels/Features | Wired E2E | Partial | Stub/Missing | Score |
|----------|----------------|-----------|---------|--------------|-------|
| Voice Synthesis (TTS) | 1 | 1 | 0 | 0 | **100%** |
| Voice Cloning | 1 | 0 | 1 | 0 | **40%** |
| Training | 2 | 0 | 2 | 0 | **50%** |
| Recording | 1 | 1 | 0 | 0 | **90%** |
| Transcription | 1 | 0 | 1 | 0 | **70%** |
| Library/Asset Management | 1 | 0 | 1 | 0 | **70%** |
| Timeline/Multitrack | 1 | 0 | 1 | 0 | **40%** |
| Effects/Mixing | 1 | 0 | 1 | 0 | **50%** |
| Batch Processing | 1 | 0 | 1 | 0 | **60%** |
| Project Management | 1 | 0 | 1 | 0 | **40%** |
| Profiles | 1 | 0 | 1 | 0 | **70%** |
| Settings | 1 | 1 | 0 | 0 | **85%** |
| Audio Playback | 1 | 1 | 0 | 0 | **90%** |
| Real-time DSP | 0 | 0 | 0 | 1 | **0%** |
| Text-based Editing | 0 | 0 | 0 | 1 | **0%** |
| Plugin Hosting | 0 | 0 | 0 | 1 | **0%** |
| Waveform Editing | 0 | 0 | 0 | 1 | **0%** |

**Overall feature completeness: ~38%**

### 9.2 Architecture Maturity

| Dimension | Score | Rationale |
|-----------|-------|-----------|
| Code organization | **85%** | Clean separation, MVVM, DDD backend, clear boundaries |
| Governance | **95%** | 49 rules, 50 ADRs, 9 roles, canonical registry, verification harness |
| Test infrastructure | **75%** | 3000+ tests across two languages; 274 skips; no E2E coverage of core flows |
| Build/CI | **80%** | verify.ps1 is excellent; not in CI (only local); GitHub Actions minimal |
| Security | **45%** | Auth optional, no consent, no watermark, no audit trail |
| Persistence | **25%** | JSON files and in-memory state; no database, no migrations |
| Performance | **30%** | No profiling, no optimization, 45-60s cold start |
| Deployment | **50%** | Inno Setup installer exists; model distribution unsolved |

### 9.3 Professional Readiness Score

| Against | Score | Meaning |
|---------|-------|---------|
| ElevenLabs (voice AI) | **25%** | Missing: cloning quality, language breadth, streaming optimization, marketplace |
| Descript (AI editor) | **20%** | Missing: text-based editing, collaborative, filler removal, AI regenerate |
| Adobe Audition (DAW) | **15%** | Missing: real-time DSP, VST, multitrack, spectral, automation |
| Professional use readiness | **35%** | Can synthesize and play back; cannot replace any existing tool |

---

## Section 10: Priority Remediation Roadmap

### Phase 1: Make Core Features Actually Work (4-6 weeks)
1. Fix voice cloning profile ↔ reference audio binding
2. Replace timeline global in-memory state with per-project SQLite
3. Unify project save (timeline + mixer + profiles + synthesis history)
4. Fix batch processing engine result handling (`None` vs file path)
5. Implement real prosody DSP (pitch shift, time stretch using librosa/rubberband)
6. Replace fake telemetry defaults with proper error responses
7. Consolidate duplicate synthesis service paths

### Phase 2: Professional Audio Foundation (6-8 weeks)
8. Real-time audio metering (LUFS, VU, true peak)
9. Waveform editing (cut, copy, paste, fade, crossfade)
10. Real-time effects preview with bypass toggle
11. Non-destructive editing with full undo history
12. LUFS normalization for export
13. Multi-track recording support
14. Proper model download manager with progress UI

### Phase 3: Competitive Features (8-12 weeks)
15. Text-based audio editing (transcription → edit → regenerate)
16. AI regenerate (fix words without re-recording)
17. Filler word detection and removal
18. Spectral editing view
19. Parameter automation in timeline
20. VST3 plugin hosting (or CLAP)

### Phase 4: Enterprise & Polish (8-12 weeks)
21. Voice consent management + watermarking
22. Audit trail for all synthesis operations
23. Database migration (PersistentStore → SQLite/PostgreSQL)
24. Durable job queue (replace asyncio.create_task)
25. Full accessibility audit (WCAG 2.1 AA)
26. Performance optimization (cold start < 10s)
27. Public API documentation and SDK

---

## Appendix A: File-Level Evidence Index

| Finding | File Path | Line/Section |
|---------|-----------|--------------|
| Prosody stub | `backend/api/routes/voice/processing.py` | `processed_audio = audio.copy()` |
| Fake telemetry | `backend/api/routes/engine.py` | `engine_ms=12.3, vram_pct=42.0` defaults |
| Training simulation | `backend/services/training_service.py` | `_simulate_training` fallback |
| Timeline in-memory | `backend/api/routes/timeline.py` | `_timeline_state` module variable |
| Batch result gap | `backend/api/routes/batch.py` | `if audio is None: raise` |
| Cloning profile gap | `backend/services/profile_service.py` | `create_profile_from_request` — no reference audio |
| Synthesis duplication | `backend/services/synthesis_service.py` vs `backend/voice/services/synthesis_service.py` | Two modules |
| Export local-only | `src/VoiceStudio.App/Views/LibraryView.xaml.cs` | `File.Copy` only |
| Stub playback | `src/VoiceStudio.App/Features/Synthesis/SynthesisViewModel.cs` | `Task.Delay` for play |
| ADR collision | `docs/architecture/decisions/ADR-045-*.md` | Two files, same number |
| Meters fake data | `backend/api/routes/mixer.py` | `POST .../simulate` |
| Dual project storage | `BackendClient.Projects.cs` vs `JsonProjectRepository.cs` | Two persistence systems |

## Appendix B: Contradictions Found During Audit

| Source A | Source B | Contradiction |
|----------|----------|--------------|
| ADR-032 (middleware stack) | `middleware_setup.py` actual code | Stack is larger and ordered differently than documented |
| Extraction inventory (through PR-15) | PR-16/17 scope docs | Inventory doc not updated for PR-16/17 |
| Remainder inventory ("Post-PR-12") | Actual state (PR-15 done) | Doc is stale by 3 PRs |
| Quality Ledger (2026-02-18) | 6 weeks of work since | Ledger not maintained |
| `CLAUDE.md` "70 engine manifests" | Actual count | Count is correct but stated as hand-maintained (should be derived) |
| Training "workflow complete" claims | `SurfaceMaturityFootnote` in VM | VM itself disclaims maturity |

## Appendix C: Industry Benchmark Sources

- ElevenLabs: MOS ~4.1/5, 70+ languages, ~75ms latency, 60s cloning audio (2026)
- Descript: Text-based editing, 95% transcription accuracy, 30+ languages, AI regenerate
- Adobe Audition: Multitrack, spectral display, non-destructive editing, VST hosting
- Resemble AI: STS, emotional depth, consent management, enterprise RBAC
- Professional DAW standard: Real-time effects, automation, metering, plugin hosting, sample-accurate editing

---

*This document is a read-only intelligence deliverable from Role 8 (Project Intelligence Analyst). It does not implement changes. All findings are repo-verified or externally researched as labeled. Recommended handoff roles are specified in Section 10.*

*Generated: 2026-03-28 | Auditor: Role 8 | Confidence: High (forensic code-level audit)*
