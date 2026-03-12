using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Shell
{
  /// <summary>
  /// ViewModel for the navigation rail. Provides ICommand per panel and delegates to NavigationService.
  /// </summary>
  public partial class NavigationViewModel : ObservableObject
  {
    private readonly CommandRouter? _commandRouter;
    private readonly INavigationService? _navigationService;

    [ObservableProperty]
    private string activePanelId = "Timeline";

    public NavigationViewModel()
    {
      _commandRouter = AppServices.TryGetCommandRouter();
      _navigationService = AppServices.TryGetNavigationService();
      if (_navigationService != null)
        _navigationService.NavigationChanged += OnNavigationChanged;

      NavigateToStudioCommand = new AsyncRelayCommand(() => ExecuteNavAsync("nav.studio", "Timeline", PanelRegion.Center));
      NavigateToProfilesCommand = new AsyncRelayCommand(() => ExecuteNavAsync("nav.profiles", "Profiles", PanelRegion.Left));
      NavigateToLibraryCommand = new AsyncRelayCommand(() => ExecuteNavAsync("nav.library", "Library", PanelRegion.Left));
      NavigateToEffectsCommand = new AsyncRelayCommand(() => ExecuteNavAsync("nav.effects", "EffectsMixer", PanelRegion.Right));
      NavigateToTrainCommand = new AsyncRelayCommand(() => ExecuteNavAsync("nav.train", "Training", PanelRegion.Left));
      NavigateToAnalyzeCommand = new AsyncRelayCommand(() => ExecuteNavAsync("nav.analyze", "Analyzer", PanelRegion.Right));
      NavigateToSettingsCommand = new AsyncRelayCommand(() => ExecuteNavAsync("nav.settings", "Settings", PanelRegion.Right));
      NavigateToLogsCommand = new AsyncRelayCommand(() => ExecuteNavAsync("nav.logs", "Diagnostics", PanelRegion.Bottom));
    }

    public ICommand NavigateToStudioCommand { get; }
    public ICommand NavigateToProfilesCommand { get; }
    public ICommand NavigateToLibraryCommand { get; }
    public ICommand NavigateToEffectsCommand { get; }
    public ICommand NavigateToTrainCommand { get; }
    public ICommand NavigateToAnalyzeCommand { get; }
    public ICommand NavigateToSettingsCommand { get; }
    public ICommand NavigateToLogsCommand { get; }

    public bool IsStudioActive => string.Equals(ActivePanelId, "Timeline", StringComparison.OrdinalIgnoreCase);
    public bool IsProfilesActive => string.Equals(ActivePanelId, "Profiles", StringComparison.OrdinalIgnoreCase);
    public bool IsLibraryActive => string.Equals(ActivePanelId, "Library", StringComparison.OrdinalIgnoreCase);
    public bool IsEffectsActive => string.Equals(ActivePanelId, "EffectsMixer", StringComparison.OrdinalIgnoreCase);
    public bool IsTrainActive => string.Equals(ActivePanelId, "Training", StringComparison.OrdinalIgnoreCase);
    public bool IsAnalyzeActive => string.Equals(ActivePanelId, "Analyzer", StringComparison.OrdinalIgnoreCase);
    public bool IsSettingsActive => string.Equals(ActivePanelId, "Settings", StringComparison.OrdinalIgnoreCase);
    public bool IsLogsActive => string.Equals(ActivePanelId, "Diagnostics", StringComparison.OrdinalIgnoreCase);

    private async Task ExecuteNavAsync(string commandId, string fallbackPanelId, PanelRegion fallbackRegion)
    {
      if (_commandRouter != null)
      {
        var success = await _commandRouter.ExecuteSafeAsync(commandId);
        if (success)
        {
          UpdateActivePanelFromCanonicalId(fallbackPanelId);
          return;
        }
      }

      if (_navigationService != null)
      {
        var panelId = fallbackPanelId.ToLowerInvariant();
        await _navigationService.NavigateToPanelAsync(panelId);
        UpdateActivePanelFromCanonicalId(fallbackPanelId);
      }
    }

    private void OnNavigationChanged(object? sender, VoiceStudio.Core.Models.NavigationEventArgs e)
    {
      if (string.IsNullOrEmpty(e.NewPanelId))
        return;

      var canonicalId = e.NewPanelId.ToLowerInvariant() switch
      {
        "studio" or "home" or "timeline" => "Timeline",
        "profiles" => "Profiles",
        "library" => "Library",
        "effects" => "EffectsMixer",
        "train" => "Training",
        "analyze" => "Analyzer",
        "settings" => "Settings",
        "logs" => "Diagnostics",
        "synthesis" => "VoiceSynthesis",
        _ => e.NewPanelId,
      };

      var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
          ?? App.MainWindowInstance?.DispatcherQueue;
      dq?.TryEnqueue(() =>
      {
        ActivePanelId = canonicalId;
        OnPropertyChanged(nameof(IsStudioActive));
        OnPropertyChanged(nameof(IsProfilesActive));
        OnPropertyChanged(nameof(IsLibraryActive));
        OnPropertyChanged(nameof(IsEffectsActive));
        OnPropertyChanged(nameof(IsTrainActive));
        OnPropertyChanged(nameof(IsAnalyzeActive));
        OnPropertyChanged(nameof(IsSettingsActive));
        OnPropertyChanged(nameof(IsLogsActive));
      });
    }

    private void UpdateActivePanelFromCanonicalId(string canonicalId)
    {
      ActivePanelId = canonicalId;
      OnPropertyChanged(nameof(IsStudioActive));
      OnPropertyChanged(nameof(IsProfilesActive));
      OnPropertyChanged(nameof(IsLibraryActive));
      OnPropertyChanged(nameof(IsEffectsActive));
      OnPropertyChanged(nameof(IsTrainActive));
      OnPropertyChanged(nameof(IsAnalyzeActive));
      OnPropertyChanged(nameof(IsSettingsActive));
      OnPropertyChanged(nameof(IsLogsActive));
    }
  }
}
