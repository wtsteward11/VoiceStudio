# Slice 22 — proof session notes (`whisper_cpp` readiness)

**When:** 2026-04-23 (closure session)  
**Stack:** FastAPI `TestClient` against `backend.api.main:app` (equivalent import surface to `uvicorn backend.api.main:app`).

## Commands (reference)

```powershell
Set-Location E:\VoiceStudio
.\.venv\Scripts\python.exe -c "import json; from fastapi.testclient import TestClient; from backend.api.main import app; c=TestClient(app); r=c.get('/api/health/preflight'); print(json.dumps(r.json().get('checks',{}).get('whisper_cpp'), indent=2))"
```

## Result

- **`checks.whisper_cpp.ok`:** **`false`** (**Outcome B**) — missing GGUF at resolved `model_path` under `get_models_path()`.
- **`ok: null`:** **absent** — Slice 22 acceptance met for boolean honesty.

Full JSON snapshot: [slice22_preflight_whisper_cpp.json](slice22_preflight_whisper_cpp.json).
