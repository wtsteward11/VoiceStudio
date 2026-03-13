using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/emotion-style. Uses IRequestCoordinator for single-flight coalescing on preset reads.
  /// </summary>
  public sealed class EmotionStyleClient : IEmotionStyleClient
  {
    private const string EmotionsKey = "emotion-style:emotions";
    private const string StylesKey = "emotion-style:styles";
    private static readonly TimeSpan PresetsTtl = TimeSpan.FromSeconds(60);

    private readonly IBackendClient _backend;
    private readonly IRequestCoordinator _coordinator;

    public EmotionStyleClient(IBackendClient backend, IRequestCoordinator coordinator)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
      _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    /// <inheritdoc />
    public Task<EmotionStyleEmotionPreset[]> GetEmotionPresetsAsync(CancellationToken ct = default)
      => _coordinator.GetOrCreateAsync(
        EmotionsKey,
        async c => await FetchEmotionPresetsAsync(c).ConfigureAwait(false),
        PresetsTtl,
        ct);

    /// <inheritdoc />
    public Task<EmotionStyleStylePreset[]> GetStylePresetsAsync(CancellationToken ct = default)
      => _coordinator.GetOrCreateAsync(
        StylesKey,
        async c => await FetchStylePresetsAsync(c).ConfigureAwait(false),
        PresetsTtl,
        ct);

    private async Task<EmotionStyleEmotionPreset[]> FetchEmotionPresetsAsync(CancellationToken ct)
    {
      var result = await _backend.SendRequestAsync<object, EmotionStyleEmotionPreset[]>(
        "/api/emotion-style/emotions",
        null,
        HttpMethod.Get,
        ct).ConfigureAwait(false);
      return result ?? Array.Empty<EmotionStyleEmotionPreset>();
    }

    private async Task<EmotionStyleStylePreset[]> FetchStylePresetsAsync(CancellationToken ct)
    {
      var result = await _backend.SendRequestAsync<object, EmotionStyleStylePreset[]>(
        "/api/emotion-style/styles",
        null,
        HttpMethod.Get,
        ct).ConfigureAwait(false);
      return result ?? Array.Empty<EmotionStyleStylePreset>();
    }

    /// <inheritdoc />
    public Task<EmotionStyleApplyResponse> ApplyEmotionStyleAsync(EmotionStyleApplyRequest request, CancellationToken ct = default)
      => _backend.SendRequestAsync<EmotionStyleApplyRequest, EmotionStyleApplyResponse>(
        "/api/emotion-style/apply",
        request,
        ct);
  }
}
