using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/advanced-settings and /api/gpu-status/devices.
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class AdvancedSettingsClient : IAdvancedSettingsClient
  {
    private readonly IBackendClient _backend;

    public AdvancedSettingsClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<AdvancedSettingsData?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, AdvancedSettingsData>(
        "/api/advanced-settings",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<GpuDeviceInfo>> GetGpuDevicesAsync(CancellationToken cancellationToken = default)
    {
      var result = await _backend.SendRequestAsync<object, List<GpuDeviceInfo>>(
        "/api/gpu-status/devices",
        null,
        HttpMethod.Get,
        cancellationToken).ConfigureAwait(false);
      return result ?? new List<GpuDeviceInfo>();
    }

    /// <inheritdoc />
    public Task SaveSettingsAsync(object settings, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, object>(
        "/api/advanced-settings",
        settings,
        HttpMethod.Put,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task ResetSettingsAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, object>(
        "/api/advanced-settings/reset",
        null,
        HttpMethod.Post,
        cancellationToken);
    }
  }
}
