"""Start uvicorn with repo-local models + TORCH_HOME for Silero preflight proofs."""
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
env = os.environ.copy()
env["PYTHONPATH"] = str(ROOT)
env["VOICESTUDIO_MODELS_PATH"] = str(ROOT / "models")
env["TORCH_HOME"] = str(ROOT / "models" / "torch")
subprocess.Popen(
    [
        sys.executable,
        "-m",
        "uvicorn",
        "backend.api.main:app",
        "--host",
        "127.0.0.1",
        "--port",
        "8002",
    ],
    cwd=str(ROOT),
    env=env,
    creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0,
)
print("uvicorn Popen issued", flush=True)
