# VoiceStudio Plugin Loader Ownership and Architecture

**Last Updated**: 2026-02-24
**Phase**: Phase 4A -- Plugin System Foundation (integration/reintroduce-v1.0.2)

## Problem Statement

Three classes named `PluginLoader` or similar exist in this codebase.
They serve different layers and are NOT interchangeable.

## Canonical Hierarchy

### 1. `backend.api.plugins.loader.PluginLoader` (FastAPI Layer)
- **Responsibility**: Discovers plugins in `plugins/` directory by scanning for `manifest.json`.
  Calls each plugin's entry point function with the FastAPI `app` instance to register routes.
- **Called by**: `main.py` startup, `load_all_plugins(app)` function.
- **Plugin API**: Expects `register(app: FastAPI, plugin_dir: Path) -> Any` entry point.
- **Not responsible for**: Lifecycle management, state tracking, hot-reload.

### 2. `backend.plugins.core.loader.PluginLoader` (DEPRECATED since v1.3.0)
- **Responsibility**: Low-level Plugin ABC lifecycle: discover -> load -> initialize.
- **Status**: DEPRECATED. Emits DeprecationWarning on import. Will be removed in v1.5.0.
- **Replacement**: `backend.plugins.registry.registry.PluginRegistry`.
- **ADR Reference**: ADR-038.

### 3. `backend.plugins.registry.registry.PluginRegistry` (Unified Registry)
- **Responsibility**: Central registry wrapping the deprecated PluginLoader. Manages
  the full plugin lifecycle including discover, load, activate, deactivate, unload,
  and capability-based lookup. Fires hooks on lifecycle events.
- **Singleton**: `get_plugin_registry()` function.
- **Depends on**: `backend.plugins.core.loader.PluginLoader` (transitional dependency).

### 4. `backend.plugins.plugin_service.PluginService` (Application Service)
- **Responsibility**: High-level service for application-facing plugin management.
  Wraps PluginRegistry, integrates wasm execution, signature verification, hot-reload,
  settings persistence, and Phase 6 integrations (AI quality, compliance, ecosystem).
- **Singleton**: `get_plugin_service()` function (after initialization).
- **Called by**: Routes (`/api/plugins/*`), startup task.

## Canonical Plugin Base Class

### `app.core.plugins_api.Plugin` (Unified ABC -- Phase 4+)
- All new plugins must inherit from this class.
- Type-specific capabilities via mixins: `EngineMixin`, `ProcessorMixin`,
  `ExporterMixin`, `ImporterMixin`, `UIPanelMixin`.

### Deprecated Base Classes (remove in v1.5.0)
- `backend.plugins.core.base.Plugin` (deprecated, see ADR-038)
- `backend.plugins.plugin_service.PluginBase` (deprecated, see ADR-038)

## Loading Order at Startup

1. `main.py lifespan`: calls `load_all_plugins(app)` (FastAPI layer loader)
   -- discovers plugins with `backend_routes=true` in manifest.json
   -- calls each plugin's `register(app, plugin_dir)` function

2. `StartupService LOADING_ENGINES`: runs plugin_startup.py task
   -- calls `PluginService.initialize()`
   -- discovers ALL plugins (including non-route plugins)
   -- runs Phase 6 integrations (code review, compliance scan)

## Rules

1. NEVER import `backend.plugins.core.loader.PluginLoader` in new code.
   Use `PluginRegistry` or `PluginService` instead.
2. NEVER define new plugin classes inheriting from any deprecated base.
   Use `app.core.plugins_api.Plugin` only.
3. The FastAPI-layer loader (`backend.api.plugins.loader`) does NOT
   manage lifecycle. It is a one-shot route registration mechanism.
