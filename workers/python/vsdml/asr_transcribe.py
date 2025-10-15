import json
import sys
from pathlib import Path

from faster_whisper import WhisperModel

seg_dir = Path(r"C:\VoiceStudio\projects\demo\dataset\segments")
manifest = seg_dir / "manifest.json"
asr_out = seg_dir / "asr.json"

if not manifest.exists():
    print(f"manifest not found: {manifest}", file=sys.stderr)
    sys.exit(2)

with open(manifest, "r", encoding="utf-8") as f:
    man = json.load(f)

model = WhisperModel("small", device="cpu", compute_type="int8")

out_segments = []
for s in man.get("segments", []):
    path = s["path"]
    segments, _ = model.transcribe(path, vad_filter=False)
    text = "".join(seg.text for seg in segments).strip()
    out_segments.append({**s, "text": text})

with open(asr_out, "w", encoding="utf-8") as f:
    json.dump({"segments": out_segments}, f, indent=2)

print(str(asr_out))
