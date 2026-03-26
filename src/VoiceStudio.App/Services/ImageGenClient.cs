using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/image (generate, upscale). Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class ImageGenClient : IImageGenClient
  {
    private readonly IBackendClient _backend;

    public ImageGenClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<ImageGenerateResponse?> GenerateAsync(
      ImageGenerateRequest request,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<ImageGenerateRequest, ImageGenerateResponse>(
        "/api/image/generate",
        request,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<ImageUpscaleResponse?> UpscaleAsync(
      ImageUpscaleRequest request,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<ImageUpscaleRequest, ImageUpscaleResponse>(
        "/api/image/upscale",
        request,
        HttpMethod.Post,
        cancellationToken);
    }
  }
}
