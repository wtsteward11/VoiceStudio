using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Models
{
  /// <summary>GAP-062: One row per venv family from GET /api/settings/torch-venv/effective.</summary>
  public sealed class TorchVenvFamilyStatus
  {
    [JsonPropertyName("family")]
    public string Family { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "unresolved";

    [JsonPropertyName("python_exe")]
    public string? PythonExe { get; set; }

    [JsonPropertyName("torch_version")]
    public string? TorchVersion { get; set; }

    [JsonPropertyName("engines")]
    public List<string> Engines { get; set; } = new();

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
  }

  /// <summary>GAP-062: Effective torch venv diagnostics payload.</summary>
  public sealed class TorchVenvStatusResponse
  {
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("families")]
    public List<TorchVenvFamilyStatus> Families { get; set; } = new();
  }
}
