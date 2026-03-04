using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Models;

/// <summary>
/// Single engine entry in list response (backend returns objects with id, name, etc.).
/// </summary>
public class EngineListEntry
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("name")]
  public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Response model for listing available engines.
/// Backend returns engines as array of {id, name, ...}; we extract IDs for compatibility.
/// </summary>
public class EnginesListResponse
{
  /// <summary>
  /// List of engines (objects with id, name). Use EngineIds for ID list.
  /// </summary>
  [JsonPropertyName("engines")]
  public List<EngineListEntry>? Engines { get; set; }

  /// <summary>
  /// Whether engines are available.
  /// </summary>
  public bool Available { get; set; }

  /// <summary>
  /// Total count of engines.
  /// </summary>
  public int Count { get; set; }

  /// <summary>
  /// Get engine IDs for EngineManager/BackendClient compatibility.
  /// </summary>
  [JsonIgnore]
  public List<string> EngineIds =>
    Engines?.Where(e => !string.IsNullOrEmpty(e.Id)).Select(e => e.Id).ToList() ?? new List<string>();
}
