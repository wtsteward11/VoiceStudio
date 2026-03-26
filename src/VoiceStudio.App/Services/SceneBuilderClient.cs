using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/scenes. Thin pass-through to IBackendClient.SendRequestAsync.
  /// </summary>
  public sealed class SceneBuilderClient : ISceneBuilderClient
  {
    private readonly IBackendClient _backend;

    public SceneBuilderClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public async Task<Scene[]> GetScenesAsync(string? projectId = null, string? search = null, CancellationToken cancellationToken = default)
    {
      var queryParams = new List<string>();
      if (!string.IsNullOrEmpty(projectId))
        queryParams.Add($"project_id={Uri.EscapeDataString(projectId)}");
      if (!string.IsNullOrEmpty(search))
        queryParams.Add($"search={Uri.EscapeDataString(search)}");

      var url = "/api/scenes";
      if (queryParams.Count > 0)
        url += "?" + string.Join("&", queryParams);

      var result = await _backend.SendRequestAsync<object, Scene[]>(
          url,
          null,
          HttpMethod.Get,
          cancellationToken);
      return result ?? Array.Empty<Scene>();
    }

    /// <inheritdoc />
    public Task<Scene?> CreateSceneAsync(SceneCreateRequest request, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<SceneCreateRequest, Scene>(
          "/api/scenes",
          request,
          HttpMethod.Post,
          cancellationToken);
    }

    /// <inheritdoc />
    public Task<Scene?> UpdateSceneAsync(string sceneId, SceneUpdateRequest request, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<SceneUpdateRequest, Scene>(
          $"/api/scenes/{Uri.EscapeDataString(sceneId)}",
          request,
          HttpMethod.Put,
          cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteSceneAsync(string sceneId, CancellationToken cancellationToken = default)
    {
      await _backend.SendRequestAsync<object, object>(
          $"/api/scenes/{Uri.EscapeDataString(sceneId)}",
          null,
          HttpMethod.Delete,
          cancellationToken);
    }

    /// <inheritdoc />
    public Task<SceneApplyResponse?> ApplySceneAsync(string sceneId, string targetProjectId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/scenes/{Uri.EscapeDataString(sceneId)}/apply?target_project_id={Uri.EscapeDataString(targetProjectId)}";
      return _backend.SendRequestAsync<object, SceneApplyResponse>(
          url,
          null,
          HttpMethod.Post,
          cancellationToken);
    }
  }
}
