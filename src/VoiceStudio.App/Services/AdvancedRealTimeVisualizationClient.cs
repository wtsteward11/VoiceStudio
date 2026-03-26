using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Advanced Real-Time Visualization API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class AdvancedRealTimeVisualizationClient : IAdvancedRealTimeVisualizationClient
  {
    private readonly IBackendClient _backend;

    public AdvancedRealTimeVisualizationClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<Dictionary<string, object>?> GetVisualizationDataAsync(string visualizationType, double updateRate, CancellationToken cancellationToken = default)
    {
      var request = new
      {
        visualization_type = (visualizationType ?? string.Empty).ToLower(),
        update_rate = updateRate
      };
      return _backend.SendRequestAsync<object, Dictionary<string, object>>(
        "/api/visualization/get-data",
        request,
        cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TimeSpan> GetPlaybackPositionAsync(CancellationToken cancellationToken = default)
    {
      var response = await _backend.SendRequestAsync<object, Dictionary<string, object>>(
        "/api/audio/playback-position",
        new { },
        cancellationToken: cancellationToken).ConfigureAwait(false);

      if (response != null && response.TryGetValue("position_seconds", out var posObj) && posObj != null &&
          double.TryParse(posObj.ToString(), out var seconds))
      {
        return TimeSpan.FromSeconds(seconds);
      }

      return TimeSpan.Zero;
    }
  }
}
