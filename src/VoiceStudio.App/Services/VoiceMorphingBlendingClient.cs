using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for voice morphing/blending API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class VoiceMorphingBlendingClient : IVoiceMorphingBlendingClient
  {
    private readonly IBackendClient _backend;

    public VoiceMorphingBlendingClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public Task<VoiceMorphingBlendingViewModel.VoicePreviewResponse?> PreviewBlendAsync(
        string voiceAId,
        string voiceBId,
        float blendRatio,
        string text,
        CancellationToken cancellationToken = default)
    {
      var request = new VoiceMorphingBlendingViewModel.VoicePreviewRequest
      {
        VoiceAId = voiceAId,
        VoiceBId = voiceBId,
        BlendRatio = blendRatio,
        Text = text
      };
      return _backend.SendRequestAsync<VoiceMorphingBlendingViewModel.VoicePreviewRequest, VoiceMorphingBlendingViewModel.VoicePreviewResponse>(
          "/api/voice-morph/voice/preview",
          request,
          HttpMethod.Post,
          cancellationToken);
    }

    public Task<VoiceMorphingBlendingViewModel.VoiceBlendResponse?> BlendVoicesAsync(
        string voiceAId,
        string voiceBId,
        float blendRatio,
        string? text,
        bool saveProfile,
        CancellationToken cancellationToken = default)
    {
      var request = new VoiceMorphingBlendingViewModel.VoiceBlendRequest
      {
        VoiceAId = voiceAId,
        VoiceBId = voiceBId,
        BlendRatio = blendRatio,
        Text = text,
        SaveProfile = saveProfile
      };
      return _backend.SendRequestAsync<VoiceMorphingBlendingViewModel.VoiceBlendRequest, VoiceMorphingBlendingViewModel.VoiceBlendResponse>(
          "/api/voice-morph/voice/blend",
          request,
          HttpMethod.Post,
          cancellationToken);
    }

    public Task<VoiceMorphingBlendingViewModel.VoiceMorphResponse?> MorphVoiceAsync(
        string sourceAudioId,
        string voiceAId,
        string voiceBId,
        float startRatio,
        float endRatio,
        float morphSpeed,
        CancellationToken cancellationToken = default)
    {
      var request = new VoiceMorphingBlendingViewModel.VoiceMorphRequest
      {
        SourceAudioId = sourceAudioId,
        VoiceAId = voiceAId,
        VoiceBId = voiceBId,
        StartRatio = startRatio,
        EndRatio = endRatio,
        MorphSpeed = morphSpeed
      };
      return _backend.SendRequestAsync<VoiceMorphingBlendingViewModel.VoiceMorphRequest, VoiceMorphingBlendingViewModel.VoiceMorphResponse>(
          "/api/voice-morph/voice/morph",
          request,
          HttpMethod.Post,
          cancellationToken);
    }
  }
}
