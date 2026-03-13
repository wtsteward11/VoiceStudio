using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for emotion/style control API (/api/emotion-style).
  /// Use instead of IBackendClient for emotion presets, style presets, and apply.
  /// </summary>
  public interface IEmotionStyleClient
  {
    Task<EmotionStyleEmotionPreset[]> GetEmotionPresetsAsync(CancellationToken ct = default);
    Task<EmotionStyleStylePreset[]> GetStylePresetsAsync(CancellationToken ct = default);
    Task<EmotionStyleApplyResponse> ApplyEmotionStyleAsync(EmotionStyleApplyRequest request, CancellationToken ct = default);
  }
}
