using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Image/Video Enhancement Pipeline API (/api/enhancement).
  /// Use instead of IBackendClient for ImageVideoEnhancementPipeline panel.
  /// </summary>
  public interface IImageVideoEnhancementPipelineClient
  {
    Task ApplyPipelineAsync(
      string contentType,
      string filePath,
      IReadOnlyList<string> steps,
      IReadOnlyDictionary<string, Dictionary<string, object>> parameters,
      bool batchMode,
      CancellationToken cancellationToken = default);

    Task<Dictionary<string, object>?> PreviewPipelineAsync(
      string contentType,
      string filePath,
      IReadOnlyList<string> steps,
      IReadOnlyDictionary<string, Dictionary<string, object>> parameters,
      CancellationToken cancellationToken = default);
  }
}
