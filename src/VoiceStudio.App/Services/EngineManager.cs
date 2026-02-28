using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services.Engines;
using VoiceStudio.Core.Engines;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Manages the lifecycle and discovery of engines.
  /// Acts as the central registry for both remote (backend) and local engines.
  /// </summary>
  public class EngineManager
  {
    private readonly IBackendClient _backendClient;
    private readonly ConcurrentDictionary<string, IEngine> _engines = new();
    private bool _isInitialized;

    public EngineManager(IBackendClient backendClient)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
    }

    /// <summary>
    /// Discovers and initializes available engines from the backend.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
      if (_isInitialized) return;

      try
      {
        // Fetch list of engines from backend
        // GET /api/engines/list
        var response = await _backendClient.SendRequestAsync<object, EnginesListResponse>(
            "/api/engines/list",
            null,
            HttpMethod.Get,
            cancellationToken);

        if (response?.Engines != null)
        {
          foreach (var engineId in response.Engines)
          {
            if (!_engines.ContainsKey(engineId))
            {
              var engine = CreateAdapterForEngine(engineId);
              _engines.TryAdd(engineId, engine);
            }
          }
        }

        _isInitialized = true;
      }
      catch (Exception ex)
      {
        // Log error but allow partial initialization
        System.Diagnostics.Debug.WriteLine($"Error initializing EngineManager: {ex.Message}");
        // In a real scenario, we might want to retry or expose the error state
      }
    }

    /// <summary>
    /// Creates the appropriate adapter based on engine ID heuristics.
    /// In the future, this should use a metadata endpoint to determine capabilities.
    /// </summary>
    private IEngine CreateAdapterForEngine(string engineId)
    {
      var id = engineId.ToLowerInvariant();
      var capabilities = EngineCapabilities.None;
      var name = engineId;

      // Heuristics for capabilities based on known engine names
      if (id.Contains("tts") || id.Contains("coqui") || id.Contains("tortoise") || id.Contains("chatterbox"))
      {
        capabilities |= EngineCapabilities.TextToSpeech;
        capabilities |= EngineCapabilities.VoiceCloning; // Most TTS engines here support cloning
      }

      if (id.Contains("whisper") || id.Contains("vosk"))
      {
        capabilities |= EngineCapabilities.Transcription;
      }

      // Default to TTS if unknown, for now (or None)
      if (capabilities == EngineCapabilities.None)
      {
        capabilities = EngineCapabilities.TextToSpeech; // Fallback assumption for now
      }

      // Format display name
      if (id == "xtts_v2") name = "Coqui XTTS v2";
      else if (id == "tortoise") name = "Tortoise TTS";
      else if (id == "whisper_large_v3") name = "Whisper Large v3";

      return new BackendEngineAdapter(_backendClient, engineId, name, capabilities);
    }

    public IEnumerable<IEngine> GetEngines()
    {
      return _engines.Values;
    }

    public IEngine? GetEngine(string id)
    {
      _engines.TryGetValue(id, out var engine);
      return engine;
    }

    public IEnumerable<T> GetEngines<T>() where T : IEngine
    {
      return _engines.Values.OfType<T>();
    }

    /// <summary>
    /// Registers a manual engine instance (e.g. from a local plugin).
    /// </summary>
    public void RegisterEngine(IEngine engine)
    {
      _engines.TryAdd(engine.Id, engine);
    }
  }
}