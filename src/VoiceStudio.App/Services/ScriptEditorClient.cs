using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/script-editor. PR-7: owns HTTP via BackendClientHttpPipeline; no IBackendClient delegation.
  /// </summary>
  public sealed class ScriptEditorClient : IScriptEditorClient
  {
    private readonly BackendClientHttpPipeline _pipeline;

    /// <summary>
    /// For DI: use BackendHttpContext.Pipeline. Tests use this ctor with mock pipeline.
    /// </summary>
    internal ScriptEditorClient(BackendClientHttpPipeline pipeline)
    {
      _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public async Task<Script?> GetScriptAsync(string scriptId, CancellationToken cancellationToken = default)
    {
      try
      {
        return await _pipeline.GetAsync<Script>($"/api/script-editor/{Uri.EscapeDataString(scriptId)}", cancellationToken);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[ScriptEditorClient] GetScriptAsync failed for {scriptId}: {ex.Message}");
        return null;
      }
    }

    /// <inheritdoc />
    public async Task<List<Script>> GetScriptsAsync(string? projectId = null, string? search = null, CancellationToken cancellationToken = default)
    {
      var queryParams = new List<string>();
      if (!string.IsNullOrEmpty(projectId))
        queryParams.Add($"project_id={Uri.EscapeDataString(projectId)}");
      if (!string.IsNullOrEmpty(search))
        queryParams.Add($"search={Uri.EscapeDataString(search)}");

      var url = "/api/script-editor";
      if (queryParams.Count > 0)
        url += $"?{string.Join("&", queryParams)}";

      var result = await _pipeline.GetAsync<List<Script>>(url, cancellationToken);
      return result ?? new List<Script>();
    }

    /// <inheritdoc />
    public Task<Script> CreateScriptAsync(ScriptCreateRequest request, CancellationToken cancellationToken = default)
      => _pipeline.PostAsync<ScriptCreateRequest, Script>("/api/script-editor", request, cancellationToken);

    /// <inheritdoc />
    public Task<Script> UpdateScriptAsync(string scriptId, ScriptUpdateRequest request, CancellationToken cancellationToken = default)
      => _pipeline.PutAsync<ScriptUpdateRequest, Script>($"/api/script-editor/{Uri.EscapeDataString(scriptId)}", request, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteScriptAsync(string scriptId, CancellationToken cancellationToken = default)
    {
      var response = await _pipeline.SendRequestAsync<object, object>(
          $"/api/script-editor/{Uri.EscapeDataString(scriptId)}", null, System.Net.Http.HttpMethod.Delete, cancellationToken);
      return response != null;
    }

    /// <inheritdoc />
    public Task<Script> AddSegmentToScriptAsync(string scriptId, ScriptSegment segment, CancellationToken cancellationToken = default)
      => _pipeline.PostAsync<ScriptSegment, Script>($"/api/script-editor/{Uri.EscapeDataString(scriptId)}/segments", segment, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> RemoveSegmentFromScriptAsync(string scriptId, string segmentId, CancellationToken cancellationToken = default)
    {
      var response = await _pipeline.SendRequestAsync<object, object>(
          $"/api/script-editor/{Uri.EscapeDataString(scriptId)}/segments/{Uri.EscapeDataString(segmentId)}", null, System.Net.Http.HttpMethod.Delete, cancellationToken);
      return response != null;
    }
  }
}
