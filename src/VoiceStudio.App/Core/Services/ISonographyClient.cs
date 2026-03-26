using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for sonography (waterfall/3D spectrogram) visualization.
  /// Use instead of IBackendClient for sonography panel.
  /// </summary>
  public interface ISonographyClient
  {
    /// <summary>
    /// Generates sonography data for the given parameters.
    /// </summary>
    Task<SonographyData?> GenerateAsync(
      string audioId,
      double timeWindow,
      double overlap,
      int frequencyResolution,
      int timeResolution,
      string colorScheme,
      string perspective,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available perspectives.
    /// </summary>
    Task<SonographyPerspectivesResponse?> GetPerspectivesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available color schemes.
    /// </summary>
    Task<SonographyColorSchemesResponse?> GetColorSchemesAsync(CancellationToken cancellationToken = default);
  }

  /// <summary>
  /// Response for perspectives endpoint.
  /// </summary>
  public class SonographyPerspectivesResponse
  {
    public SonographyPerspectiveInfo[] Perspectives { get; set; } = System.Array.Empty<SonographyPerspectiveInfo>();
  }

  /// <summary>
  /// Perspective info.
  /// </summary>
  public class SonographyPerspectiveInfo
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
  }

  /// <summary>
  /// Response for color schemes endpoint.
  /// </summary>
  public class SonographyColorSchemesResponse
  {
    public SonographyColorSchemeInfo[] ColorSchemes { get; set; } = System.Array.Empty<SonographyColorSchemeInfo>();
  }

  /// <summary>
  /// Color scheme info.
  /// </summary>
  public class SonographyColorSchemeInfo
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
  }
}
