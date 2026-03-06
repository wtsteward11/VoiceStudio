# _archived Route Classification

Classification of archived route modules per FCM-009 item 9.

**Date**: 2026-03-06.  
**Source**: Release Truth Hardening Plan, Task 9.

## Legend

| Action | Meaning |
|--------|---------|
| **delete** | Superseded by active route or purely experimental/unused; safe to remove |
| **migrate** | Unique functionality with no replacement; needs re-registration to restore |
| **keep** | Archive for reference; do not delete or migrate yet |

## Classification

| File | Prefix | Superseded By | Action | Notes |
|------|--------|---------------|--------|-------|
| adr.py | /api/adr | — | **migrate** | ADR (Automatic Dialogue Replacement) alignment; unique, no active replacement |
| deepfake_creator.py | /api/deepfake-creator | face_swap.py (/api/face-swap) | **delete** | Renamed for legal/ethical; face_swap provides equivalent functionality |
| docs.py | /api/docs | FastAPI built-in /docs | **delete** | Custom OpenAPI endpoints redundant with FastAPI Swagger |
| mix_scene.py | /api/mix/scene | — | **migrate** | Scene analysis for mix; SceneBuilder panel may need this |
| mcp_dashboard.py | /api/mcp-dashboard | — | **migrate** | MCP server management; MCPDashboardView exists |
| reward.py | /api/rm | — | **delete** | Experimental RL reward model; unused in production |
| script_editor.py | /api/script-editor | — | **migrate** | Script CRUD; ScriptEditor panel may need this |
| text_highlighting.py | /api/text-highlighting | — | **migrate** | Text highlighting sessions; TextHighlightingView exists |
| todo_panel.py | /api/todo-panel | — | **migrate** | Todo CRUD; TodoPanel is registered |
| ultimate_dashboard.py | /api/ultimate-dashboard | — | **keep** | Experimental dashboard; UltimateDashboardView exists but deprecated |

## Summary

- **delete**: 3 (deepfake_creator, docs, reward)
- **migrate**: 6 (adr, mix_scene, mcp_dashboard, script_editor, text_highlighting, todo_panel)
- **keep**: 1 (ultimate_dashboard)

## Next Steps

- **delete**: Remove files after confirming no imports reference them
- **migrate**: Re-register in route_registry.py and _include_route when ready to restore
- **keep**: No action; retain for reference

---

# contexts/ Route Classification (Task 10)

Context routes are API tag aggregators, not standalone route modules. They aggregate flat routes for OpenAPI grouping.

| File | Mounted? | Action | Notes |
|------|----------|--------|-------|
| audio.py | No | **dead scaffolding** | Not imported in route_registry or main.py |
| media.py | No | **dead scaffolding** | Not imported in route_registry or main.py |
| ml.py | No | **dead scaffolding** | Not imported in route_registry or main.py |
| platform.py | No | **dead scaffolding** | Not imported in route_registry or main.py |
| plugins.py | No | **dead scaffolding** | Not imported in route_registry or main.py |
| project.py | No | **dead scaffolding** | Not imported in route_registry or main.py |
| voice.py | No | **dead scaffolding** | Not imported in route_registry or main.py |

**Summary**: All 7 context files are dead scaffolding. They define aggregator routers but are never mounted. Individual routes are registered directly via route_registry. Consider removing or repurposing for OpenAPI tag grouping if needed.
