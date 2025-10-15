import argparse
import json
import sys
from pathlib import Path

# Align with whisperx (CPU-safe: int8)
try:
    import whisperx
except Exception as e:
    print("whisperx import error:", e, file=sys.stderr)
    sys.exit(1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--segments", required=True, help="folder containing manifest.json and asr.json")
    ap.add_argument("--lang", default="en")
    args = ap.parse_args()

    seg_dir = Path(args.segments)
    manifest = seg_dir / "manifest.json"
    asr_json = seg_dir / "asr.json"
    out_json = seg_dir / "aligned.json"

    if not manifest.exists() or not asr_json.exists():
        print(f"Missing files. Need {manifest} and {asr_json}", file=sys.stderr)
        sys.exit(2)

    device = "cpu"
    # load ASR model (compute_type int8 for CPU)
    _ = whisperx.load_model("small", device=device, compute_type="int8")

    # load align model (API uses language_code, not 'language')
    model_a, metadata = whisperx.load_align_model(language_code=args.lang, device=device)

    with open(asr_json, "r", encoding="utf-8") as f:
        asr = json.load(f)

    aligned = []
    for item in asr.get("segments", []):
        audio_path = item["path"]
        text = item.get("text", "").strip()
        if not text:
            aligned.append({**item, "words": []})
            continue

        # Transcribe again just to ensure segments are valid for aligner
        audio = whisperx.load_audio(audio_path)
        # word-level alignment
        try:
            result = whisperx.align(
                transcript={"text": text}, model_a=model_a, metadata=metadata, audio=audio, device=device
            )
            words = result.get("segments", [])
        except Exception:
            words = []
        aligned.append({**item, "words": words})

    with open(out_json, "w", encoding="utf-8") as f:
        json.dump({"segments": aligned}, f, indent=2)

    print(str(out_json))
    sys.exit(0)


if __name__ == "__main__":
    main()
