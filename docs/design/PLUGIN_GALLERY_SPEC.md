# Plugin Gallery Specification

**Status**: Implemented (v1.0.2) -- `PluginGalleryView.xaml`, `PluginGalleryViewModel.cs`, `backend/plugins/gallery/`
**Priority**: Medium
**Estimated Effort**: High
**Dependencies**: Engine manifest system, Plugin infrastructure

## 1. Executive Summary

This specification defines the Plugin Gallery feature for VoiceStudio, enabling users to discover, install, update, and manage engines and plugins from within the application. The gallery provides a curated marketplace experience while maintaining VoiceStudio's local-first and free-only principles.

## 2. Goals and Non-Goals

### 2.1 Goals

- **Discoverability** - Users can browse and search available plugins
- **One-click install** - Simple installation process with dependency handling
- **Update management** - Notify and apply plugin updates
- **Local-first** - All plugins run locally, no cloud dependency for core function
- **Free focus** - Prioritize free and open-source plugins

### 2.2 Non-Goals

- Plugin monetization or payment processing
- User-generated plugin publishing (admin-curated only)
- Cloud-hosted plugin execution
- Plugin sandboxing (trust model assumes vetted plugins)

## 3. Architecture Overview

### 3.1 System Components

```
┌─────────────────────────────────────────────────────────────────┐
│                        WinUI Frontend                            │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────────────┐  │
│  │ Plugin      │───▶│ Plugin      │───▶│ DownloadManager     │  │
│  │ Gallery UI  │    │ ViewModel   │    │ (Background)        │  │
│  └─────────────┘    └─────────────┘    └─────────────────────┘  │
│         │                  │                    │                │
│         ▼                  ▼                    ▼                │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────────────┐  │
│  │ Plugin      │    │ Version     │    │ Plugin Installer    │  │
│  │ Cards       │    │ Resolver    │    │ Service             │  │
│  └─────────────┘    └─────────────┘    └─────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                     Local Plugin Manager                         │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────────────┐  │
│  │ Manifest    │    │ Dependency  │    │ Plugin Lifecycle    │  │
│  │ Registry    │    │ Resolver    │    │ Manager             │  │
│  └─────────────┘    └─────────────┘    └─────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Remote Catalog Service                        │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────────────┐  │
│  │ catalog.json│    │ Download    │    │ Update Checker      │  │
│  │ (GitHub)    │    │ Mirrors     │    │ (Periodic)          │  │
│  └─────────────┘    └─────────────┘    └─────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 Data Flow

1. **Gallery opens** → Fetch catalog from remote (cached locally)
2. **User browses** → Display plugin cards with metadata
3. **User installs** → Download package, verify checksum, extract
4. **Plugin activates** → Register with engine/plugin system
5. **Update check** → Compare local vs remote versions periodically

## 4. Plugin Catalog Schema

### 4.1 Remote Catalog (catalog.json)

```json
{
  "catalog_version": "1.0.0",
  "last_updated": "2026-02-09T10:00:00Z",
  "plugins": [
    {
      "id": "xtts-v2",
      "name": "XTTS v2",
      "description": "Cross-lingual text-to-speech with voice cloning",
      "category": "engine",
      "subcategory": "tts",
      "author": "Coqui AI",
      "license": "MPL-2.0",
      "homepage": "https://github.com/coqui-ai/TTS",
      "icon_url": "https://example.com/icons/xtts.png",
      "tags": ["tts", "voice-cloning", "multilingual"],
      "versions": [
        {
          "version": "2.0.3",
          "release_date": "2026-01-15",
          "download_url": "https://example.com/plugins/xtts-v2-2.0.3.zip",
          "checksum_sha256": "abc123...",
          "size_bytes": 524288000,
          "min_voicestudio_version": "1.0.0",
          "dependencies": {
            "python": ">=3.10,<3.12",
            "pytorch": ">=2.0.0",
            "cuda": "optional:>=11.8"
          },
          "changelog": "- Improved voice quality\n- Faster inference"
        }
      ],
      "stats": {
        "downloads": 15420,
        "rating": 4.8,
        "reviews": 127
      },
      "featured": true,
      "verified": true
    }
  ],
  "categories": [
    {"id": "engine", "name": "Synthesis Engines", "icon": "speaker"},
    {"id": "voice-model", "name": "Voice Models", "icon": "mic"},
    {"id": "effect", "name": "Audio Effects", "icon": "waveform"},
    {"id": "tool", "name": "Tools & Utilities", "icon": "wrench"}
  ]
}
```

### 4.2 Local Plugin Manifest (plugin.json)

```json
{
  "id": "xtts-v2",
  "version": "2.0.3",
  "installed_at": "2026-02-09T14:30:00Z",
  "install_path": "plugins/engines/xtts-v2",
  "state": "enabled",
  "config": {
    "gpu_enabled": true,
    "cache_size_mb": 1024
  },
  "files": [
    {"path": "model.pth", "checksum": "..."},
    {"path": "config.json", "checksum": "..."}
  ]
}
```

## 5. UI Design

### 5.1 Gallery Main View

```
┌─────────────────────────────────────────────────────────────────┐
│ Plugin Gallery                                    [Search... 🔍] │
├─────────────────────────────────────────────────────────────────┤
│ Categories:  [All] [Engines] [Models] [Effects] [Tools]          │
│ Sort by:     [Featured ▼]                                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────────┐  ┌──────────────────┐  ┌────────────────┐ │
│  │ 🔊 XTTS v2       │  │ 🎤 RVC v2        │  │ 🎵 Piper       │ │
│  │ ★★★★★ (127)      │  │ ★★★★☆ (89)       │  │ ★★★★★ (203)    │ │
│  │                  │  │                  │  │                │ │
│  │ Voice cloning    │  │ Voice conversion │  │ Fast local TTS │ │
│  │ and multilingual │  │ with AI          │  │                │ │
│  │ synthesis        │  │                  │  │                │ │
│  │                  │  │                  │  │                │ │
│  │ [Featured] [Free]│  │ [Free]           │  │ [Featured]     │ │
│  │                  │  │                  │  │                │ │
│  │ [Install]        │  │ [Update ↑]       │  │ [Installed ✓]  │ │
│  └──────────────────┘  └──────────────────┘  └────────────────┘ │
│                                                                  │
│  ┌──────────────────┐  ┌──────────────────┐  ┌────────────────┐ │
│  │ 🎭 Bark          │  │ 🔧 Audio Toolbox │  │ 🌐 SpeechT5    │ │
│  │ ...              │  │ ...              │  │ ...            │ │
│  └──────────────────┘  └──────────────────┘  └────────────────┘ │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 5.2 Plugin Detail View

