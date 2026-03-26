namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// MCP Dashboard API models. Matches backend /api/mcp-dashboard.
  /// </summary>
  public class MCPDashboardSummary
  {
    public int TotalServers { get; set; }
    public int ConnectedServers { get; set; }
    public int DisconnectedServers { get; set; }
    public int ErrorServers { get; set; }
    public int TotalOperations { get; set; }
    public int AvailableOperations { get; set; }
  }

  public class MCPServerInfo
  {
    public string ServerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ServerType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public string? Version { get; set; }
    public string[] Capabilities { get; set; } = System.Array.Empty<string>();
    public string? LastConnected { get; set; }
    public string? ErrorMessage { get; set; }
  }

  public class MCPOperationInfo
  {
    public string OperationId { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
  }

  public class MCPServerCreateRequest
  {
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ServerType { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
  }

  public class MCPServerUpdateRequest
  {
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Endpoint { get; set; }
  }
}
