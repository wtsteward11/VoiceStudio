using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Spatial Audio API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class SpatialStageClient : ISpatialStageClient
  {
    private readonly IBackendClient _backend;

    public SpatialStageClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<SpatialConfigInfo[]?> GetConfigsAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, SpatialConfigInfo[]>(
        "/api/spatial-audio/configs",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<SpatialConfigInfo?> CreateConfigAsync(SpatialConfigCreateRequest request, CancellationToken cancellationToken = default)
    {
      var body = new
      {
        name = request?.Name ?? string.Empty,
        audio_id = request?.AudioId ?? string.Empty,
        x = request?.X ?? 0,
        y = request?.Y ?? 0,
        z = request?.Z ?? 0,
        distance = request?.Distance ?? 1.0,
        room_size = request?.RoomSize ?? 1.0,
        reverb_amount = request?.ReverbAmount ?? 0,
        occlusion = request?.Occlusion ?? 0,
        doppler = request?.Doppler ?? false,
        hrtf = request?.Hrtf ?? true
      };
      return _backend.SendRequestAsync<object, SpatialConfigInfo>(
        "/api/spatial-audio/configs",
        body,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<SpatialConfigInfo?> UpdateConfigAsync(string configId, SpatialConfigUpdateRequest request, CancellationToken cancellationToken = default)
    {
      var body = new
      {
        name = request?.Name,
        audio_id = request?.AudioId,
        x = request?.X,
        y = request?.Y,
        z = request?.Z,
        distance = request?.Distance,
        room_size = request?.RoomSize,
        reverb_amount = request?.ReverbAmount,
        occlusion = request?.Occlusion,
        doppler = request?.Doppler,
        hrtf = request?.Hrtf
      };
      var url = $"/api/spatial-audio/configs/{System.Uri.EscapeDataString(configId ?? "")}";
      return _backend.SendRequestAsync<object, SpatialConfigInfo>(
        url,
        body,
        System.Net.Http.HttpMethod.Put,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteConfigAsync(string configId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/spatial-audio/configs/{System.Uri.EscapeDataString(configId ?? "")}";
      return _backend.SendRequestAsync<object, object>(
        url,
        null,
        System.Net.Http.HttpMethod.Delete,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<SpatialApplyResponse?> ApplySpatialAsync(string configId, string outputFormat = "wav", CancellationToken cancellationToken = default)
    {
      var body = new { config_id = configId ?? "", output_format = outputFormat ?? "wav" };
      return _backend.SendRequestAsync<object, SpatialApplyResponse>(
        "/api/spatial-audio/apply",
        body,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<SpatialPreviewResponse?> PreviewSpatialAsync(string audioId, double x, double y, double z, double distance, CancellationToken cancellationToken = default)
    {
      var url = $"/api/spatial-audio/preview?audio_id={System.Uri.EscapeDataString(audioId ?? "")}&x={x}&y={y}&z={z}&distance={distance}";
      return _backend.SendRequestAsync<object, SpatialPreviewResponse>(
        url,
        null,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }
  }
}