```
┌─────────────────────────────────────────────────────────────────┐
│ ← Back to Gallery                                                │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌────────┐  XTTS v2                          [Install]         │
│  │  ICON  │  by Coqui AI                                         │
│  │        │  ★★★★★ 4.8 (127 reviews)                            │
│  └────────┘  License: MPL-2.0                                    │
│                                                                  │
├─────────────────────────────────────────────────────────────────┤
│ Description                                                      │
│                                                                  │
│ Cross-lingual text-to-speech synthesis with voice cloning       │
│ capabilities. Supports 17 languages with high-quality output.   │
│                                                                  │
├─────────────────────────────────────────────────────────────────┤
│ Requirements                                                     │
│                                                                  │
│ • Python 3.10-3.11           ✓ Installed                        │
│ • PyTorch 2.0+               ✓ Installed                        │
│ • CUDA 11.8+ (optional)      ✓ Available (RTX 3080)             │
│ • Disk Space: 500 MB         ✓ Available                        │
│                                                                  │
├─────────────────────────────────────────────────────────────────┤
│ Version History                                                  │
│                                                                  │
│ v2.0.3 (2026-01-15) - Latest                                    │
│   • Improved voice quality                                       │
│   • Faster inference                                             │
│                                                                  │
│ v2.0.2 (2025-12-01)                                             │
│   • Bug fixes                                                    │
│                                                                  │
├─────────────────────────────────────────────────────────────────┤
│ Tags: tts, voice-cloning, multilingual                          │
│                                                                  │
│ [View on GitHub]  [Report Issue]  [View License]                │
└─────────────────────────────────────────────────────────────────┘
```

### 5.3 Installed Plugins View

```
┌─────────────────────────────────────────────────────────────────┐
│ Installed Plugins                                                │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Plugin              Version    Status      Actions              │
│  ─────────────────────────────────────────────────────────────  │
│  XTTS v2             2.0.3      Enabled     [Disable] [Remove]  │
│  Piper               1.4.0      Enabled     [Update ↑] [...]    │
│  RVC v2              2.1.0      Disabled    [Enable] [Remove]   │
│  Audio Toolbox       1.0.0      Enabled     [....]              │
│                                                                  │
│  ─────────────────────────────────────────────────────────────  │
│  Total: 4 plugins (3 enabled)    Disk: 2.3 GB                   │
│                                                                  │
│  [Check for Updates]  [Open Plugins Folder]                      │
└─────────────────────────────────────────────────────────────────┘
```

## 6. Backend Services

### 6.1 PluginCatalogService

```csharp
// src/VoiceStudio.App/Services/PluginCatalogService.cs

public interface IPluginCatalogService
{
    Task<PluginCatalog> GetCatalogAsync(bool forceRefresh = false);
    Task<IReadOnlyList<PluginInfo>> SearchPluginsAsync(string query);
    Task<IReadOnlyList<PluginInfo>> GetPluginsByCategoryAsync(string category);
    Task<PluginDetails> GetPluginDetailsAsync(string pluginId);
    
    event EventHandler<CatalogUpdatedEventArgs>? CatalogUpdated;
}

public class PluginCatalogService : IPluginCatalogService
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorage _localStorage;
    private const string CATALOG_URL = "https://voicestudio.github.io/plugins/catalog.json";
    private const string CATALOG_CACHE_KEY = "plugin_catalog";
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromHours(4);
    
    // ...
}
```

