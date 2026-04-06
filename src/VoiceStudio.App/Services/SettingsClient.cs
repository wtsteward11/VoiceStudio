using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for settings API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class SettingsClient : ISettingsClient
  {
    private readonly IBackendClient _backend;

    public SettingsClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<Dictionary<string, object>?> CheckDependenciesAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, Dictionary<string, object>>(
          "/api/settings/check/dependencies",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public Task<EffectiveEnginePriorityResponse?> GetEffectiveEnginePriorityAsync(
        string taskType = "tts",
        CancellationToken cancellationToken = default)
    {
      var q = string.IsNullOrWhiteSpace(taskType) ? "" : $"?task_type={System.Uri.EscapeDataString(taskType)}";
      return _backend.SendRequestAsync<object, EffectiveEnginePriorityResponse>(
          $"/api/settings/engine-priority/effective{q}",
          null,
          HttpMethod.Get,
          cancellationToken);
    }
  }
}
