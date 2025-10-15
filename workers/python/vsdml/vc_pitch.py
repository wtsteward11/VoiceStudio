import argparse
import subprocess
import sys
from pathlib import Path

ap = argparse.ArgumentParser()
ap.add_argument("--in", dest="inp", required=True)
ap.add_argument("--out", dest="out", required=True)
ap.add_argument("--semitones", type=float, default=3.0)
args = ap.parse_args()

inp = Path(args.inp)
out = Path(args.out)
out.parent.mkdir(parents=True, exist_ok=True)

if not inp.exists():
    print("input not found", file=sys.stderr)
    sys.exit(2)

# Pitch shift via asetrate+aresample (length will change; OK for stub)
factor = 2 ** (args.semitones / 12.0)  # pitch ratio
# assume 48000 target rate (our pipeline uses 48k)
cmd = [
    "ffmpeg",
    "-y",
    "-hide_banner",
    "-loglevel",
    "error",
    "-i",
    str(inp),
    "-af",
    f"asetrate=48000*{factor},aresample=48000",
    str(out),
]
rc = subprocess.call(cmd)
if rc != 0:
    sys.exit(rc)
print(str(out))
