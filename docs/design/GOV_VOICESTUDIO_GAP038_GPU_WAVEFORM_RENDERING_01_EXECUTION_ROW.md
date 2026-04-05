# GOV-VOICESTUDIO-GAP038-GPU-WAVEFORM-RENDERING-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP038-GPU-WAVEFORM-RENDERING-01 |
| **GAP** | GAP-038 (GPU / high-performance waveform rendering) |
| **Status** | **Closed** (2026-04-05) — **slice 0** waveform cache; **slice 1** Win2D `CanvasControl` + CPU Path + `WaveformDownsampler` — [remainder closure](../reports/verification/VOICESTUDIO_GAP038_GPU_WAVEFORM_RENDERING_REMAINDER_LANE_CLOSURE_2026-04-05.md); **slice 2** viewport virtualization — [viewport closure](../reports/verification/VOICESTUDIO_GAP038_VIEWPORT_VIRTUALIZATION_LANE_CLOSURE_2026-04-05.md); **slice 3** spectrogram Win2D + CPU fallback + `SpectrogramHeatmapRasterizer` — [spectrogram closure](../reports/verification/VOICESTUDIO_GAP038_SPECTROGRAM_GPU_LANE_CLOSURE_2026-04-05.md) |
| **Phase** | Professional Roadmap v3 — Phase 4 |
| **Role** | UI Engineer |
| **Dependency** | **GAP-040** closed — [GOV_VOICESTUDIO_SUCCESSOR_GAP040_GAP038_SEQUENCE_FREEZE.md](GOV_VOICESTUDIO_SUCCESSOR_GAP040_GAP038_SEQUENCE_FREEZE.md) |

## §1 Objective (frozen)

Improve timeline-related **waveform data** latency and repeat-selection churn by caching `GetWaveformDataAsync` results keyed by `(audioId, width, mode)` with a bounded LRU-style eviction policy, **without** changing edit authority, persistence, or transcript linkage.

**Slice 1 (closed):** `WaveformControl` uses **Win2D `CanvasControl`** (event-driven `Invalidate` only — **no** `CanvasAnimatedControl` always-on loop) when device creation and draw succeed; on failure uses the **existing Path-based** renderer (deterministic fallback).

**Slice 2 (viewport virtualization — frozen):** **Visible-window policy** is owned by **`TimelineViewModel`** (timeline presentation seam), not by `WaveformControl`. The control remains **render-only**: it receives **windowed** sample buffers and a **viewport-local** normalized playhead (`0..1` within the window, or `-1` to hide). **Canonical full waveform samples** remain loaded/stored as today (`WaveformSamples` / `IAudioVisualizationService` cache authority unchanged). **No** new backend fetch keyed by viewport width in this slice: windowing is **client-side** over the cached/downsampled buffer. **Refetch** rules: unchanged from slice 0 — still `GetWaveformDataAsync` with existing width/mode; virtualization does not add per-zoom network requests.

**Acceptance tests (slice 1):** `WaveformDownsampler` unit tests (deterministic downsample parity with prior behavior); build + `validate_xaml_resources.py` + targeted App.Tests + `pytest tests/ci` + `verify.ps1 -Quick` + `run_verification.py` recorded in lane closure §2.

**Acceptance tests (slice 2):** `WaveformViewportPolicy` unit tests (clamping, determinism, playback normalization); `TimelineViewModel` seam test for display buffer refresh when zoom/position/samples change (mock audio player duration); full matrix + closure report.

**Acceptance tests (slice 3):** `SpectrogramHeatmapRasterizer` unit tests (empty input, determinism, non-finite zoom default); build + `validate_xaml_resources.py` + full App.Tests + `pytest tests/ci` + `verify.ps1 -Quick` + `run_verification.py` recorded in spectrogram closure §2.

## §2 Hard IN (this slice)

- `AudioVisualizationService` in-process cache: max **64** entries; on hit return a **copy** of `WaveformData.Samples` so clip mutations cannot corrupt cache or shared lists.
- Deterministic key: `"{audioId}\u001f{width}\u001f{mode}"`.
- Thread-safe: concurrent reads/writes guarded.

## §3 Hard OUT (slice 0 + slice 1 + slice 2)

- Any change to trim/split/fade **semantics**, undo, `derived_from_clip_id`, or `ClipTranscriptLink` rules.  
- Replacing `IAudioVisualizationService` contract or backend waveform API shape.  
- **Spectrogram** transform/cache/API ownership migration (slice 3 closed render path only — see §3.4).  
- **CanvasAnimatedControl** / continuous render loops (battery + lifecycle risk).  
- **Per-viewport backend fetch** or changing waveform API parameters based on timeline scroll (slice 2 uses **client-side** windowing only).

## §3.4 Slice 3 — Spectrogram GPU (frozen)

