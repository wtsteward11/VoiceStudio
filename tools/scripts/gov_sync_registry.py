"""One-off governance header normalization (registry + gap tracker)."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

REG = ROOT / "docs/governance/CANONICAL_REGISTRY.md"
text = REG.read_text(encoding="utf-8")
lines = text.splitlines(True)
for i, line in enumerate(lines):
    if line.startswith("> **Last Updated**"):
        lines[i] = (
            "> **Last Updated**: 2026-04-05 — **Authoritative current verification:** "
            "[.cursor/STATE.md](../.cursor/STATE.md) **Last Verified Commands** + **LATEST PROOF INDEX**. "
            "**Registry mega-header (historical narrative, not competing truth):** see **Update Addendum** below; "
            "full milestone chain: [STATE_MILESTONE_SNAPSHOT_2026-04-05.md](../archive/STATE_MILESTONE_SNAPSHOT_2026-04-05.md).\n"
        )
        break
else:
    raise SystemExit("CANONICAL_REGISTRY: Last Updated not found")
for i, line in enumerate(lines):
    if line.startswith("| Session State |"):
        lines[i] = (
            "| Session State | `.cursor/STATE.md` | 2026-04-05 | **Operational truth** — read "
            "[.cursor/STATE.md](../.cursor/STATE.md) **ACTIVE WINDOW** only. **Latest verified mirror (post GAP-045 reload/rehydrate closure):** "
            "App.Tests **3082**/skipped **274**; `pytest tests/ci` **217**; Quick `artifacts/verify/20260405_070100/`; "
            "`.buildlogs/verification/last_run.json` **20260405-071523**; OnlyStage UI smokes `20260405_071408` / `071415` / `071423` / `071442`. "
            "**Posture:** Active Task **None**; **GOV-GAP045 reload/rehydrate** **Closed**; product **GAP-045** **Open**; **GAP-038** **Closed** (slices 0–3). "
            "**Pointer:** `artifacts/verify/latest_pointer.json` may lag.\n"
        )
        break
else:
    raise SystemExit("CANONICAL_REGISTRY: Session State row not found")
REG.write_text("".join(lines), encoding="utf-8")

TR = ROOT / "docs/design/PROFESSIONAL_GAP_TRACKER.md"
tlines = TR.read_text(encoding="utf-8").splitlines(True)
for i, line in enumerate(tlines):
    if line.startswith("**Companion:**"):
        tlines[i] = (
            "**Companion:** [VOICESTUDIO_PROFESSIONAL_ROADMAP_V3.md](../governance/VOICESTUDIO_PROFESSIONAL_ROADMAP_V3.md) — "
            "**Last tracker sync:** 2026-04-05 — **Authoritative:** [.cursor/STATE.md](../.cursor/STATE.md) **ACTIVE WINDOW** + **Tracker Addendum** (next line). "
            "**Latest fingerprint:** App.Tests **3082**/skipped **274**; rolling **20260405-071523**; Quick **20260405_070100**; "
            "GAP-038 **Closed** (slices 0–3); GAP-045 bounded persistence + reload/rehydrate **Closed**; product **GAP-045** **Open**. "
            "**Historical closure mega-chain:** [STATE_MILESTONE_SNAPSHOT_2026-04-05.md](../archive/STATE_MILESTONE_SNAPSHOT_2026-04-05.md).\n"
        )
        break
else:
    raise SystemExit("PROFESSIONAL_GAP_TRACKER: Companion line not found")
TR.write_text("".join(tlines), encoding="utf-8")
print("OK: CANONICAL_REGISTRY + PROFESSIONAL_GAP_TRACKER headers normalized")
