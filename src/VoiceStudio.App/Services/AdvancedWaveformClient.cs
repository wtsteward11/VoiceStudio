using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Advanced Waveform API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class AdvancedWaveformClient : IAdvancedWaveformClient
  {
    private readonly IBackendClient _backend;

    public AdvancedWaveformClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<AdvancedWaveformData?> GetWaveformDataAsync(
      string audioId,
      double? zoomLevel = null,
      double? timeStart = null,
      double? timeEnd = null,
      CancellationToken cancellationToken = default)
    {
      var queryParams = new System.Collections.Specialized.NameValueCollection();
      if (zoomLevel.HasValue && zoomLevel.Value != 1.0)
        queryParams.Add("zoom_level", zoomLevel.Value.ToString());
      if (timeStart.HasValue)
        queryParams.Add("time_start", timeStart.Value.ToString());
      if (timeEnd.HasValue)
        queryParams.Add("time_end", timeEnd.Value.ToString());

      var queryString = string.Join("&",
        queryParams.AllKeys.SelectMany(key =>
          queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
        )
      );

      var url = $"/api/waveform/data/{Uri.EscapeDataString(audioId ?? "")}";
      if (!string.IsNullOrEmpty(queryString))
        url += $"?{queryString}";

      return _backend.SendRequestAsync<object, AdvancedWaveformData>(
        url,
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<AdvancedWaveformConfigResponse?> UpdateConfigAsync(
      string audioId,
      AdvancedWaveformConfigRequest request,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<AdvancedWaveformConfigRequest, AdvancedWaveformConfigResponse>(
        $"/api/waveform/config/{Uri.EscapeDataString(audioId ?? "")}",
        request,
        System.Net.Http.HttpMethod.Put,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<AdvancedWaveformAnalysis?> GetAnalysisAsync(
      string audioId,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, AdvancedWaveformAnalysis>(
        $"/api/waveform/analysis/{Uri.EscapeDataString(audioId ?? "")}",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }
  }
}
