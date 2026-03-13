using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for emotion control API (/api/emotion).
  /// Use instead of IBackendClient for list, apply-extended, preview, and preset CRUD.
  /// </summary>
  public interface IEmotionControlClient
  {
    Task<string[]> GetAvailableEmotionsAsync(CancellationToken ct = default);
    Task ApplyEmotionAsync(EmotionApplyExtendedRequest request, CancellationToken ct = default);
    Task<EmotionPreviewResponse?> PreviewEmotionAsync(EmotionApplyExtendedRequest request, CancellationToken ct = default);
    Task<EmotionPreset[]> GetPresetsAsync(CancellationToken ct = default);
    Task<EmotionPreset> CreatePresetAsync(EmotionPresetCreateRequest request, CancellationToken ct = default);
    Task DeletePresetAsync(string presetId, CancellationToken ct = default);
  }
}
