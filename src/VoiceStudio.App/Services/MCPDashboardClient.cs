using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for MCP Dashboard API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class MCPDashboardClient : IMCPDashboardClient
  {
    private readonly IBackendClient _backend;

    public MCPDashboardClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<MCPDashboardSummary?> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, MCPDashboardSummary>(
        "/api/mcp-dashboard",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<MCPServerInfo[]?> GetServersAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, MCPServerInfo[]>(
        "/api/mcp-dashboard/servers",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<string[]?> GetServerTypesAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, string[]>(
        "/api/mcp-dashboard/server-types",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<MCPServerInfo?> CreateServerAsync(MCPServerCreateRequest request, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<MCPServerCreateRequest, MCPServerInfo>(
        "/api/mcp-dashboard/servers",
        request,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<MCPServerInfo?> UpdateServerAsync(string serverId, MCPServerUpdateRequest request, CancellationToken cancellationToken = default)
    {
      var url = $"/api/mcp-dashboard/servers/{System.Uri.EscapeDataString(serverId ?? "")}";
      return _backend.SendRequestAsync<MCPServerUpdateRequest, MCPServerInfo>(
        url,
        request,
        System.Net.Http.HttpMethod.Put,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<MCPServerInfo?> ConnectServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/mcp-dashboard/servers/{System.Uri.EscapeDataString(serverId ?? "")}/connect";
      return _backend.SendRequestAsync<object, MCPServerInfo>(
        url,
        null,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<MCPServerInfo?> DisconnectServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/mcp-dashboard/servers/{System.Uri.EscapeDataString(serverId ?? "")}/disconnect";
      return _backend.SendRequestAsync<object, MCPServerInfo>(
        url,
        null,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/mcp-dashboard/servers/{System.Uri.EscapeDataString(serverId ?? "")}";
      return _backend.SendRequestAsync<object, object>(
        url,
        null,
        System.Net.Http.HttpMethod.Delete,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<MCPOperationInfo[]?> GetOperationsAsync(string serverId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/mcp-dashboard/servers/{System.Uri.EscapeDataString(serverId ?? "")}/operations";
      return _backend.SendRequestAsync<object, MCPOperationInfo[]>(
        url,
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }
  }
}
