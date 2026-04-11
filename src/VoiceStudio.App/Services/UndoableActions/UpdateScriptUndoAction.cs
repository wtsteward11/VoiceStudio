using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.UndoableActions;

/// <summary>
/// GAP-012 remainder: undo/redo for script updates (name, description, segments) via <see cref="IScriptEditorClient.UpdateScriptAsync"/>.
/// </summary>
public sealed class UpdateScriptUndoAction : IUndoableAction
{
  private readonly IScriptEditorClient _client;
  private readonly string _scriptId;
  private readonly ScriptUpdateRequest _before;
  private readonly ScriptUpdateRequest _after;
  private readonly Action _reloadAfterApply;
  private readonly IProjectSessionDirtyState? _sessionDirty;
  private readonly IErrorLoggingService? _log;

  public UpdateScriptUndoAction(
      IScriptEditorClient client,
      string scriptId,
      ScriptUpdateRequest before,
      ScriptUpdateRequest after,
      Action reloadAfterApply,
      string actionName,
      IProjectSessionDirtyState? sessionDirty = null,
      IErrorLoggingService? log = null)
  {
    _client = client ?? throw new ArgumentNullException(nameof(client));
    _scriptId = scriptId ?? throw new ArgumentNullException(nameof(scriptId));
    _before = ScriptUndoSnapshots.CloneRequest(before ?? throw new ArgumentNullException(nameof(before)));
    _after = ScriptUndoSnapshots.CloneRequest(after ?? throw new ArgumentNullException(nameof(after)));
    _reloadAfterApply = reloadAfterApply ?? throw new ArgumentNullException(nameof(reloadAfterApply));
    ActionName = actionName ?? throw new ArgumentNullException(nameof(actionName));
    _sessionDirty = sessionDirty;
    _log = log;
  }

  public string ActionName { get; }

  public void Undo() => Apply(_before);

  public void Redo() => Apply(_after);

  private void Apply(ScriptUpdateRequest target)
  {
    try
    {
      _ = _client
          .UpdateScriptAsync(_scriptId, ScriptUndoSnapshots.CloneRequest(target), CancellationToken.None)
          .ConfigureAwait(false)
          .GetAwaiter()
          .GetResult();
      _sessionDirty?.MarkProjectDirty("script_editor_undo");
      _reloadAfterApply();
    }
    catch (Exception ex)
    {
      _log?.LogError(ex, "UpdateScriptUndoAction.Apply");
      throw;
    }
  }
}

/// <summary>Deep copies for undo snapshots (script editor).</summary>
public static class ScriptUndoSnapshots
{
  public static ScriptUpdateRequest CloneRequest(ScriptUpdateRequest source)
  {
    ArgumentNullException.ThrowIfNull(source);
    return new ScriptUpdateRequest
    {
      Name = source.Name,
      Description = source.Description,
      Segments = source.Segments?.Select(CloneSegment).ToList(),
      Metadata = source.Metadata != null
          ? new Dictionary<string, object>(source.Metadata)
          : null
    };
  }

  public static ScriptSegment CloneSegment(ScriptSegment s)
  {
    ArgumentNullException.ThrowIfNull(s);
    return new ScriptSegment
    {
      Id = s.Id,
      Text = s.Text,
      StartTime = s.StartTime,
      EndTime = s.EndTime,
      Speaker = s.Speaker,
      VoiceProfileId = s.VoiceProfileId,
      Prosody = s.Prosody != null ? new Dictionary<string, object>(s.Prosody) : null,
      Phonemes = s.Phonemes != null ? new List<string>(s.Phonemes) : null,
      Notes = s.Notes,
      GeneratedAudioId = s.GeneratedAudioId,
      GeneratedAt = s.GeneratedAt,
      GenerationProfileId = s.GenerationProfileId,
      GenerationEngineId = s.GenerationEngineId,
      GenerationStatus = s.GenerationStatus
    };
  }
}
