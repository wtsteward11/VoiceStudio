using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls.Primitives;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Forwards nav rail highlight to a target action wired after <see cref="MainWindowNavigationShellBridge"/> construction.
/// <see cref="ShellNavigationCoordinator"/> is constructed before the bridge; it receives <see cref="Forward"/> first.
/// </summary>
public sealed class NavButtonActionSink
{
    public Action<string>? Target { get; set; }

    public void Forward(string name) => Target?.Invoke(name);
}

/// <summary>
/// MainWindow navigation shell glue around <see cref="IShellNavigationCoordinator"/> (GAP-008 Slice 2).
/// Owns rail toggle updates, NavigationService subscription lifecycle, and thin coordinator forwards.
/// </summary>
public sealed class MainWindowNavigationShellBridge
{
    private readonly IShellNavigationCoordinator _shell;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Func<string, object?> _findNameOnContent;
    private INavigationService? _navigationService;
    private EventHandler<NavigationEventArgs>? _navigationChangedHandler;

    public MainWindowNavigationShellBridge(
        IShellNavigationCoordinator shell,
        DispatcherQueue dispatcherQueue,
        Func<string, object?> findNameOnContent,
        NavButtonActionSink sink)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        _findNameOnContent = findNameOnContent ?? throw new ArgumentNullException(nameof(findNameOnContent));
        ArgumentNullException.ThrowIfNull(sink);
        sink.Target = SetActiveNavButton;
        _navigationChangedHandler = OnNavigationChanged;
    }

    public void AttachNavigationService(INavigationService? navigationService)
    {
        DetachNavigationService();
        if (navigationService == null)
        {
            return;
        }

        _navigationService = navigationService;
        _navigationService.NavigationChanged += _navigationChangedHandler!;
        Debug.WriteLine("[MainWindowNavigationShellBridge] Subscribed to NavigationService.NavigationChanged");
    }

    public void DetachNavigationService()
    {
        if (_navigationService != null && _navigationChangedHandler != null)
        {
            _navigationService.NavigationChanged -= _navigationChangedHandler;
        }

        _navigationService = null;
    }

    public void ExecuteNavCommand(string commandId, string fallbackPanelId, PanelRegion fallbackRegion, string buttonName)
    {
        _ = ExecuteNavCommandAsync(commandId, fallbackPanelId, fallbackRegion, buttonName);
    }

    public Task ExecuteNavCommandAsync(string commandId, string fallbackPanelId, PanelRegion fallbackRegion, string buttonName)
    {
        return _shell.ExecuteNavCommandAsync(commandId, fallbackPanelId, fallbackRegion, buttonName);
    }

    public Task<bool> OpenPanelByIdAsync(string panelId, PanelRegion? overrideRegion = null)
    {
        return _shell.OpenPanelByIdAsync(panelId, overrideRegion);
    }

    public PanelRegion GetPanelRegion(string panelId)
    {
        return _shell.GetPanelRegion(panelId);
    }

    public string GetPanelTitle(string panelId)
    {
        return _shell.GetPanelTitle(panelId);
    }

    public void SetActiveNavButton(string activeButtonName)
    {
        var navButtons = new[]
        {
            _findNameOnContent("NavStudio") as ToggleButton,
            _findNameOnContent("NavProfiles") as ToggleButton,
            _findNameOnContent("NavLibrary") as ToggleButton,
            _findNameOnContent("NavEffects") as ToggleButton,
            _findNameOnContent("NavTrain") as ToggleButton,
            _findNameOnContent("NavAnalyze") as ToggleButton,
            _findNameOnContent("NavSettings") as ToggleButton,
            _findNameOnContent("NavLogs") as ToggleButton
        };

        foreach (var navButton in navButtons)
        {
            if (navButton == null)
            {
                continue;
            }

            navButton.IsChecked = string.Equals(navButton.Name, activeButtonName, StringComparison.Ordinal);
        }
    }

    private void OnNavigationChanged(object? sender, NavigationEventArgs e)
    {
        if (string.IsNullOrEmpty(e.NewPanelId))
        {
            return;
        }

        var panelId = e.NewPanelId.ToLowerInvariant();
        Debug.WriteLine($"[MainWindowNavigationShellBridge] OnNavigationChanged: {panelId}");

        _dispatcherQueue.TryEnqueue(() =>
        {
            _ = OnNavigationChangedCoreAsync(panelId, e.NewPanelId);
        });
    }

    private async Task OnNavigationChangedCoreAsync(string panelId, string originalPanelId)
    {
        try
        {
            var canonicalId = _shell.ResolvePanelIdAlias(panelId);

            if (await OpenPanelByIdAsync(canonicalId).ConfigureAwait(true))
            {
                // Nav button state updated via NavigationViewModel bindings / coordinator callbacks where applicable.
            }
            else
            {
                Debug.WriteLine($"[MainWindowNavigationShellBridge] Unknown panel ID in navigation: {panelId}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindowNavigationShellBridge] Navigation failed: {ex.Message}");
        }
    }
}
