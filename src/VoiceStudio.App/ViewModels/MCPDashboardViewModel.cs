using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the MCPDashboardView panel - MCP server dashboard and management.
  /// </summary>
  public partial class MCPDashboardViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IMCPDashboardClient _client;
    private CancellationTokenSource? _selectedServerOperationsCts;

    public string PanelId => "mcp-dashboard";
    public string DisplayName => ResourceHelper.GetString("Panel.MCPDashboard.DisplayName", "MCP Dashboard");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private MCPDashboardSummaryItem? summary;

    [ObservableProperty]
    private ObservableCollection<MCPServerItem> servers = new();

    [ObservableProperty]
    private MCPServerItem? selectedServer;

    [ObservableProperty]
    private ObservableCollection<MCPOperationItem> serverOperations = new();

    [ObservableProperty]
    private ObservableCollection<string> availableServerTypes = new();

    [ObservableProperty]
    private string? newServerName;

    [ObservableProperty]
    private string? newServerDescription;

    [ObservableProperty]
    private string? newServerType;

    [ObservableProperty]
    private string? newServerEndpoint;

    [ObservableProperty]
    private bool isCreatingServer;

    public MCPDashboardViewModel(IViewModelContext context, IMCPDashboardClient client)
        : base(context)
    {
      _client = client ?? throw new ArgumentNullException(nameof(client));

      LoadSummaryCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadSummary");
        await LoadSummaryAsync(ct);
      }, () => !IsLoading);
      LoadServersCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadServers");
        await LoadServersAsync(ct);
      }, () => !IsLoading);
      LoadServerTypesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadServerTypes");
        await LoadServerTypesAsync(ct);
      }, () => !IsLoading);
      CreateServerCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateServer");
        await CreateServerAsync(ct);
      }, () => !IsCreatingServer && !string.IsNullOrWhiteSpace(NewServerName) && !string.IsNullOrWhiteSpace(NewServerType) && !IsLoading);
      UpdateServerCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateServer");
        await UpdateServerAsync(ct);
      }, () => SelectedServer != null && !IsLoading);
      ConnectServerCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ConnectServer");
        await ConnectServerAsync(ct);
      }, () => SelectedServer != null && SelectedServer.Status != "connected" && !IsLoading);
      DisconnectServerCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DisconnectServer");
        await DisconnectServerAsync(ct);
      }, () => SelectedServer?.Status == "connected" && !IsLoading);
      DeleteServerCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteServer");
        await DeleteServerAsync(ct);
      }, () => SelectedServer != null && !IsLoading);
      LoadOperationsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadOperations");
        await LoadOperationsAsync(ct);
      }, () => SelectedServer != null && !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
    }

    public IAsyncRelayCommand LoadSummaryCommand { get; }
    public IAsyncRelayCommand LoadServersCommand { get; }
    public IAsyncRelayCommand LoadServerTypesCommand { get; }
    public IAsyncRelayCommand CreateServerCommand { get; }
    public IAsyncRelayCommand UpdateServerCommand { get; }
    public IAsyncRelayCommand ConnectServerCommand { get; }
    public IAsyncRelayCommand DisconnectServerCommand { get; }
    public IAsyncRelayCommand DeleteServerCommand { get; }
    public IAsyncRelayCommand LoadOperationsCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    Task IPanelLifecycle.OnActivatedAsync(CancellationToken cancellationToken)
    {
      return RefreshAsync(cancellationToken);
    }

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken cancellationToken)
    {
      _selectedServerOperationsCts?.Cancel();
      return Task.CompletedTask;
    }

    Task IPanelLifecycle.RefreshAsync(CancellationToken cancellationToken)
    {
      return RefreshAsync(cancellationToken);
    }

    partial void OnIsCreatingServerChanged(bool value)
    {
      CreateServerCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewServerNameChanged(string? value)
    {
      CreateServerCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewServerTypeChanged(string? value)
    {
      CreateServerCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedServerChanged(MCPServerItem? value)
    {
      UpdateServerCommand.NotifyCanExecuteChanged();
      ConnectServerCommand.NotifyCanExecuteChanged();
      DisconnectServerCommand.NotifyCanExecuteChanged();
      DeleteServerCommand.NotifyCanExecuteChanged();
      LoadOperationsCommand.NotifyCanExecuteChanged();

      if (value != null)
      {
        _selectedServerOperationsCts?.Cancel();
        _selectedServerOperationsCts = new CancellationTokenSource();
        var cts = _selectedServerOperationsCts;
        var serverId = value.ServerId;
        _ = LoadOperationsForSelectionAsync(serverId, cts.Token);
      }
    }

    private async Task LoadOperationsForSelectionAsync(string serverId, CancellationToken cancellationToken)
    {
      try
      {
        await LoadOperationsAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        return;
      }
    }

    private async Task LoadSummaryAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var s = await _client.GetSummaryAsync(cancellationToken);
        if (s != null)
        {
          Summary = new MCPDashboardSummaryItem(s);
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadSummary");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadServersAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var list = await _client.GetServersAsync(cancellationToken);
        if (list != null)
        {
          Servers.Clear();
          foreach (var server in list)
          {
            Servers.Add(new MCPServerItem(server));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadServers");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadServerTypesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var types = await _client.GetServerTypesAsync(cancellationToken);
        if (types != null)
        {
          AvailableServerTypes.Clear();
          foreach (var type in types)
          {
            AvailableServerTypes.Add(type);
          }
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadServerTypes");
      }
    }

    private async Task CreateServerAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(NewServerName) || string.IsNullOrWhiteSpace(NewServerType))
      {
        return;
      }

      IsCreatingServer = true;
      ErrorMessage = null;

      try
      {
        var request = new MCPServerCreateRequest
        {
          Name = NewServerName,
          Description = NewServerDescription ?? string.Empty,
          ServerType = NewServerType,
          Endpoint = NewServerEndpoint
        };

        var server = await _client.CreateServerAsync(request, cancellationToken);
        if (server != null)
        {
          Servers.Add(new MCPServerItem(server));
          NewServerName = null;
          NewServerDescription = null;
          NewServerType = null;
          NewServerEndpoint = null;
          StatusMessage = ResourceHelper.GetString("MCPDashboard.ServerCreated", "MCP server created successfully");
          await LoadSummaryAsync(cancellationToken);
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CreateServer");
      }
      finally
      {
        IsCreatingServer = false;
      }
    }

    private async Task UpdateServerAsync(CancellationToken cancellationToken)
    {
      if (SelectedServer == null)
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new MCPServerUpdateRequest
        {
          Name = SelectedServer.Name,
          Description = SelectedServer.Description,
          Endpoint = SelectedServer.Endpoint
        };

        var server = await _client.UpdateServerAsync(SelectedServer.ServerId, request, cancellationToken);
        if (server != null)
        {
          var index = Servers.IndexOf(SelectedServer);
          if (index >= 0)
          {
            Servers[index] = new MCPServerItem(server);
          }
          StatusMessage = ResourceHelper.GetString("MCPDashboard.ServerUpdated", "MCP server updated successfully");
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "UpdateServer");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ConnectServerAsync(CancellationToken cancellationToken)
    {
      if (SelectedServer == null)
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var server = await _client.ConnectServerAsync(SelectedServer.ServerId, cancellationToken);
        if (server != null)
        {
          var index = Servers.IndexOf(SelectedServer);
          if (index >= 0)
          {
            Servers[index] = new MCPServerItem(server);
          }
          StatusMessage = ResourceHelper.GetString("MCPDashboard.ServerConnected", "Connected to MCP server");
          await LoadSummaryAsync(cancellationToken);
          await LoadOperationsAsync(cancellationToken);
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ConnectServer");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DisconnectServerAsync(CancellationToken cancellationToken)
    {
      if (SelectedServer == null)
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var server = await _client.DisconnectServerAsync(SelectedServer.ServerId, cancellationToken);
        if (server != null)
        {
          var index = Servers.IndexOf(SelectedServer);
          if (index >= 0)
          {
            Servers[index] = new MCPServerItem(server);
          }
          StatusMessage = ResourceHelper.GetString("MCPDashboard.ServerDisconnected", "Disconnected from MCP server");
          await LoadSummaryAsync(cancellationToken);
          ServerOperations.Clear();
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DisconnectServer");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteServerAsync(CancellationToken cancellationToken)
    {
      if (SelectedServer == null)
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _client.DeleteServerAsync(SelectedServer.ServerId, cancellationToken);
        Servers.Remove(SelectedServer);
        SelectedServer = null;
        ServerOperations.Clear();
        StatusMessage = ResourceHelper.GetString("MCPDashboard.ServerDeleted", "MCP server deleted successfully");
        await LoadSummaryAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteServer");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadOperationsAsync(CancellationToken cancellationToken)
    {
      if (SelectedServer == null)
      {
        return;
      }

      var serverId = SelectedServer.ServerId;
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var operations = await _client.GetOperationsAsync(serverId, cancellationToken);
        if (operations != null && SelectedServer?.ServerId == serverId)
        {
          ServerOperations.Clear();
          foreach (var op in operations)
          {
            ServerOperations.Add(new MCPOperationItem(op));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadOperations");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await LoadSummaryAsync(cancellationToken);
      await LoadServersAsync(cancellationToken);
      await LoadServerTypesAsync(cancellationToken);
      if (SelectedServer != null)
      {
        await LoadOperationsAsync(cancellationToken);
      }
      StatusMessage = ResourceHelper.GetString("MCPDashboard.Refreshed", "Refreshed");
    }
  }

  // View-specific wrappers for display
  public class MCPDashboardSummaryItem : ObservableObject
  {
    public int TotalServers { get; set; }
    public int ConnectedServers { get; set; }
    public int DisconnectedServers { get; set; }
    public int ErrorServers { get; set; }
    public int TotalOperations { get; set; }
    public int AvailableOperations { get; set; }

    public MCPDashboardSummaryItem(MCPDashboardSummary s)
    {
      TotalServers = s.TotalServers;
      ConnectedServers = s.ConnectedServers;
      DisconnectedServers = s.DisconnectedServers;
      ErrorServers = s.ErrorServers;
      TotalOperations = s.TotalOperations;
      AvailableOperations = s.AvailableOperations;
    }
  }

  public class MCPServerItem : ObservableObject
  {
    public string ServerId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string ServerType { get; set; }
    public string Status { get; set; }
    public string? Endpoint { get; set; }
    public string? Version { get; set; }
    public string[] Capabilities { get; set; }
    public string? LastConnected { get; set; }
    public string? ErrorMessage { get; set; }

    public string StatusDisplay => Status.ToUpper();
    public string CapabilitiesDisplay => Capabilities?.Length > 0 ? string.Join(", ", Capabilities) : ResourceHelper.GetString("MCPDashboard.NoCapabilities", "No capabilities");
    public string LastConnectedDisplay => LastConnected != null ? FormatDateTime(LastConnected) : ResourceHelper.GetString("MCPDashboard.Never", "Never");

    public MCPServerItem(MCPServerInfo server)
    {
      ServerId = server.ServerId;
      Name = server.Name;
      Description = server.Description;
      ServerType = server.ServerType;
      Status = server.Status;
      Endpoint = server.Endpoint;
      Version = server.Version;
      Capabilities = server.Capabilities ?? Array.Empty<string>();
      LastConnected = server.LastConnected;
      ErrorMessage = server.ErrorMessage;
    }

    private static string FormatDateTime(string isoString)
    {
      if (DateTime.TryParse(isoString, out var dateTime))
      {
        return dateTime.ToString("yyyy-MM-dd HH:mm");
      }
      return isoString;
    }
  }

  public class MCPOperationItem : ObservableObject
  {
    public string OperationId { get; set; }
    public string ServerId { get; set; }
    public string OperationName { get; set; }
    public string Description { get; set; }
    public bool IsAvailable { get; set; }

    public string StatusDisplay => IsAvailable ? "Available" : "Unavailable";

    public MCPOperationItem(MCPOperationInfo op)
    {
      OperationId = op.OperationId;
      ServerId = op.ServerId;
      OperationName = op.OperationName;
      Description = op.Description;
      IsAvailable = op.IsAvailable;
    }
  }
}
