#!/usr/bin/env python3
"""
GAP-015 slice 3: aggregate CI timing samples into slo_baselines.json (schema v1).

Reads JSON produced by tests (via tests/ci/slo_timing_io.py) and writes a
machine-readable baseline artifact next to runtime_proof.json.

Usage:
  python scripts/ci/write_slo_baseline_proof.py \\
    --timing-json PATH \\
    --output PATH \\
    --commit-hash HASH \\
    --environment asgi_transport \\
    --proof-grade R
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

# Canonical workflow definitions (order preserved in output)
WORKFLOWS: list[tuple[str, str, str]] = [
    (
        "backend_readiness",
        "GET /api/health",
        "ASGI transport; client perf_counter around health GET",
    ),
    (
        "canonical_synthesis",
        "POST /api/voice/synthesize",
        "ASGI transport; client perf_counter around synthesize POST only (after setup)",
    ),
    (
        "training_export_rejection",
        "POST /api/training/export",
        "ASGI transport; client perf_counter around export POST (expected 404)",
    ),
]


def _linear_percentile(sorted_vals: list[float], p: float) -> float:
    """Linear interpolation percentile; p in [0, 100]."""
    if not sorted_vals:
        return 0.0
    if len(sorted_vals) == 1:
        return sorted_vals[0]
    k = (len(sorted_vals) - 1) * (p / 100.0)
    f = math.floor(k)
    c = math.ceil(k)
    if f == c:
        return sorted_vals[int(k)]
    return sorted_vals[f] + (sorted_vals[c] - sorted_vals[f]) * (k - f)


def _aggregate_seconds(values: list[float]) -> tuple[int, float | None, float | None, float | None]:
    """Return sample_count, p50, p95, p99 (p99 None if n < 100)."""
    n = len(values)
    if n == 0:
        return 0, None, None, None
    s = sorted(values)
    p50 = _linear_percentile(s, 50.0)
    p95 = _linear_percentile(s, 95.0)
    p99 = _linear_percentile(s, 99.0) if n >= 100 else None
    return n, p50, p95, p99


def _fingerprint_payload(workflows_out: list[dict[str, Any]], commit_hash: str) -> str:
    canonical = json.dumps(
        {"commit_hash": commit_hash, "workflows": workflows_out},
        sort_keys=True,
        separators=(",", ":"),
    )
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def load_samples(timing_path: Path) -> list[dict[str, Any]]:
    if not timing_path.is_file():
        return []
    try:
        data = json.loads(timing_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return []
    samples = data.get("samples")
    if not isinstance(samples, list):
        return []
    out: list[dict[str, Any]] = []
    for item in samples:
        if not isinstance(item, dict):
            continue
        wid = item.get("workflow_id")
        ep = item.get("endpoint")
        sec = item.get("seconds")
        if isinstance(wid, str) and isinstance(ep, str) and isinstance(sec, (int, float)):
            out.append(item)
    return out


def build_document(
    *,
    samples: list[dict[str, Any]],
    commit_hash: str,
    proof_grade: str,
    environment: str,
) -> dict[str, Any]:
    by_id: dict[str, list[float]] = defaultdict(list)
    for s in samples:
        wid = s["workflow_id"]
        by_id[str(wid)].append(float(s["seconds"]))

    workflows_out: list[dict[str, Any]] = []
    for wf_id, endpoint, notes in WORKFLOWS:
        vals = by_id.get(wf_id, [])
        n, p50, p95, p99 = _aggregate_seconds(vals)
        if n > 0:
            status = "RECORDED"
            notes_out = (
                f"{notes} sample_count={n}; with small n, p95 approaches tail of observations."
            )
        else:
            status = "NOT_RECORDED"
            notes_out = (
                f"{notes} — no samples captured (test skipped, failed early, or env unset)."
            )
        workflows_out.append(
            {
                "id": wf_id,
                "endpoint": endpoint,
                "sample_count": n,
                "p50_seconds": round(p50, 6) if p50 is not None else None,
                "p95_seconds": round(p95, 6) if p95 is not None else None,
                "p99_seconds": round(p99, 6) if p99 is not None else None,
                "status": status,
                "notes": notes_out,
            }
        )

    fp = _fingerprint_payload(workflows_out, commit_hash)
    return {
        "schema_version": 1,
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "commit_hash": commit_hash,
        "proof_grade": proof_grade,
        "environment": environment,
        "workflows": workflows_out,
        "baseline_policy": "advisory",
        "evidence_fingerprint": fp,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Write slo_baselines.json (GAP-015 slice 3).")
    parser.add_argument("--timing-json", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--commit-hash", type=str, required=True)
    parser.add_argument("--proof-grade", type=str, default="R")
    parser.add_argument("--environment", type=str, default="asgi_transport")
    args = parser.parse_args()

    samples = load_samples(args.timing_json)
    doc = build_document(
        samples=samples,
        commit_hash=args.commit_hash,
        proof_grade=args.proof_grade,
        environment=args.environment,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(doc, indent=2, sort_keys=False) + "\n", encoding="utf-8")
    print(f"Wrote {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
