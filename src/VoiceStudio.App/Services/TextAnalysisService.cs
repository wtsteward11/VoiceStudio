using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Text analysis service. Delegates to IBackendClient.
  /// </summary>
  public sealed class TextAnalysisService : ITextAnalysisService
  {
    private readonly IBackendClient _backend;

    public TextAnalysisService(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<TextAnalysisResult> AnalyzeTextAsync(string text, string language, CancellationToken ct = default)
      => _backend.AnalyzeTextAsync(text, language, ct);

    /// <inheritdoc />
    public Task<QualityRecommendation> GetQualityRecommendationAsync(string text, string language, List<string>? engines, double? targetQuality, CancellationToken ct = default)
      => _backend.GetQualityRecommendationAsync(text, language, engines, targetQuality, ct);
  }
}
