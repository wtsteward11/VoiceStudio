# Panel Settings Catalog

> **Single Source of Truth** for panel settings keys, scope, defaults, and validation.
>
> **Last Updated**: 2026-03-06  
> **Status**: Active

## Overview

All persisted settings must declare scope:
- **Global**: `ISettingsService` — app-wide preferences (backend + local storage)
- **Workspace**: `PanelStateService` / `IPanelStatePersistable` — per-workspace state
- **Local**: In-memory only — transient UI state, not persisted

## Settings Service APIs

| Service | Scope | Usage |
|---------|-------|-------|
| `ISettingsService` | Global | `LoadSettingsAsync`, `SaveSettingsAsync`, `LoadCategoryAsync`, `UpdateCategoryAsync` |
| `PanelStateService` | Workspace | Workspace profiles, layout, panel state via `IPanelStatePersistable` |
| `PanelSettingsStore` | Global (per-panel) | `GetSettings<T>(panelId)`, `SaveSettings<T>(panelId, settings)` — local JSON file |

## Settings Categories (ISettingsService — Global)

`SettingsData` categories from `SettingsService.cs`:

| Category | Type | Default | Validation |
|----------|------|---------|------------|
| `General` | GeneralSettings | — | — |
| `Engine` | EngineSettings | — | — |
| `Audio` | AudioSettings | — | — |
| `Timeline` | TimelineSettings | — | — |
| `Backend` | BackendSettings | — | — |
| `Performance` | PerformanceSettings | — | — |
| `Plugins` | PluginSettings | — | — |
| `Mcp` | McpSettings | — | — |
| `Privacy` | — | — | — |
| `System` | — | — | — |
| `Diagnostics` | DiagnosticsSettings | — | — |

API paths: `/api/settings`, `/api/settings/{category}`, `/api/settings/reset`, `/api/settings/check/dependencies`

## Panel State (Workspace — IPanelStatePersistable)

Panels that implement `IPanelStatePersistable` persist custom state via `PanelStateService` when switching workspaces.

### Library (LibraryViewModel)

| Key | Scope | Type | Default | Validation |
|-----|-------|------|---------|------------|
| `SelectedFolderId` | Workspace | string | — | Must exist in Folders |
| `SelectedAssetType` | Workspace | string | — | — |
| `ShowFolders` | Workspace | bool | true | — |

### Profiles (ProfilesViewModel)

| Key | Scope | Type | Default | Validation |
|-----|-------|------|---------|------------|
| `SelectedLanguage` | Workspace | string | — | — |
| `SelectedEmotion` | Workspace | string | — | — |
| `SelectedQualityRange` | Workspace | string | — | — |
| `ViewMode` | Workspace | string | — | — |

### Timeline (Features/Timeline/TimelineViewModel)

**Note**: `Views.Panels.TimelineViewModel` (used by TimelineView) does NOT implement `IPanelStatePersistable`. The Features/Timeline TimelineViewModel does but is a different class. **Gap**: Main Timeline panel state is not persisted.

| Key | Scope | Type | Default | Validation |
|-----|-------|------|---------|------------|
| `CurrentTime` | Workspace | double | 0 | ≥ 0 |
| `IsLooping` | Workspace | bool | false | — |
| `SnapEnabled` | Workspace | bool | true | — |
| `SnapInterval` | Workspace | double | 0.1 | > 0 |
| `SelectionStart` | Workspace | double | 0 | ≥ 0 |
| `SelectionEnd` | Workspace | double | 0 | ≥ 0 |
| `ProjectId` | Workspace | string | — | — |
| `ExpandedSections` (track) | Workspace | dict | — | — |

## PanelSettingsStore (Global — per panelId)

| PanelId | Usage | Notes |
|---------|-------|------|
| — | `GetSettings<T>(panelId)`, `SaveSettings<T>(panelId, settings)` | Used by panel-specific config; keys are panel IDs |

**Conflict risk**: If a panel uses both `SettingsService` (category) and `PanelSettingsStore` (panelId) for overlapping concepts, scope is ambiguous. Document any such overlap.

## SettingsViewModel / SettingsView

| Access | Scope | Notes |
|--------|-------|-------|
| `ISettingsService.LoadSettingsAsync` | Global | Loads full SettingsData |
| `ISettingsService.SaveSettingsAsync` | Global | Saves full SettingsData |
| Category buttons | — | General, Engine, Audio, Timeline, Backend, Performance, Plugins, MCP, Privacy, System |

## AdvancedSettingsViewModel

| Access | Scope | Notes |
|--------|-------|-------|
| `/api/gpu-status/devices` | — | — |
| `/api/advanced-settings` | Global | GET/POST |
| `/api/advanced-settings/reset` | Global | — |

## TimelineViewModel (Views.Panels) — Preview Settings

| Key | Scope | Type | Default | Notes |
|-----|-------|------|---------|-------|
| `_previewEnabled` | Local | bool | true | In-memory |
| `_previewDuration` | Local | double | 0.15 | In-memory |
| `_previewVolume` | Local | double | 0.6 | In-memory |
| `LoadPreviewSettingsAsync` | — | — | — | May use ISettingsService; verify |

## Identified Gaps

| Gap | Panel | Issue |
|-----|-------|-------|
| GAP-S1 | Timeline | Views.Panels.TimelineViewModel does NOT implement IPanelStatePersistable — timeline state not persisted on workspace switch |
| GAP-S2 | Multiple | No explicit catalog of which panels use SettingsService categories; audit needed |
| GAP-S3 | PanelSettingsStore | Panel IDs and keys not enumerated; audit needed |
| GAP-S4 | Profile selection | Last selected profile for Synthesis — should be Workspace (PanelStateService) |

## Scope Rules

1. **Global**: User preferences that apply across all workspaces (e.g., default engine, auto-play on synthesis).
2. **Workspace**: State that depends on the active workspace (e.g., last selected profile, folder, project).
3. **Local**: Transient UI state (e.g., scroll position, hover state) — never persisted.

## Validation

- CI test must fail if a ViewModel accesses `SettingsService` category or `PanelSettingsStore` key not declared here.
- CI test must fail if a key is used with wrong scope (e.g., Global key used as Workspace).