### 6.2 PluginInstallService

```csharp
// src/VoiceStudio.App/Services/PluginInstallService.cs

public interface IPluginInstallService
{
    Task<InstallResult> InstallPluginAsync(
        string pluginId, 
        string version,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default);
    
    Task<bool> UninstallPluginAsync(string pluginId);
    Task<UpdateCheckResult> CheckForUpdatesAsync();
    Task<bool> UpdatePluginAsync(string pluginId, string targetVersion);
    
    IReadOnlyList<InstalledPlugin> GetInstalledPlugins();
    InstalledPlugin? GetInstalledPlugin(string pluginId);
}

public class InstallProgress
{
    public InstallPhase Phase { get; init; }
    public double Progress { get; init; }  // 0-1
    public string? CurrentFile { get; init; }
    public long BytesDownloaded { get; init; }
    public long TotalBytes { get; init; }
}

public enum InstallPhase
{
    Preparing,
    Downloading,
    Verifying,
    Extracting,
    InstallingDependencies,
    Configuring,
    Activating,
    Complete
}
```

### 6.3 DependencyResolver

```python
# backend/services/plugin_dependency_resolver.py

class PluginDependencyResolver:
    """Resolves and validates plugin dependencies."""
    
    async def check_dependencies(
        self, 
        plugin_manifest: PluginManifest
    ) -> DependencyCheckResult:
        """Check if all dependencies are satisfied."""
        
        results = []
        
        for dep_name, version_spec in plugin_manifest.dependencies.items():
            status = await self._check_dependency(dep_name, version_spec)
            results.append(status)
        
        return DependencyCheckResult(
            satisfied=all(r.satisfied for r in results),
            dependencies=results
        )
    
    async def install_dependencies(
        self,
        plugin_id: str,
        dependencies: List[Dependency],
        progress_callback: Optional[Callable] = None
    ) -> bool:
        """Install missing dependencies for a plugin."""
        # Uses appropriate package managers (pip, conda, etc.)
        ...
```

## 7. Security Considerations

### 7.1 Plugin Verification

- **Checksum verification** - SHA256 hash validation for all downloads
- **Signature verification** - Optional GPG signatures for verified publishers
- **Manifest validation** - Schema validation before processing
- **Permission model** - Plugins declare required permissions

### 7.2 Network Security

- **HTTPS only** - All catalog and download URLs must be HTTPS
- **Certificate pinning** - Pin known certificates for catalog source
- **Rate limiting** - Prevent catalog abuse
- **Offline mode** - Full functionality with cached catalog

### 7.3 Runtime Security

- **No arbitrary code execution** - Plugins are pre-defined engine integrations
- **Resource limits** - Memory and CPU constraints per plugin
- **Isolation** - Plugin venvs are isolated from core application

## 8. Implementation Phases

### Phase 1: Core Infrastructure
- [ ] Plugin manifest schema and validation
- [ ] Local plugin registry (installed plugins tracking)
- [ ] Basic install/uninstall functionality
- [ ] Plugin enable/disable support

### Phase 2: Catalog Integration
- [ ] Remote catalog fetching and caching
- [ ] Catalog search and filtering
- [ ] Version comparison and update detection
- [ ] Dependency checking

### Phase 3: Gallery UI
- [ ] Plugin Gallery panel
- [ ] Plugin cards with status indicators
- [ ] Plugin detail view
- [ ] Install progress UI

### Phase 4: Advanced Features
- [ ] Automatic update checking
- [ ] Dependency resolution and installation
- [ ] Plugin configuration UI
- [ ] Usage statistics and ratings

## 9. Plugin Types

| Type | Description | Example |
|------|-------------|---------|
| `engine` | Synthesis/processing engine | XTTS, RVC, Piper |
| `voice-model` | Pre-trained voice model | Celebrity voices, accents |
| `effect` | Audio effect processor | EQ, compressor, reverb |
| `tool` | Utility or workflow tool | Batch processor, format converter |

## 10. Local-First Principles

Per VoiceStudio's architecture rules:

1. **All plugins run locally** - No cloud execution
2. **Catalog is cached** - Works offline with local cache
3. **No telemetry** - Plugin usage is not tracked
4. **Free priority** - Open-source plugins featured prominently
5. **No accounts required** - Anonymous browsing and installation

## 11. Success Metrics

| Metric | Target |
|--------|--------|
| Plugin discovery time | <2 seconds |
| Install success rate | >95% |
| Catalog load time | <500ms (cached) |
| Update check frequency | Every 4 hours |
| User satisfaction | >4.0/5.0 |

## 12. Related Documents

- [Engine Manifest System](ENGINE_MANIFEST_SYSTEM.md)
- [Plugin Architecture](ENGINE_EXTENSIBILITY.md)
- [Venv Isolation](ENGINE_VENV_ISOLATION_SPEC.md)

---

**Last Updated**: 2026-02-09
**Author**: VoiceStudio Development Team
