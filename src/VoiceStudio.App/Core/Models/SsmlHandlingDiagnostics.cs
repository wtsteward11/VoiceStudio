using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  public sealed class SsmlHandlingDiagnostics
  {
    public bool SsmlDetected { get; set; }
    public string CapabilityClass { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
    public string EngineId { get; set; } = string.Empty;
  }
}
