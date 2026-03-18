# Installed Layout Reference

This document describes the packaged directory tree produced by the VoiceStudio installer. It is the source of truth for `FindAppRoot`, `HasBackendMarker`, and Python/runtime discovery in `BackendProcessManager`.

**Last verified:** 2026-03-17 against `installer/VoiceStudio.iss` and `installer/prepare-runtime.ps1`.

---

## Installer Output Structure

Default installation path: `C:\Program Files\VoiceStudio` (or user-selected `{app}`).

```
{app}/
├── App/
│   ├── VoiceStudio.App.exe      # Frontend executable
│   └── (WinUI 3 runtime DLLs, resources)
├── Backend/
│   ├── api/
│   │   ├── main.py              # Backend entrypoint (HasBackendMarker requires this)
│   │   ├── routes/
│   │   └── ws/
│   ├── services/
│   ├── requirements.txt
│   └── ...
├── Core/
│   ├── engines/
│   ├── audio/
│   ├── runtime/
│   └── ...
├── Engines/
│   ├── audio/
│   ├── image/
│   ├── video/
│   └── llm/
├── Runtime/
│   ├── python/
│   │   └── python.exe           # Bundled Python (from prepare-runtime.ps1)
│   └── ffmpeg/
│       └── ffmpeg.exe           # Optional; from prepare-runtime.ps1
├── Shared/
├── Docs/
└── requirements.txt
```

---

## Startup Assumptions (BackendProcessManager)

### App Root Discovery

`FindAppRoot` resolves the app root in this order:

1. **VOICESTUDIO_APP_ROOT** env var (if set and `HasBackendMarker` passes)
2. **exe_dir** – directory containing the executable
3. **exe_parent** – parent of exe_dir (used when EXE is in `App/` subdir)
4. **dev_walk** – walk up for `.git` or `VoiceStudio.sln` (Debug only)

For installed layout: EXE is at `{app}\App\VoiceStudio.App.exe`, so `exe_parent` yields `{app}`.

### HasBackendMarker

```csharp
Directory.Exists(dir + "/backend") && File.Exists(dir + "/backend/api/main.py")
```

On Windows, paths are case-insensitive. The installer uses `Backend` (capital B); the check uses `backend` (lowercase). Both resolve to the same directory.

### Python Discovery

Candidates, in order:

1. `{appRoot}/Runtime/python/python.exe` – bundled runtime (installer)
2. `{appRoot}/venv/Scripts/python.exe` – local venv (dev)
3. `{appRoot}/.venv/Scripts/python.exe` – alternate venv (dev)

The installer places Python at `{app}/Runtime/python/` when `prepare-runtime.ps1` has been run and `installer/runtime/python/` exists before build.

### FFmpeg

Optional: `{appRoot}/Runtime/ffmpeg/ffmpeg.exe`. Set `VOICESTUDIO_FFMPEG_PATH` when present. The installer also sets `VOICESTUDIO_FFMPEG_PATH` in the user environment to `{app}\Runtime\ffmpeg\ffmpeg.exe`.

---

## Validation Checklist

Before release, verify:

- [ ] `{app}/Backend/api/main.py` exists
- [ ] `{app}/Runtime/python/python.exe` exists (or user has system Python)
- [ ] `{app}/Runtime/ffmpeg/ffmpeg.exe` exists if prepare-runtime included FFmpeg
- [ ] `FindAppRoot` returns `{app}` when run from installed EXE
- [ ] `HasBackendMarker({app})` returns true

---

## References

- `src/VoiceStudio.App/Services/BackendProcessManager.cs` – `FindAppRoot`, `HasBackendMarker`, Python candidates
- `installer/VoiceStudio.iss` – Inno Setup file layout
- `installer/prepare-runtime.ps1` – Python and FFmpeg bundle preparation
