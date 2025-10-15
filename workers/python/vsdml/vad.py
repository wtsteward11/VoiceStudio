import argparse
import contextlib
import json
import subprocess
import sys
import wave
from pathlib import Path


def get_duration_ffprobe(path: Path):
    try:
        cmd = ["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", str(path)]
        out = subprocess.check_output(cmd, stderr=subprocess.STDOUT, text=True).strip()
        return float(out)
    except Exception:
        return None


def get_duration_wave(path: Path):
    try:
        with contextlib.closing(wave.open(str(path), "rb")) as w:
            return w.getnframes() / float(w.getframerate())
    except Exception:
        return None


ap = argparse.ArgumentParser()
ap.add_argument("--in", dest="inp", required=True)
ap.add_argument("--out", dest="out", required=True)
ap.add_argument("--aggr", default="2")
ap.add_argument("--min", default="0.6")
args = ap.parse_args()

inp = Path(args.inp)
out_dir = Path(args.out)
out_dir.mkdir(parents=True, exist_ok=True)
manifest_path = out_dir / "manifest.json"

if not inp.exists():
    print(f"input wav not found: {inp}", file=sys.stderr)
    sys.exit(2)

dur = get_duration_ffprobe(inp)
if dur is None:
    dur = get_duration_wave(inp)
if dur is None or dur <= 0:
    print("could not get duration for input wav", file=sys.stderr)
    sys.exit(3)

data = {"segments": [{"id": "seg-0001", "path": str(inp), "t0": 0.0, "t1": round(float(dur), 3)}]}
with open(manifest_path, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2)

print(str(manifest_path))
