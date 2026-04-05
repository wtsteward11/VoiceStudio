# VoiceStudio GAP-031 Timeline multi-track mixdown → master export — 2026-04-02

**Lane:** **GOV-VOICESTUDIO-GAP031-TIMELINE-MULTITRACK-MIXDOWN-01** — deterministic multi-track mix, solo/mute, import-from-project before canonical export, `PUT /api/timeline/tracks/{id}`, and C# use-case/view wiring.  
**Execution row:** [GOV_VOICESTUDIO_GAP031_TIMELINE_MULTITRACK_MIXDOWN_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP031_TIMELINE_MULTITRACK_MIXDOWN_01_EXECUTION_ROW.md)  
**Tracker:** **GAP-031** **Closed** — [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md).  
**Product:** **GAP-034** / **GAP-045** / **GAP-047** remain **Open** per tracker (out of scope for this lane).

## 0) Verification provenance

**Label:** **Independently repo-verified locally** — commands below executed on a developer machine (Windows, .NET 8, Python 3.9).

## 1) Scope summary

- **Backend (`backend/api/routes/timeline.py`):** `_render_timeline_audio` — audio tracks only; sort by `(order, id)`; solo resolution; mute skip; no audible mix → `None`. `export_timeline` — HTTP **400** when no rendered audio and no usable fallback. `POST /import-from-project` hydrates `_timeline_state` from `TrackStore` + `AudioRegistry.get_path`. `PUT /tracks/{track_id}` updates in-memory mix fields.
- **Backend (`backend/api/routes/tracks.py`):** `is_muted` / `is_solo` on track create/update (persistence for import).
- **Frontend:** `ITimelineUseCase.ImportProjectTimelineAsync`, `UpdateTimelineTrackAsync`, `ExportAsync` (import when `ProjectId` set; `BackendValidationException` → `InvalidOperationException`). `TimelineViewModel.PersistTrackMixStateAsync` → project `UpdateTrackAsync` + timeline `UpdateTimelineTrackAsync`. `TimelineView` injects `ITimelineUseCase`.
- **Tests:** `test_timeline_mixdown.py`; `test_timeline.py` updates; C# `TimelineUseCaseTests`, `TimelineViewModelTests`, `FileOperationsHandlerTests`; dispatcher-drain hardening for `TranscribeViewModelInlineEditTests` apply-job row (stability under async progress + finalize).

## 2) Verification matrix (closure run)

| Command | Result (closure run) |
|--------|----------------------|
| `dotnet build src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS (0 errors) |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS — **3029** passed, **274** skipped, **0** failed |
| `python -m pytest tests/unit/backend/api/routes/test_timeline.py tests/unit/backend/api/routes/test_timeline_mixdown.py -q` | PASS — **41** passed |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS — **217** passed, **2** deselected |
| `python scripts/run_verification.py` | PASS — **9/9** gates; rolling `.buildlogs/verification/last_run.json` (see **timestamp_short** at closure time) |

**Note:** One full `dotnet test` invocation hit a transient test host crash on first attempt; immediate **retry** with `--no-build` completed **3029** passed — cite retry as operational noise, not acceptance failure.

## 3) Proof artifacts

- [GOV_VOICESTUDIO_GAP031_TIMELINE_MULTITRACK_MIXDOWN_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP031_TIMELINE_MULTITRACK_MIXDOWN_01_EXECUTION_ROW.md)
- `backend/api/routes/timeline.py`, `backend/api/routes/tracks.py`
- `src/VoiceStudio.App/UseCases/ITimelineUseCase.cs`, `TimelineUseCase.cs`
- `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs`, `TimelineView.xaml.cs`
- `tests/unit/backend/api/routes/test_timeline_mixdown.py`, `tests/unit/backend/api/routes/test_timeline.py`
- `src/VoiceStudio.App.Tests/UseCases/TimelineUseCaseTests.cs`, `ViewModels/TimelineViewModelTests.cs`, `Commands/FileOperationsHandlerTests.cs`
- `.cursor/STATE.md`, `docs/governance/CANONICAL_REGISTRY.md`, `docs/design/PROFESSIONAL_GAP_TRACKER.md`

## 4) Honest limits

- **Pan** on timeline tracks is still **not** applied in mix math (execution row Hard OUT).
- **In-memory** `_timeline_state** still resets on backend restart; persistence gap remains outside this lane.
- **Transcribe** apply-job UI tests now **drain** the VM dispatcher after coordinator completion to avoid Racing `Running` vs `Succeeded` observable state — behavioral product unchanged; test harness alignment only.

## 5) Closure

**GOV-VOICESTUDIO-GAP031-TIMELINE-MULTITRACK-MIXDOWN-01:** **Closed** 2026-04-02 with proof-backed acceptance per execution row and this report.

**Next open hero-path (typical):** **GAP-034** (OS notifications) — see [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md).
