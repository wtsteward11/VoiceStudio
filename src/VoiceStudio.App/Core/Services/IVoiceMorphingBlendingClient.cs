using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for voice morphing/blending API (/api/voice-morph/voice/preview, blend, morph).
  /// Use instead of IBackendClient for VoiceMorphingBlending panel.
  /// </summary>
  public interface IVoiceMorphingBlendingClient
  {
    Task<VoiceMorphingBlendingViewModel.VoicePreviewResponse?> PreviewBlendAsync(
        string voiceAId,
        string voiceBId,
        float blendRatio,
        string text,
        CancellationToken cancellationToken = default);

    Task<VoiceMorphingBlendingViewModel.VoiceBlendResponse?> BlendVoicesAsync(
        string voiceAId,
        string voiceBId,
        float blendRatio,
        string? text,
        bool saveProfile,
        CancellationToken cancellationToken = default);

    Task<VoiceMorphingBlendingViewModel.VoiceMorphResponse?> MorphVoiceAsync(
        string sourceAudioId,
        string voiceAId,
        string voiceBId,
        float startRatio,
        float endRatio,
        float morphSpeed,
        CancellationToken cancellationToken = default);
  }
}
