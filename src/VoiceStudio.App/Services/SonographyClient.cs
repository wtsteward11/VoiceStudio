using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for sonography (waterfall/3D spectrogram) visualization.
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class SonographyClient : ISonographyClient
  {
    private readonly IBackendClient _backend;

    public SonographyClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public async Task<SonographyData?> GenerateAsync(
      string audioId,
      double timeWindow,
      double overlap,
      int frequencyResolution,
      int timeResolution,
      string colorScheme,
      string perspective,
      CancellationToken cancellationToken = default)
    {
      var request = new
      {
        audio_id = audioId,
        time_window = timeWindow,
        overlap = overlap,
        frequency_resolution = frequencyResolution,
        time_resolution = timeResolution,
        color_scheme = colorScheme,
        perspective = perspective
      };
      return await _backend.SendRequestAsync<object, SonographyData>(
        "/api/sonography/generate",
        request,
        HttpMethod.Post,
        cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<SonographyPerspectivesResponse?> GetPerspectivesAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, SonographyPerspectivesResponse>(
        "/api/sonography/perspectives",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<SonographyColorSchemesResponse?> GetColorSchemesAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, SonographyColorSchemesResponse>(
        "/api/sonography/color-schemes",
        null,
        HttpMethod.Get,
        cancellationToken);
    }
  }
}
