using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/spectrogram (data, config, export, color-schemes). Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class SpectrogramClient : ISpectrogramClient
  {
    private readonly IBackendClient _backend;

    public SpectrogramClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public async Task<SpectrogramData?> GetSpectrogramDataAsync(
      string audioId,
      SpectrogramDataRequest request,
      CancellationToken cancellationToken = default)
    {
      var queryParams = new NameValueCollection();
      queryParams.Add("window_size", request.WindowSize.ToString());
      queryParams.Add("hop_length", request.HopLength.ToString());
      queryParams.Add("n_fft", request.NFft.ToString());
      if (request.FrequencyMin.HasValue)
        queryParams.Add("frequency_min", request.FrequencyMin.Value.ToString());
      if (request.FrequencyMax.HasValue)
        queryParams.Add("frequency_max", request.FrequencyMax.Value.ToString());
      if (request.TimeStart.HasValue)
        queryParams.Add("time_start", request.TimeStart.Value.ToString());
      if (request.TimeEnd.HasValue)
        queryParams.Add("time_end", request.TimeEnd.Value.ToString());
      queryParams.Add("log_scale", request.LogScale.ToString().ToLowerInvariant());

      var queryString = string.Join("&",
        queryParams.AllKeys.SelectMany(key =>
          queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
        )
      );

      var url = $"/api/spectrogram/data/{Uri.EscapeDataString(audioId)}";
      if (!string.IsNullOrEmpty(queryString))
        url += $"?{queryString}";

      return await _backend.SendRequestAsync<object, SpectrogramData>(
        url,
        null,
        HttpMethod.Get,
        cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task UpdateConfigAsync(
      string audioId,
      SpectrogramConfigRequest config,
      CancellationToken cancellationToken = default)
    {
      var body = new
      {
        audio_id = config.AudioId,
        window_size = config.WindowSize,
        hop_length = config.HopLength,
        n_fft = config.NFft,
        frequency_range = config.FrequencyRange != null ? new { min = config.FrequencyRange.Min, max = config.FrequencyRange.Max } : (object?)null,
        time_range = config.TimeRange != null ? new { start = config.TimeRange.Min, end = config.TimeRange.Max } : (object?)null,
        color_scheme = config.ColorScheme,
        show_phase = config.ShowPhase,
        show_magnitude = config.ShowMagnitude,
        log_scale = config.LogScale
      };

      return _backend.SendRequestAsync<object, object>(
        $"/api/spectrogram/config/{Uri.EscapeDataString(audioId)}",
        body,
        HttpMethod.Put,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<SpectrogramExportResponse?> ExportSpectrogramAsync(
      string audioId,
      string format = "png",
      int width = 1920,
      int height = 1080,
      CancellationToken cancellationToken = default)
    {
      var url = $"/api/spectrogram/export/{Uri.EscapeDataString(audioId)}?format={Uri.EscapeDataString(format)}&width={width}&height={height}";
      return _backend.SendRequestAsync<object, SpectrogramExportResponse>(
        url,
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<SpectrogramColorSchemesResponse?> GetColorSchemesAsync(
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, SpectrogramColorSchemesResponse>(
        "/api/spectrogram/color-schemes",
        null,
        HttpMethod.Get,
        cancellationToken);
    }
  }
}
