// VoiceStudio - Shell Navigation Coordination (Premium Reliability Pass Task 9)
// Extracted from MainWindow.xaml.cs per MAINWINDOW_DECOMPOSITION_PLAN.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Services;

/// <summary>
/// Coordinates panel navigation: OpenPanelByIdAsync, command resolution, region targeting,
/// search-result-to-panel navigation, nav button state sync.
/// </summary>
public sealed class ShellNavigationCoordinator : IShellNavigationCoordinator
{
    private readonly Func<PanelRegion, PanelHost?> _getPanelHost;
    private readonly Func<string, object?> _findName;
    private readonly Func<string, Func<Microsoft.UI.Xaml.Controls.UserControl>?> _getLegacyFactory;
    private readonly Action<string> _setActiveNavButton;
    private readonly Action<string, PanelRegion, PanelHost> _showQuickSwitchIndicator;
    private readonly Func<bool> _isGateCSmokeMode;
    private readonly PanelStateService? _panelStateService;
    private readonly CommandRouter? _commandRouter;

    private static readonly Dictionary<string, (PanelRegion DefaultRegion, string Title, Func<Microsoft.UI.Xaml.Controls.UserControl> Factory)> LegacyRegistry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MiniTimeline"] = (PanelRegion.Bottom, "Mini Timeline", () => new MiniTimelineView()),
        };

    public ShellNavigationCoordinator(
        Func<PanelRegion, PanelHost?> getPanelHost,
        Func<string, object?> findName,
        Func<string, Func<Microsoft.UI.Xaml.Controls.UserControl>?> getLegacyFactory,
        Action<string> setActiveNavButton,
        Action<string, PanelRegion, PanelHost> showQuickSwitchIndicator,
        Func<bool> isGateCSmokeMode,
        PanelStateService? panelStateService = null,
        CommandRouter? commandRouter = null)
    {
        _getPanelHost = getPanelHost ?? throw new ArgumentNullException(nameof(getPanelHost));
        _findName = findName ?? throw new ArgumentNullException(nameof(findName));
        _getLegacyFactory = getLegacyFactory ?? (_ => null);
        _setActiveNavButton = setActiveNavButton ?? (_ => { });
        _showQuickSwitchIndicator = showQuickSwitchIndicator ?? ((_, __, ___) => { });
        _isGateCSmokeMode = isGateCSmokeMode ?? (() => false);
        _panelStateService = panelStateService;
        _commandRouter = commandRouter;
    }

    /// <inheritdoc />
    public async Task<bool> OpenPanelByIdAsync(string panelId, PanelRegion? overrideRegion = null)
    {
        var startupState = ServiceProvider.GetStartupStateService();
        if (!startupState.IsReady)
        {
            AppServices.TryGetToastNotificationService()?.ShowInfo("Starting VoiceStudio services…", "Please wait");
            return false;
        }

        var finalRegion = overrideRegion ?? GetPanelRegion(panelId);

        if (overrideRegion.HasValue && _panelStateService != null)
        {
            var layout = _panelStateService.GetCurrentLayout();
            var currentRegionState = layout.Regions?.FirstOrDefault(r => r.PanelStates.ContainsKey(panelId));
            if (currentRegionState != null && currentRegionState.Region != finalRegion)
                _panelStateService.MigratePanelState(panelId, currentRegionState.Region, finalRegion);
        }

        return await SwitchToPanelByIdAsync(finalRegion, panelId);
    }

    /// <inheritdoc />
    public async Task ExecuteNavCommandAsync(string commandId, string fallbackPanelId, PanelRegion fallbackRegion, string buttonName)
    {
        if (MainWindowSmokeStartupModeShellBridge.EvaluateSafeStartup())
        {
            if (await OpenPanelByIdAsync(fallbackPanelId, fallbackRegion))
                _setActiveNavButton(buttonName);
            return;
        }

        if (_commandRouter != null)
        {
            var success = await _commandRouter.ExecuteSafeAsync(commandId);
            if (success)
            {
                Debug.WriteLine($"[ShellNavigation] Nav command succeeded via CommandRouter: {commandId}");
                _setActiveNavButton(buttonName);
                return;
            }
            Debug.WriteLine($"[ShellNavigation] Nav command failed; falling back to OpenPanelByIdAsync: {fallbackPanelId}");
        }

        if (await OpenPanelByIdAsync(fallbackPanelId, fallbackRegion))
            _setActiveNavButton(buttonName);
    }

    /// <inheritdoc />
    public string ResolvePanelIdAlias(string panelId)
    {
        var lower = panelId.ToLowerInvariant();
        return lower switch
        {
            "studio" or "home" or "timeline" => "Timeline",
            "profiles" => "Profiles",
            "library" => "Library",
            "effects" => "EffectsMixer",
            "train" => "Training",
            "analyze" or "analyzer" => "Analyzer",
            "settings" => "Settings",
            "logs" => "Diagnostics",
            "synthesis" => "VoiceSynthesis",
            "profilesview" => "Profiles",
            "timelineview" => "Timeline",
            "effectsmixer" or "effectsmixerview" => "EffectsMixer",
            "macro" or "macroview" or "macros" => "Macro",
            "analyzerview" => "Analyzer",
            "libraryview" => "Library",
            "script_editor" or "scripteditor" => "ScriptEditor",
            _ => panelId,
        };
    }

    /// <inheritdoc />
    public PanelRegion GetPanelRegion(string panelId)
    {
        var registry = AppServices.GetPanelRegistry();
        if (registry.TryGetDescriptor(panelId, out var descriptor) && descriptor != null)
            return descriptor.DefaultRegion;

        if (LegacyRegistry.TryGetValue(panelId, out var legacy))
            return legacy.DefaultRegion;

        return PanelRegion.Center;
    }

    /// <inheritdoc />
    public string GetPanelTitle(string panelId)
    {
        var registry = AppServices.GetPanelRegistry();
        if (registry.TryGetDescriptor(panelId, out var descriptor) && descriptor != null)
            return descriptor.DisplayName;

        if (LegacyRegistry.TryGetValue(panelId, out var legacy))
            return legacy.Title;

        return panelId;
    }

    private async Task<bool> SwitchToPanelByIdAsync(PanelRegion region, string panelId)
    {
        var targetHost = _getPanelHost(region);
        if (targetHost == null)
            return false;

        var legacyFactory = _getLegacyFactory(panelId);
        var panel = await targetHost.LoadPanelAsync(panelId, legacyFactory);
        if (panel == null)
            return false;

        var title = GetPanelTitle(panelId);
        if (!_isGateCSmokeMode())
            _showQuickSwitchIndicator(title, region, targetHost);

        return true;
    }
}
