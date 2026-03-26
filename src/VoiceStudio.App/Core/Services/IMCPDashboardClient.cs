using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for MCP Dashboard API (/api/mcp-dashboard).
  /// Use instead of IBackendClient for MCPDashboard panel.
  /// </summary>
  public interface IMCPDashboardClient
  {
    Task<MCPDashboardSummary?> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<MCPServerInfo[]?> GetServersAsync(CancellationToken cancellationToken = default);

    Task<string[]?> GetServerTypesAsync(CancellationToken cancellationToken = default);

    Task<MCPServerInfo?> CreateServerAsync(MCPServerCreateRequest request, CancellationToken cancellationToken = default);

    Task<MCPServerInfo?> UpdateServerAsync(string serverId, MCPServerUpdateRequest request, CancellationToken cancellationToken = default);

    Task<MCPServerInfo?> ConnectServerAsync(string serverId, CancellationToken cancellationToken = default);

    Task<MCPServerInfo?> DisconnectServerAsync(string serverId, CancellationToken cancellationToken = default);

    Task DeleteServerAsync(string serverId, CancellationToken cancellationToken = default);

    Task<MCPOperationInfo[]?> GetOperationsAsync(string serverId, CancellationToken cancellationToken = default);
  }
}
