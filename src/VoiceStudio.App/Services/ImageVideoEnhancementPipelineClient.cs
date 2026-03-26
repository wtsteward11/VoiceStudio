using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Image/Video Enhancement Pipeline API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class ImageVideoEnhancementPipelineClient : IImageVideoEnhancementPipelineClient
  {
    private readonly IBackendClient _backend;

    public ImageVideoEnhancementPipelineClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task ApplyPipelineAsync(
      string contentType,
      string filePath,
      IReadOnlyList<string> steps,
      IReadOnlyDictionary<string, Dictionary<string, object>> parameters,
      bool batchMode,
      CancellationToken cancellationToken = default)
    {
      var request = new
      {
        content_type = (contentType ?? string.Empty).ToLower(),
        file_path = filePath,
        steps = steps?.ToList() ?? new List<string>(),
        parameters = parameters?.ToDictionary(k => k.Key, v => (object)(v.Value ?? new Dictionary<string, object>())),
        batch_mode = batchMode
      };
      return _backend.SendRequestAsync<object, object>("/api/enhancement/apply-pipeline", request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Dictionary<string, object>?> PreviewPipelineAsync(
      string contentType,
      string filePath,
      IReadOnlyList<string> steps,
      IReadOnlyDictionary<string, Dictionary<string, object>> parameters,
      CancellationToken cancellationToken = default)
    {
      var request = new
      {
        content_type = (contentType ?? string.Empty).ToLower(),
        file_path = filePath,
        steps = steps?.ToList() ?? new List<string>(),
        parameters = parameters?.ToDictionary(k => k.Key, v => (object)(v.Value ?? new Dictionary<string, object>())),
        preview = true
      };
      return _backend.SendRequestAsync<object, Dictionary<string, object>>("/api/enhancement/preview-pipeline", request, System.Net.Http.HttpMethod.Post, cancellationToken);
    }
  }
}
