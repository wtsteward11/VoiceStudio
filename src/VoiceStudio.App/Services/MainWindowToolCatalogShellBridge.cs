using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 10: MainWindow shell wiring for tool catalog only — not command palette (Slice 8);
/// not search overlay; not toolbar customization; not toolbar command routing (Slice 9).
/// </summary>
public sealed class MainWindowToolCatalogShellBridge
{
    private readonly Func<XamlRoot?> _getXamlRoot;
    private readonly IToolCatalogShellLauncher _launcher;
    private readonly Func<IToastNotificationService?> _getToast;
    private readonly IToolCatalogShellDiagnostics _diagnostics;
    private Func<string, PanelRegion?, Task<bool>>? _openPanelByIdAsync;
    private Action<PanelRegion, string, string?>? _applyPanelHostChrome;

    public MainWindowToolCatalogShellBridge(
        Func<XamlRoot?> getXamlRoot,
        IToolCatalogShellLauncher launcher,
        Func<IToastNotificationService?> getToast,
        IToolCatalogShellDiagnostics? diagnostics = null)
    {
        _getXamlRoot = getXamlRoot ?? throw new ArgumentNullException(nameof(getXamlRoot));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _getToast = getToast ?? throw new ArgumentNullException(nameof(getToast));
        _diagnostics = diagnostics ?? ToolCatalogShellErrorDiagnostics.Instance;
    }

    /// <summary>
    /// Wires panel-open and chrome callbacks from <see cref="MainWindow"/> composition (call once when shell is ready).
    /// </summary>
    public void WireToolCatalogHandlers(
        Func<string, PanelRegion?, Task<bool>> openPanelByIdAsync,
        Action<PanelRegion, string, string?> applyPanelHostChrome)
    {
        _openPanelByIdAsync = openPanelByIdAsync ?? throw new ArgumentNullException(nameof(openPanelByIdAsync));
        _applyPanelHostChrome = applyPanelHostChrome ?? throw new ArgumentNullException(nameof(applyPanelHostChrome));
    }

    /// <summary>
    /// Opens the tool catalog from the shell (keyboard / future menu paths).
    /// </summary>
    public async Task RunShowAsync()
    {
        var root = _getXamlRoot();
        // Use "is null" — do not use "==" : WinUI XamlRoot overload can dereference ThisPtr on invalid handles.
        if (root is null)
        {
            var toastEarly = _getToast();
            toastEarly?.ShowError(
                "Tool Catalog is unavailable: XamlRoot is not ready yet.",
                "Tool Catalog");
            return;
        }

        try
        {
            var choice = await _launcher.ShowAsync(root).ConfigureAwait(true);
            if (choice == null)
            {
                return;
            }

            var open = _openPanelByIdAsync
                ?? throw new InvalidOperationException(
                    "Tool catalog shell is not wired: MainWindow must call WireToolCatalogHandlers before opening a panel from the catalog.");
            var opened = await open(choice.PanelId, choice.EffectiveRegion).ConfigureAwait(true);
            if (!opened)
            {
                return;
            }

            var apply = _applyPanelHostChrome
                ?? throw new InvalidOperationException(
                    "Tool catalog shell is not wired: MainWindow must call WireToolCatalogHandlers before applying panel chrome.");
            apply(choice.EffectiveRegion, choice.DisplayName, choice.Icon);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("Tool catalog shell is not wired", StringComparison.Ordinal))
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"Tool catalog failed: {ex.Message}";
            var context = new Dictionary<string, object>
            {
                ["ExceptionType"] = ex.GetType().FullName ?? string.Empty
            };
            _diagnostics.LogToolCatalogFailure(
                message,
                "MainWindowToolCatalogShellBridge.RunShowAsync",
                ex,
                context);
            var toastService = _getToast();
            toastService?.ShowError(ex.Message, "Tool Catalog");
        }
    }
}
