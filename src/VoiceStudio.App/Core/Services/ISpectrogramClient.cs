using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpectrogramData = VoiceStudio.App.ViewModels.SpectrogramData;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for /api/spectrogram (data, config, export, color-schemes).
  /// Use instead of IBackendClient for spectrogram panel.
  /// </summary>
  public interface ISpectrogramClient
  {
    /// <summary>
    /// Gets spectrogram data for an audio file with optional query parameters.
    /// </summary>
    Task<SpectrogramData?> GetSpectrogramDataAsync(
      string audioId,
      SpectrogramDataRequest request,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates spectrogram configuration for an audio file.
    /// </summary>
    Task UpdateConfigAsync(
      string audioId,
      SpectrogramConfigRequest config,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports spectrogram as an image.
    /// </summary>
    Task<SpectrogramExportResponse?> ExportSpectrogramAsync(
      string audioId,
      string format = "png",
      int width = 1920,
      int height = 1080,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available color schemes.
    /// </summary>
    Task<SpectrogramColorSchemesResponse?> GetColorSchemesAsync(
      CancellationToken cancellationToken = default);
  }

  /// <summary>
  /// Request parameters for spectrogram data.
  /// </summary>
  public sealed class SpectrogramDataRequest
  {
    public int WindowSize { get; set; } = 2048;
    public int HopLength { get; set; } = 512;
    public int NFft { get; set; } = 2048;
    public double? FrequencyMin { get; set; }
    public double? FrequencyMax { get; set; }
    public double? TimeStart { get; set; }
    public double? TimeEnd { get; set; }
    public bool LogScale { get; set; } = true;
  }

  /// <summary>
  /// Request body for spectrogram config update.
  /// </summary>
  public sealed class SpectrogramConfigRequest
  {
    public string AudioId { get; set; } = string.Empty;
    public int WindowSize { get; set; }
    public int HopLength { get; set; }
    public int NFft { get; set; }
    public SpectrogramRange? FrequencyRange { get; set; }
    public SpectrogramRange? TimeRange { get; set; }
    public string ColorScheme { get; set; } = "viridis";
    public bool ShowPhase { get; set; }
    public bool ShowMagnitude { get; set; } = true;
    public bool LogScale { get; set; } = true;
  }

  /// <summary>
  /// Min/max range for frequency or time.
  /// </summary>
  public sealed class SpectrogramRange
  {
    public double Min { get; set; }
    public double Max { get; set; }
  }

  /// <summary>
  /// Response from spectrogram export.
  /// </summary>
  public sealed class SpectrogramExportResponse
  {
    public string AudioId { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
  }

  /// <summary>
  /// Response from color-schemes endpoint.
  /// </summary>
  public sealed class SpectrogramColorSchemesResponse
  {
    public List<SpectrogramColorSchemeDto> Schemes { get; set; } = new();
  }

  /// <summary>
  /// Color scheme DTO from API.
  /// </summary>
  public sealed class SpectrogramColorSchemeDto
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
  }
}
