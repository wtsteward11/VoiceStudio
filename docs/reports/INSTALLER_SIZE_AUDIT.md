# Installer Size Audit (Item 32)

**Purpose:** Document the output size breakdown of the VoiceStudio runtime bundle for the content-creator wedge. Budget: total ≤ 300MB; if exceeded, identify components that can be made optional (download-on-demand).

## How to produce this report

1. From repo root, run: `.\installer\prepare-runtime.ps1`
2. Optionally with starter models: `.\installer\prepare-runtime.ps1 -IncludeModels` (then measure `installer/runtime/models/` manually)
3. Record sizes from script output and/or measure directories under `installer/runtime/`.

## Size breakdown (fill after running prepare-runtime.ps1)

| Component        | Path                      | Size (MB) | Notes                          |
|------------------|---------------------------|-----------|--------------------------------|
| Embedded Python  | `installer/runtime/python/` | 0 (not built) | Includes pip + requirements.txt deps |
| FFmpeg           | `installer/runtime/ffmpeg/`  | 0 (not built) | Win64 GPL build                 |
| Models (optional)| `installer/runtime/models/` | 0 (not built) | Piper, Whisper, etc.; recommend download-on-demand |
| App binaries     | From build output          | 0 (not built) | WinUI + .NET runtime            |
| **Total**        | —                          | 0         | **Target: ≤ 300MB**             |

## Optional / download-on-demand candidates

If total exceeds 300MB:

- **Engine model weights:** Do not bundle large TTS/STT models; use first-run or on-demand download (see `model_preflight.py` and engine manifests).
- **FFmpeg:** Required for export; keep in bundle unless a smaller static build is available.
- **Python + deps:** Required for backend; consider trimming unused packages in `requirements.txt`.

## Revision history

| Date       | Author | Change |
|------------|--------|--------|
| 2026-02-28 | —      | Initial audit template; run `prepare-runtime.ps1` to fill sizes. |
