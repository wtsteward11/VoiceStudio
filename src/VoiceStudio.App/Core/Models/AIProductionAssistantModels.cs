using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  public class AIProductionAssistantQueryRequest
  {
    public string Query { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public Dictionary<string, object>? Context { get; set; }
  }

  public class AIProductionAssistantQueryResponse
  {
    public string SessionId { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public Dictionary<string, object>? ActionData { get; set; }
    public List<string> Suggestions { get; set; } = new();
    public float Confidence { get; set; }
  }

  public class AIProductionAssistantExecuteRequest
  {
    public string SessionId { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
  }

  public class AIProductionAssistantExecuteResponse
  {
    public bool Success { get; set; }
    public Dictionary<string, object>? Result { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
  }

  public class AIProductionAssistantContextResponse
  {
    public List<string> OpenPanels { get; set; } = new();
    public string? CurrentProject { get; set; }
    public string? ActiveAudioId { get; set; }
    public List<string> AvailableProfiles { get; set; } = new();
    public List<string> RecentOperations { get; set; } = new();
  }
}
