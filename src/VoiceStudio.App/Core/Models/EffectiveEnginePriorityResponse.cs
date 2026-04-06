using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Models
{
  /// <summary>GAP-053: GET /api/settings/engine-priority/effective response.</summary>
  public sealed class EffectiveEnginePriorityResponse
  {
    [JsonPropertyName("task_type")]
    public string TaskType { get; set; } = "tts";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("order")]
    public List<string> Order { get; set; } = new();

    [JsonPropertyName("available")]
    public List<string> Available { get; set; } = new();

    [JsonPropertyName("skipped")]
    public List<string> Skipped { get; set; } = new();

    [JsonPropertyName("registered_engines")]
    public List<string> RegisteredEngines { get; set; } = new();
  }
}
