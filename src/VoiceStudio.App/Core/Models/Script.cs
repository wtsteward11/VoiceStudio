using System;
using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// A segment in a script for voice synthesis.
  /// </summary>
  public class ScriptSegment
  {
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double? StartTime { get; set; }
    public double? EndTime { get; set; }
    public string? Speaker { get; set; }
    public string? VoiceProfileId { get; set; }
    public Dictionary<string, object>? Prosody { get; set; }
    public List<string>? Phonemes { get; set; }
    public string? Notes { get; set; }
    /// <summary>Audio ID from latest segment generation. Persisted with script.</summary>
    public string? GeneratedAudioId { get; set; }
    /// <summary>ISO timestamp of latest generation.</summary>
    public string? GeneratedAt { get; set; }
    /// <summary>Profile ID used for generation.</summary>
    public string? GenerationProfileId { get; set; }
    /// <summary>Engine ID used for generation.</summary>
    public string? GenerationEngineId { get; set; }
    /// <summary>Generation status (e.g. "success", "failed").</summary>
    public string? GenerationStatus { get; set; }
  }

  /// <summary>
  /// A script for voice synthesis.
  /// </summary>
  public class Script
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public List<ScriptSegment> Segments { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string Created { get; set; } = string.Empty;
    public string Modified { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
  }

  /// <summary>
  /// Request to create a script.
  /// </summary>
  public class ScriptCreateRequest
  {
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public List<ScriptSegment>? Segments { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
  }

  /// <summary>
  /// Request to update a script.
  /// </summary>
  public class ScriptUpdateRequest
  {
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<ScriptSegment>? Segments { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
  }

  /// <summary>
  /// Response from script synthesis.
  /// </summary>
  public class ScriptSynthesisResponse
  {
    public string ScriptId { get; set; } = string.Empty;
    public string AudioId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
  }
}