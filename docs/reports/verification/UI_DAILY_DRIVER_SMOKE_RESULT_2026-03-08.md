# Daily-Driver UI Smoke Result

**Date:** 2026-03-08  
**Commit:** `86c07e1f8dba72f264d104930de32978b7ffc8a0`  
**Build Config:** Debug, x64

---

## Result Template

Reference: [UI_DAILY_DRIVER_SMOKE.md](../../governance/UI_DAILY_DRIVER_SMOKE.md)

| # | Action | Expected Outcome | Result | Notes |
|---|--------|------------------|--------|-------|
| 1 | Launch app | Default workspace loads; no crash. | | |
| 2 | Click NavRail Studio | Center panel shows Timeline. | | |
| 3 | Click NavRail Library | Left panel shows Library. | | |
| 4 | Click NavRail Profiles | Left panel shows Profiles. | | |
| 5 | Click NavRail Effects | Right panel shows EffectsMixer. | | |
| 6 | Click NavRail Settings | Right panel shows Settings. | | |
| 7 | View > Studio (Ctrl+1) | Center panel shows Timeline. | | |
| 8 | View > Library (Ctrl+2) | Left panel shows Library. | | |
| 9 | View > Profiles (Ctrl+3) | Left panel shows Profiles. | | |
| 10 | View > Effects (Ctrl+4) | Right panel shows EffectsMixer. | | |
| 11 | View > Settings (Ctrl+,) | Right panel shows Settings. | | |
| 12 | File > New Project | New project created or dialog shown. | | |
| 13 | File > Open Project | Open dialog shown. | | |
| 14 | File > Save Project | Project saved or prompt shown. | | |
| 15 | File > Import Audio File... | Import dialog shown. | | |
| 16 | Open Tool Catalog (Tools or region header) | Tool Catalog opens; panels listed. | | |
| 17 | In Tool Catalog, select region dropdown | Region options shown; selection works. | | |
| 18 | In Tool Catalog, toggle pin on a panel | Pin state toggles; persists. | | |
| 19 | Drag panel from one region to another (swap) | Panels swap; toast: "Swapped X (region) ↔ Y (region)". | | |
| 20 | Drag panel to empty region (move) | Panel moves; toast: "Moved X -> region". | | |
| 21 | Drag splitter between regions | Splitter moves; layout updates. | | |
| 22 | Switch workspace, then switch back | Splitter positions persist. | | |
| 23 | Rename workspace, switch away, switch back | Renamed workspace loads; layout intact. | | |

---

## Execution Notes

- **Result values:** PASS | FAIL
- Fill in Result and Notes columns during manual UI run.
- Update Commit hash if run against a different build.
