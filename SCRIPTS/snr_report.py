import argparse
import json
from pathlib import Path

import numpy as np
import soundfile as sf


def rms(x):
    import numpy as _np

    x = _np.asarray(x, dtype=_np.float64)
    return float((_np.mean(x**2) + 1e-12) ** 0.5)


def snr_db(sig):
    frame = 1024
    if len(sig) < frame:
        return 0.0
    frames = [sig[i : i + frame] for i in range(0, len(sig), frame)]
    rmses = np.array([rms(f) for f in frames])
    sig_r = float(rms(sig))
    noise_r = float(np.quantile(rmses, 0.1))
    import math as _m

    return 20.0 * _m.log10(sig_r / (noise_r + 1e-9))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--segments", required=True)
    args = ap.parse_args()

    segdir = Path(args.segments)
    manifest = segdir / "manifest.json"
    data = json.loads(manifest.read_text(encoding="utf-8"))
    out = []
    for s in data.get("segments", []):
        p = Path(s["path"])
        try:
            sig, sr = sf.read(p, dtype="float32")
            if hasattr(sig, "ndim") and sig.ndim > 1:
                import numpy as _np

                sig = _np.mean(sig, axis=1)
            s["snr_db"] = round(snr_db(sig), 2)
        except Exception:
            s["snr_db"] = None
        out.append(s)
    data["segments"] = out
    manifest.write_text(json.dumps(data, indent=2), encoding="utf-8")
    vals = [s.get("snr_db") for s in out if s.get("snr_db") is not None]
    avg = float(np.mean(vals)) if vals else None
    print(json.dumps({"status": "ok", "updated": len(out), "avg_snr": avg}))


if __name__ == "__main__":
    main()
