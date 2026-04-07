using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/emotion. Uses IRequestCoordinator for single-flight coalescing on list/presets.
  /// </summary>
  public sealed class EmotionControlClient : IEmotionControlClient
  {
    private const string EmotionsKey = "emotion:list";
    private const string PresetsKey = "emotion:presets";
    private static readonly TimeSpan ListTtl = TimeSpan.FromSeconds(600);
    private static readonly TimeSpan PresetsTtl = TimeSpan.FromSeconds(60);

    private readonly IBackendClient _backend;
    private readonly IRequestCoordinator _coordinator;

    public EmotionControlClient(IBackendClient backend, IRequestCoordinator coordinator)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
      _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    /// <inheritdoc />
    public Task<string[]> GetAvailableEmotionsAsync(CancellationToken ct = default)
      => _coordinator.GetOrCreateAsync(
        EmotionsKey,
        async c => await FetchEmotionsAsync(c).ConfigureAwait(false),
        ListTtl,
        ct);

    private async Task<string[]> FetchEmotionsAsync(CancellationToken ct)
    {
      var result = await _backend.SendRequestAsync<object, string[]>(
        "/api/emotion/list",
        null,
        HttpMethod.Get,
        ct).ConfigureAwait(false);
      return result ?? Array.Empty<string>();
    }

    /// <inheritdoc />
    public Task<EmotionApplyExtendedResponse?> ApplyEmotionAsync(EmotionApplyExtendedRequest request, CancellationToken ct = default) => _backend.SendRequestAsync<EmotionApplyExtendedRequest, EmotionApplyExtendedResponse>(
        "/api/emotion/apply-extended",
        request,
        HttpMethod.Post,
        ct);

    /// <inheritdoc />
    public Task<EmotionPreviewResponse?> PreviewEmotionAsync(EmotionApplyExtendedRequest request, CancellationToken ct = default)
      => _backend.SendRequestAsync<EmotionApplyExtendedRequest, EmotionPreviewResponse>(
        "/api/emotion/preview",
        request,
        HttpMethod.Post,
        ct);

    /// <inheritdoc />
    public Task<EmotionPreset[]> GetPresetsAsync(CancellationToken ct = default)
      => _coordinator.GetOrCreateAsync(
        PresetsKey,
        async c => await FetchPresetsAsync(c).ConfigureAwait(false),
        PresetsTtl,
        ct);

    private async Task<EmotionPreset[]> FetchPresetsAsync(CancellationToken ct)
    {
      var result = await _backend.SendRequestAsync<object, EmotionPreset[]>(
        "/api/emotion/preset/list",
        null,
        HttpMethod.Get,
        ct).ConfigureAwait(false);
      return result ?? Array.Empty<EmotionPreset>();
    }

    /// <inheritdoc />
    public async Task<EmotionPreset> CreatePresetAsync(EmotionPresetCreateRequest request, CancellationToken ct = default)
    {
      var preset = await _backend.SendRequestAsync<EmotionPresetCreateRequest, EmotionPreset>(
        "/api/emotion/preset/save",
        request,
        HttpMethod.Post,
        ct).ConfigureAwait(false);
      _coordinator.Invalidate(PresetsKey);
      return preset;
    }

    /// <inheritdoc />
    public async Task<EmotionPreset> UpdatePresetAsync(string presetId, EmotionPresetUpdateRequest request, CancellationToken ct = default)
    {
      var preset = await _backend.UpdateEmotionPresetAsync(presetId, request, ct).ConfigureAwait(false);
      _coordinator.Invalidate(PresetsKey);
      return preset;
    }

    /// <inheritdoc />
    public async Task DeletePresetAsync(string presetId, CancellationToken ct = default)
    {
      await _backend.SendRequestAsync<object, object>(
        $"/api/emotion/preset/{Uri.EscapeDataString(presetId)}",
        null,
        HttpMethod.Delete,
        ct).ConfigureAwait(false);
      _coordinator.Invalidate(PresetsKey);
    }
  }
}

