using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/spatial-audio. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class SpatialAudioClient : ISpatialAudioClient
  {
    private readonly IBackendClient _backend;

    public SpatialAudioClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<SpatialConfigResponse?> SetPositionAsync(
      SpatialPositionRequest request,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<SpatialPositionRequest, SpatialConfigResponse>(
        "/api/spatial-audio/position",
        request,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<Dictionary<string, object>?> ConfigureEnvironmentAsync(
      SpatialEnvironmentRequest request,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<SpatialEnvironmentRequest, Dictionary<string, object>>(
        "/api/spatial-audio/environment",
        request,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<SpatialProcessResponse?> ProcessAudioAsync(
      SpatialProcessRequest request,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<SpatialProcessRequest, SpatialProcessResponse>(
        "/api/spatial-audio/process",
        request,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<Dictionary<string, object>?> PreviewAsync(
      string audioId,
      float x,
      float y,
      float z,
      float distance,
      CancellationToken cancellationToken = default)
    {
      var url = $"/api/spatial-audio/preview?audio_id={Uri.EscapeDataString(audioId)}&x={x}&y={y}&z={z}&distance={distance}";
      return _backend.SendRequestAsync<object, Dictionary<string, object>>(
        url,
        null,
        HttpMethod.Post,
        cancellationToken);
    }
  }
}
