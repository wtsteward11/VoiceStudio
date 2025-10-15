import argparse
import subprocess
import sys
from pathlib import Path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--text", required=True)
    ap.add_argument("--voice", required=True, help=".onnx path")
    ap.add_argument("--out", required=True, help="wav path")
    ap.add_argument("--piper", default=r"C:\VoiceStudio\tools\piper\piper.exe")
    ap.add_argument("--config", default=None, help="optional .json, default: voice + .json")
    ap.add_argument("--espeak", default=r"C:\VoiceStudio\tools\piper\espeak-ng-data")
    args = ap.parse_args()

    voice = Path(args.voice)
    if not voice.exists():
        print("voice .onnx not found", file=sys.stderr)
        sys.exit(3)
    conf = Path(args.config) if args.config else Path(str(voice) + ".json")
    if not conf.exists():
        print("voice .json not found", file=sys.stderr)
        sys.exit(3)

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)

    cmd = [args.piper, "-m", str(voice), "-c", str(conf), "--espeak_data", args.espeak, "-f", str(out)]
    # send text over stdin
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    stdout, stderr = p.communicate(args.text)
    if p.returncode != 0:
        print(stderr, file=sys.stderr)
        sys.exit(p.returncode)

    print(str(out))
    sys.exit(0)


if __name__ == "__main__":
    main()
