using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for /api/image (image generation and upscaling).
  /// Use instead of IBackendClient for image generation panel.
  /// </summary>
  public interface IImageGenClient
  {
    /// <summary>
    /// Generates an image from a text prompt.
    /// </summary>
    Task<ImageGenerateResponse?> GenerateAsync(
      ImageGenerateRequest request,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Upscales an image by the specified scale factor.
    /// </summary>
    Task<ImageUpscaleResponse?> UpscaleAsync(
      ImageUpscaleRequest request,
      CancellationToken cancellationToken = default);
  }
}
