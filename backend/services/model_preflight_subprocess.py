"""
Chatterbox HF and Tortoise subprocess probes extracted from ``model_preflight``.

Keeps ``backend.services.model_preflight`` under the monolith-prevention line budget
while preserving identical runtime behavior.
"""

from __future__ import annotations

import json
import logging
import os
import subprocess
import tempfile
from pathlib import Path

logger = logging.getLogger(__name__)

CHATTERBOX_REPO_ID = "ResembleAI/chatterbox"
CHATTERBOX_PROBE_FILE = "ve.safetensors"

_CHATTERBOX_HF_SUBPROCESS_TAIL = """
def main():
    try:
        from huggingface_hub import hf_hub_download
    except ImportError as e:
        print(json.dumps({"ok": False, "error": "import", "detail": repr(e)}))
        sys.exit(1)
    downloaded = False
    if not AUTO:
        try:
            local_path = hf_hub_download(repo_id=REPO, filename=FN, local_files_only=True)
        except Exception as e:
            print(json.dumps({"ok": False, "error": "hf", "detail": type(e).__name__ + ": " + str(e)}))
            sys.exit(1)
    else:
        had_local = False
        try:
            hf_hub_download(repo_id=REPO, filename=FN, local_files_only=True)
            had_local = True
        except Exception:
            had_local = False
        try:
            local_path = hf_hub_download(repo_id=REPO, filename=FN)
            downloaded = not had_local
        except Exception as e:
            print(json.dumps({"ok": False, "error": "hf", "detail": type(e).__name__ + ": " + str(e)}))
            sys.exit(1)
    print(json.dumps({"ok": True, "path": str(local_path), "downloaded": downloaded}))


main()
"""


def chatterbox_hf_subprocess_script(auto_download: bool) -> str:
    """Run inside ``venv_advanced_tts`` so HF cache paths match that interpreter."""
    ad = "True" if auto_download else "False"
    return (
        "import json\nimport sys\nAUTO = "
        + ad
        + "\nREPO = "
        + json.dumps(CHATTERBOX_REPO_ID)
        + "\nFN = "
        + json.dumps(CHATTERBOX_PROBE_FILE)
        + "\n"
        + _CHATTERBOX_HF_SUBPROCESS_TAIL
    )


def subprocess_chatterbox_hf_hub(
    python_exe: Path,
    auto_download: bool,
    *,
    timeout: float = 180.0,
) -> tuple[str, bool, str | None]:
    """Returns ``(local_path_str, downloaded, error_or_none)``."""
    script_body = chatterbox_hf_subprocess_script(auto_download)
    fd, tpath = tempfile.mkstemp(suffix="_chatterbox_hf.py", text=True)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            f.write(script_body)
        run_env = os.environ.copy()
        run_env.setdefault("HF_ENDPOINT", "https://huggingface.co")
        proc = subprocess.run(
            [str(python_exe), tpath],
            capture_output=True,
            text=True,
            timeout=timeout,
            check=False,
            env=run_env,
        )
    finally:
        try:
            os.unlink(tpath)
        except OSError as ex:
            logger.debug("chatterbox HF temp script cleanup: %s", ex)

    if proc.returncode != 0:
        tail = (proc.stderr or proc.stdout or "").strip()
        return "", False, tail or f"exit_code={proc.returncode}"

    out = (proc.stdout or "").strip().splitlines()
    if not out:
        return "", False, "chatterbox HF probe: empty stdout"
    try:
        payload = json.loads(out[-1])
    except json.JSONDecodeError as e:
        return "", False, f"chatterbox HF probe: invalid json ({e}): {out[-1]!r}"

    if not payload.get("ok"):
        detail = payload.get("detail") or payload.get("error") or payload
        return "", False, str(detail)

    return str(payload["path"]), bool(payload.get("downloaded")), None


_TORTOISE_WARM_SUBPROCESS_BODY = """
import json
import sys
from pathlib import Path

MODELS = Path(sys.argv[1])
AUTO = sys.argv[2] == "1"


def _has_weights() -> bool:
    return MODELS.is_dir() and any(p.is_file() for p in MODELS.rglob("*"))


def main() -> None:
    if not _has_weights() and not AUTO:
        print(json.dumps({"ok": False, "error": "no_cache", "models_dir": str(MODELS)}))
        sys.exit(1)
    if not _has_weights() and AUTO:
        import torch
        from tortoise.api import TextToSpeech

        device = "cuda" if torch.cuda.is_available() else "cpu"
        tts = TextToSpeech(device=device, models_dir=str(MODELS), kv_cache=True)
        del tts
        if torch.cuda.is_available():
            try:
                torch.cuda.empty_cache()
            except Exception as e:
                print(
                    json.dumps({"warn": "cuda_empty_cache", "detail": repr(e)}),
                    file=sys.stderr,
                )
        print(json.dumps({"ok": True, "downloaded": True, "models_dir": str(MODELS)}))
        return
    print(json.dumps({"ok": True, "downloaded": False, "models_dir": str(MODELS)}))


main()
"""


def subprocess_tortoise_models_ready(
    python_exe: Path,
    models_dir: Path,
    auto_download: bool,
    *,
    timeout: float = 600.0,
) -> tuple[bool, str | None]:
    """Run Tortoise weight/cache validation in the family venv. Returns (downloaded, error_or_none)."""
    fd, tpath = tempfile.mkstemp(suffix="_tortoise_warm.py", text=True)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            handle.write(_TORTOISE_WARM_SUBPROCESS_BODY)
        proc = subprocess.run(
            [str(python_exe), tpath, str(models_dir), "1" if auto_download else "0"],
            capture_output=True,
            text=True,
            timeout=timeout,
            check=False,
        )
    finally:
        try:
            os.unlink(tpath)
        except OSError as ex:
            logger.debug("tortoise warm temp script cleanup: %s", ex)

    if proc.returncode != 0:
        tail = (proc.stderr or proc.stdout or "").strip()
        return False, tail or f"exit_code={proc.returncode}"

    out = (proc.stdout or "").strip().splitlines()
    if not out:
        return False, "tortoise models probe: empty stdout"
    try:
        payload = json.loads(out[-1])
    except json.JSONDecodeError as e:
        return False, f"tortoise models probe: invalid json ({e}): {out[-1]!r}"

    if not payload.get("ok"):
        detail = payload.get("detail") or payload.get("error") or payload
        return False, str(detail)

    return bool(payload.get("downloaded")), None
