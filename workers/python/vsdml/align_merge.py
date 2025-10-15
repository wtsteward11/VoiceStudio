import argparse
import json
from pathlib import Path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--segments", required=True, help="folder with manifest.json, asr.json, aligned.json")
    ap.add_argument("--out", required=True, help="output dataset.json path")
    args = ap.parse_args()

    segdir = Path(args.segments)
    with open(segdir / "manifest.json", "r", encoding="utf-8") as f:
        manifest = json.load(f)["segments"]
    with open(segdir / "asr.json", "r", encoding="utf-8") as f:
        asr = {x["index"]: x for x in json.load(f)["segments"]}
    with open(segdir / "aligned.json", "r", encoding="utf-8") as f:
        aligned = {x["index"]: x for x in json.load(f)["items"]}

    items = []
    for m in manifest:
        idx = m["index"]
        entry = {
            "index": idx,
            "path": m["path"],
            "start_ms": m["start_ms"],
            "end_ms": m["end_ms"],
            "text": asr.get(idx, {}).get("text", ""),
            "words": aligned.get(idx, {}).get("words", []),
        }
        items.append(entry)

    with open(args.out, "w", encoding="utf-8") as f:
        json.dump({"items": items}, f, indent=2)
    print("Wrote:", args.out)


if __name__ == "__main__":
    main()
