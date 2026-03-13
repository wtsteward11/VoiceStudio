using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;
using Preset = VoiceStudio.App.ViewModels.Preset;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/presets. Thin pass-through with optional IRequestCoordinator for list/types/categories.
  /// </summary>
  public sealed class PresetLibraryClient : IPresetLibraryClient
  {
    private const string PresetsKey = "preset:search";
    private const string TypesKey = "preset:types";
    private static readonly TimeSpan SearchTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TypesTtl = TimeSpan.FromSeconds(600);

    private readonly IBackendClient _backend;
    private readonly IRequestCoordinator? _coordinator;

    public PresetLibraryClient(IBackendClient backend, IRequestCoordinator? coordinator = null)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
      _coordinator = coordinator;
    }

    /// <inheritdoc />
    public async Task<PresetSearchResult> SearchPresetsAsync(string? query, string? presetType, string? category, CancellationToken ct = default)
    {
      var queryParams = new NameValueCollection();
      if (!string.IsNullOrEmpty(query))
        queryParams.Add("query", query);
      if (!string.IsNullOrEmpty(presetType))
        queryParams.Add("preset_type", presetType);
      if (!string.IsNullOrEmpty(category))
        queryParams.Add("category", category);

      var queryString = string.Join("&",
          queryParams.AllKeys.Cast<string>().SelectMany(key =>
              queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
          )
      );

      var url = "/api/presets";
      if (!string.IsNullOrEmpty(queryString))
        url += $"?{queryString}";

      var response = await _backend.SendRequestAsync<object, PresetSearchResult>(
          url,
          null,
          HttpMethod.Get,
          ct).ConfigureAwait(false);

      return response ?? new PresetSearchResult();
    }

    /// <inheritdoc />
    public async Task<Preset> CreatePresetAsync(PresetCreateRequest request, CancellationToken ct = default)
    {
      var body = new
      {
        name = request.Name,
        preset_type = request.PresetType,
        category = request.Category,
        description = request.Description,
        data = request.Data ?? new { },
        tags = request.Tags ?? Array.Empty<string>(),
        is_public = request.IsPublic
      };

      var preset = await _backend.SendRequestAsync<object, Preset>(
          "/api/presets",
          body,
          HttpMethod.Post,
          ct).ConfigureAwait(false);

      _coordinator?.Invalidate(PresetsKey);
      return preset ?? throw new InvalidOperationException("Create preset returned null");
    }

    /// <inheritdoc />
    public async Task<Preset> UpdatePresetAsync(string presetId, PresetUpdateRequest request, CancellationToken ct = default)
    {
      var body = new
      {
        name = request.Name,
        category = request.Category,
        description = request.Description,
        tags = request.Tags,
        is_public = request.IsPublic
      };

      var preset = await _backend.SendRequestAsync<object, Preset>(
          $"/api/presets/{Uri.EscapeDataString(presetId)}",
          body,
          HttpMethod.Put,
          ct).ConfigureAwait(false);

      _coordinator?.Invalidate(PresetsKey);
      return preset ?? throw new InvalidOperationException("Update preset returned null");
    }

    /// <inheritdoc />
    public async Task DeletePresetAsync(string presetId, CancellationToken ct = default)
    {
      await _backend.SendRequestAsync<object, object>(
          $"/api/presets/{Uri.EscapeDataString(presetId)}",
          null,
          HttpMethod.Delete,
          ct).ConfigureAwait(false);

      _coordinator?.Invalidate(PresetsKey);
    }

    /// <inheritdoc />
    public Task<PresetApplyResult> ApplyPresetAsync(string presetId, string? targetId, CancellationToken ct = default)
    {
      var body = new { target_id = targetId };
      return _backend.SendRequestAsync<object, PresetApplyResult>(
          $"/api/presets/{Uri.EscapeDataString(presetId)}/apply",
          body,
          HttpMethod.Post,
          ct);
    }

    /// <inheritdoc />
    public async Task<string[]> GetPresetTypesAsync(CancellationToken ct = default)
    {
      if (_coordinator != null)
      {
        return await _coordinator.GetOrCreateAsync(
            TypesKey,
            async c =>
            {
              var response = await _backend.SendRequestAsync<object, PresetTypesResponse>(
                  "/api/presets/types",
                  null,
                  HttpMethod.Get,
                  c).ConfigureAwait(false);
              return response?.Types?.Select(t => t.Id).ToArray() ?? Array.Empty<string>();
            },
            TypesTtl,
            ct).ConfigureAwait(false);
      }

      var resp = await _backend.SendRequestAsync<object, PresetTypesResponse>(
          "/api/presets/types",
          null,
          HttpMethod.Get,
          ct).ConfigureAwait(false);
      return resp?.Types?.Select(t => t.Id).ToArray() ?? Array.Empty<string>();
    }

    /// <inheritdoc />
    public async Task<string[]> GetCategoriesAsync(string presetType, CancellationToken ct = default)
    {
      var result = await _backend.SendRequestAsync<object, string[]>(
          $"/api/presets/categories/{Uri.EscapeDataString(presetType)}",
          null,
          HttpMethod.Get,
          ct).ConfigureAwait(false);
      return result ?? Array.Empty<string>();
    }

    private class PresetTypesResponse
    {
      public PresetTypeInfo[]? Types { get; set; }
    }

    private class PresetTypeInfo
    {
      public string Id { get; set; } = string.Empty;
      public string Name { get; set; } = string.Empty;
    }
  }
}
