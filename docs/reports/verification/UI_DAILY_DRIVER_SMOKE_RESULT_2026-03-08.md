# Daily-Driver UI Smoke Result

**Date:** 2026-03-08  
**Commit:** `86c07e1f8dba72f264d104930de32978b7ffc8a0`  
**Build Config:** Debug, x64

---

## Automated Coverage

Steps 1-10 and 13-17 are covered by `--ui-self-test` (Gate C smoke runner).
See `.buildlogs/verify/ui_self_test.json` for latest automated results.

Steps marked MANUAL below require human verification:
- Steps 11-12 (Tool Catalog drag/drop, pin toggle)
- Steps 18-23 (Splitter persistence, workspace rename, restart restore)

---

## Result Template

Reference: [UI_DAILY_DRIVER_SMOKE.md](../../governance/UI_DAILY_DRIVER_SMOKE.md)

| # | Action | Expected Outcome | Result | Notes |
|---|--------|------------------|--------|-------|
| 1 | Launch app | Default workspace loads; no crash. | AUTO | Covered by --ui-self-test |
| 2 | Click NavRail Studio | Center panel shows Timeline. | AUTO | Covered by --ui-self-test |
| 3 | Click NavRail Library | Left panel shows Library. | AUTO | Covered by --ui-self-test |
| 4 | Click NavRail Profiles | Left panel shows Profiles. | AUTO | Covered by --ui-self-test |
| 5 | Click NavRail Effects | Right panel shows EffectsMixer. | AUTO | Covered by --ui-self-test |
| 6 | Click NavRail Settings | Right panel shows Settings. | AUTO | Covered by --ui-self-test |
| 7 | View > Studio (Ctrl+1) | Center panel shows Timeline. | AUTO | Covered by --ui-self-test |
| 8 | View > Library (Ctrl+2) | Left panel shows Library. | AUTO | Covered by --ui-self-test |
| 9 | View > Profiles (Ctrl+3) | Left panel shows Profiles. | AUTO | Covered by --ui-self-test |
| 10 | View > Effects (Ctrl+4) | Right panel shows EffectsMixer. | AUTO | Covered by --ui-self-test |
| 11 | View > Settings (Ctrl+,) | Right panel shows Settings. | MANUAL | |
| 12 | File > New Project | New project created or dialog shown. | MANUAL | |
| 13 | File > Open Project | Open dialog shown. | AUTO | Covered by --ui-self-test |
| 14 | File > Save Project | Project saved or prompt shown. | AUTO | Covered by --ui-self-test |
| 15 | File > Import Audio File... | Import dialog shown. | AUTO | Covered by --ui-self-test |
| 16 | Open Tool Catalog (Tools or region header) | Tool Catalog opens; panels listed. | AUTO | Covered by --ui-self-test |
| 17 | In Tool Catalog, select region dropdown | Region options shown; selection works. | AUTO | Covered by --ui-self-test |
| 18 | In Tool Catalog, toggle pin on a panel | Pin state toggles; persists. | MANUAL | |
| 19 | Drag panel from one region to another (swap) | Panels swap; toast: "Swapped X (region) ↔ Y (region)". | MANUAL | |
| 20 | Drag panel to empty region (move) | Panel moves; toast: "Moved X -> region". | MANUAL | |
| 21 | Drag splitter between regions | Splitter moves; layout updates. | MANUAL | |
| 22 | Switch workspace, then switch back | Splitter positions persist. | MANUAL | |
| 23 | Rename workspace, switch away, switch back | Renamed workspace loads; layout intact. | MANUAL | |

---

## Execution Notes

- **Result values:** AUTO | MANUAL | PASS | FAIL
- AUTO = covered by `--ui-self-test`; see `.buildlogs/verify/ui_self_test.json`.
- MANUAL = requires human verification.
- Fill in Result and Notes columns for MANUAL steps during manual UI run.
- Update Commit hash if run against a different build.
