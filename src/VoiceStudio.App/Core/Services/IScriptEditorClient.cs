using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for script editor API (/api/script-editor).
  /// Use instead of IBackendClient for script CRUD, segments, and synthesis.
  /// </summary>
  public interface IScriptEditorClient
  {
    Task<Script?> GetScriptAsync(string scriptId, CancellationToken cancellationToken = default);
    Task<List<Script>> GetScriptsAsync(string? projectId = null, string? search = null, CancellationToken cancellationToken = default);
    Task<Script> CreateScriptAsync(ScriptCreateRequest request, CancellationToken cancellationToken = default);
    Task<Script> UpdateScriptAsync(string scriptId, ScriptUpdateRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteScriptAsync(string scriptId, CancellationToken cancellationToken = default);
    Task<Script> AddSegmentToScriptAsync(string scriptId, ScriptSegment segment, CancellationToken cancellationToken = default);
    Task<bool> RemoveSegmentFromScriptAsync(string scriptId, string segmentId, CancellationToken cancellationToken = default);
  }
}
