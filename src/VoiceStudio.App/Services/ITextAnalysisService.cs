using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for text analysis and quality recommendations. Delegates to IBackendClient.
  /// </summary>
  public interface ITextAnalysisService
  {
    Task<TextAnalysisResult> AnalyzeTextAsync(string text, string language, CancellationToken ct = default);
    Task<QualityRecommendation> GetQualityRecommendationAsync(string text, string language, List<string>? engines, double? targetQuality, CancellationToken ct = default);
  }
}
