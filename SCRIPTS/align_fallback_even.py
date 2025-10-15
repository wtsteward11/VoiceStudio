import json
from pathlib import Path

segdir = Path(r"C:\VoiceStudio\projects\demo\dataset\segments")
manifest = segdir / "manifest.json"
asr_json = segdir / "asr.json"
out_json = segdir / "aligned.json"

m = json.loads(manifest.read_text(encoding="utf-8"))
a = json.loads(asr_json.read_text(encoding="utf-8"))

dur_by_idx = {s["index"]: (s["end_ms"] - s["start_ms"]) / 1000.0 for s in m["segments"]}
out = {"segments": []}
for item in a["segments"]:
    idx = item["index"]
    text = (item.get("text") or "").strip()
    dur = max(0.01, dur_by_idx.get(idx, 1.0))
    words = text.split()
    if not words:
        out["segments"].append({"index": idx, "path": item["path"], "words": []})
        continue
    step = dur / len(words)
    t = 0.0
    W = []
    for w in words:
        W.append({"word": w, "start": round(t, 3), "end": round(min(dur, t + step), 3)})
        t += step
    out["segments"].append({"index": idx, "path": item["path"], "words": W})

out_json.write_text(json.dumps(out, indent=2), encoding="utf-8")
print("ok:", out_json)