- **Transform / FFT frame authority:** unchanged — `AudioVisualizationService` + `IAudioVisualizationService`; VM requests frames; **no** spectrogram fetch in `SpectrogramControl`.  
- **Render authority:** `SpectrogramControl` owns Win2D `CanvasControl` draw + `WriteableBitmap` CPU fallback; heatmap pixels from pure **`SpectrogramHeatmapRasterizer`** (unit-tested determinism).  
- **Fallback:** any Win2D `CreateFromBytes` / draw failure → CPU `WriteableBitmap` path for that instance (`Debug.WriteLine`, no silent swallow).  
- **Bounds:** same clamps as pre-slice CPU path — max **1024×512**, min **64×64** (fail-closed sizing).  
- **Hard OUT (slice 3):** waveform viewport/cache/slice-2 policy changes; timeline edit/undo/persistence; backend spectrogram API shape changes; `CanvasAnimatedControl` / continuous loops.

## §3.1 Slice 1 — Hard IN

- Win2D draw path owned entirely inside `WaveformControl` (capability probe + draw + dispose via control lifecycle).  
- **Fallback:** any Win2D create/draw/device-loss failure → Path-based render path for that control instance (operator-visible waveform preserved).  
- **Pixel contract:** downsample + peak/RMS + zoom + amplitude scale (**40%** of control height) unchanged vs pre-slice Path behavior (validated by shared `WaveformDownsampler` + manual/visual spot-check).  
- **Playback line** remains XAML `Line` above waveform layer (unchanged semantics).

## §3.2 Slice 1 — Fallback policy

1. First successful `CanvasControl` draw locks Win2D for that instance until `RecreateResources` or device loss.  
2. On draw/create exception or `RecreateResources` failure → set internal **CPU-only** flag, collapse `CanvasControl`, show `Path`, redraw CPU path.  
3. No silent swallow: failures are surfaced via **Debug.WriteLine** with exception type (no user toast in this lane).

## §3.3 Slice 2 — Hard IN (viewport)

- **`WaveformViewportPolicy`** (pure, testable): normalized window `(start, width)` from `focusTimeSeconds`, `referenceDurationSeconds` (use `IAudioPlayerService.Duration` when `> 0`; otherwise full window `0..1`), and `TimelineZoom` (`visibleWidth = min(1, 1/zoom)`).  
- **`TimelineViewModel`**: owns refresh of **`WaveformDisplaySamples`** + **`WaveformVisualizerPlaybackNormalized`**; calls policy + slice; raises property notifications.  
- **`WaveformControl`**: `Samples` bound to display buffer; `ZoomLevel` fixed **1.0** for this row (zoom expressed in VM window, not double-applied in control).  
- **Bounded indices**: sample window indices clamped; empty input → empty display; non-finite inputs → deterministic safe defaults.

## §4 Authority map

| Concern | Owner |
|---------|--------|
| Waveform **pixels** / GPU | `WaveformControl` Win2D slice 1 (`CanvasControl`) |
| Waveform **samples** (network/cache) | `AudioVisualizationService` (slice 0) |
| **Viewport / visible window** (timeline waveform row) | `TimelineViewModel` + `WaveformViewportPolicy` |
| Spectrogram **heatmap pixels** / GPU | `SpectrogramControl` Win2D slice 3 (`CanvasControl`) + CPU `WriteableBitmap`; raster math `SpectrogramHeatmapRasterizer` |
| Spectrogram **frames** (service/cache) | `AudioVisualizationService` / `IAudioVisualizationService` (unchanged) |
| Clip / project truth | Unchanged — GAP-040 / GAP-012 |

## §5 Verification

- `dotnet test` — existing visualization consumers unchanged behavior.  
- No new pytest required for this slice (C# only).

## §6 Risks

| Risk | Mitigation |
|------|------------|
| Memory pressure | Cap 64 entries; evict FIFO-ish by queue order. |
| Stale cache if audio replaced | Cache key includes `audioId`; regeneration uses new id (GAP-046 path). |

## §7 Rollback

1. Revert `AudioVisualizationService` cache to thin delegate (slice 0).  
2. Revert `WaveformControl` Win2D layer + `WaveformDownsampler` extraction (slice 1); restore Path-only control.  
3. Revert `SpectrogramControl` Win2D layer + `SpectrogramHeatmapRasterizer` (slice 3); restore Image-only control; remove `TimelineView` spectrogram bindings if required for consistency.  

## §8 Closure artifacts (slice 1)

- Lane closure report under `docs/reports/verification/` (VOICESTUDIO_GAP038_GPU_WAVEFORM_RENDERING_REMAINDER_LANE_CLOSURE_YYYY-MM-DD.md).  

## §9 Closure artifacts (slice 2 — viewport)

- [VOICESTUDIO_GAP038_VIEWPORT_VIRTUALIZATION_LANE_CLOSURE_2026-04-05.md](../reports/verification/VOICESTUDIO_GAP038_VIEWPORT_VIRTUALIZATION_LANE_CLOSURE_2026-04-05.md) — `WaveformViewportPolicy` + `TimelineViewModel` seam + `TimelineView` bindings; tests **12** policy + **2** VM seam + downsampler **7**.  

## §10 Closure artifacts (slice 3 — spectrogram GPU)

- [VOICESTUDIO_GAP038_SPECTROGRAM_GPU_LANE_CLOSURE_2026-04-05.md](../reports/verification/VOICESTUDIO_GAP038_SPECTROGRAM_GPU_LANE_CLOSURE_2026-04-05.md) — `SpectrogramHeatmapRasterizer` + `SpectrogramControl` Win2D + `TimelineView` bindings; test counts recorded in report §2.  
