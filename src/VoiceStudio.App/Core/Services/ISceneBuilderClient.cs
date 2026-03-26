using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for scenes API (/api/scenes).
  /// Use instead of IBackendClient for scene CRUD and apply.
  /// </summary>
  public interface ISceneBuilderClient
  {
    Task<Scene[]> GetScenesAsync(string? projectId = null, string? search = null, CancellationToken cancellationToken = default);
    Task<Scene?> CreateSceneAsync(SceneCreateRequest request, CancellationToken cancellationToken = default);
    Task<Scene?> UpdateSceneAsync(string sceneId, SceneUpdateRequest request, CancellationToken cancellationToken = default);
    Task DeleteSceneAsync(string sceneId, CancellationToken cancellationToken = default);
    Task<SceneApplyResponse?> ApplySceneAsync(string sceneId, string targetProjectId, CancellationToken cancellationToken = default);
  }
}
