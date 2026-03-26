using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Advanced Spectrogram API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class AdvancedSpectrogramClient : IAdvancedSpectrogramClient
  {
    private readonly IBackendClient _backend;

    public AdvancedSpectrogramClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<AdvancedSpectrogramViewTypesResponse?> GetViewTypesAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, AdvancedSpectrogramViewTypesResponse>(
        "/api/advanced-spectrogram/view-types",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<AdvancedSpectrogramGenerateResponse?> GenerateSpectrogramAsync(
      AdvancedSpectrogramGenerateRequest request,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<AdvancedSpectrogramGenerateRequest, AdvancedSpectrogramGenerateResponse>(
        "/api/advanced-spectrogram/generate",
        request,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<AdvancedSpectrogramCompareResponse?> CompareSpectrogramsAsync(
      AdvancedSpectrogramCompareRequest request,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<AdvancedSpectrogramCompareRequest, AdvancedSpectrogramCompareResponse>(
        "/api/advanced-spectrogram/compare",
        request,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }
  }
}
