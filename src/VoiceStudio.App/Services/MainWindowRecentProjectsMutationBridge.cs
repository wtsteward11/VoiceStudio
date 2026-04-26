using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 5: thin façade for recent-project Pin/Unpin/Clear/Remove actions + toasts.
/// Does not build flyouts or forward File New/Open/Save / open-recent (Slice 4 coordinator seam) — not a dumping ground for other shell glue.
/// </summary>
public sealed class MainWindowRecentProjectsMutationBridge
{
    private readonly Func<IRecentProjectsMutationCommands?> _getMutations;
    private readonly Func<IToastNotificationService?> _getToast;

    public MainWindowRecentProjectsMutationBridge(
        Func<IRecentProjectsMutationCommands?> getMutations,
        Func<IToastNotificationService?> getToast)
    {
        ArgumentNullException.ThrowIfNull(getMutations);
        ArgumentNullException.ThrowIfNull(getToast);
        _getMutations = getMutations;
        _getToast = getToast;
    }

    public async Task PinRecentProjectAsync(string projectPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var mutations = _getMutations();
        if (mutations == null)
        {
            return;
        }

        try
        {
            await mutations.PinProjectAsync(projectPath).ConfigureAwait(true);
            _getToast()?.ShowToast(
                ToastType.Success,
                "Project Pinned",
                "Project pinned to Recent Projects menu");
        }
        catch (Exception ex)
        {
            _getToast()?.ShowToast(
                ToastType.Error,
                "Failed to Pin Project",
                ex.Message);
        }
    }

    public async Task UnpinRecentProjectAsync(string projectPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var mutations = _getMutations();
        if (mutations == null)
        {
            return;
        }

        try
        {
            await mutations.UnpinProjectAsync(projectPath).ConfigureAwait(true);
            _getToast()?.ShowToast(
                ToastType.Success,
                "Project Unpinned",
                "Project removed from pinned list");
        }
        catch (Exception ex)
        {
            _getToast()?.ShowToast(
                ToastType.Error,
                "Failed to Unpin Project",
                ex.Message);
        }
    }

    public async Task ClearRecentProjectsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var mutations = _getMutations();
        if (mutations == null)
        {
            return;
        }

        try
        {
            await mutations.ClearRecentProjectsAsync().ConfigureAwait(true);
            _getToast()?.ShowToast(
                ToastType.Success,
                "Recent Projects Cleared",
                "All recent projects have been cleared");
        }
        catch (Exception ex)
        {
            _getToast()?.ShowToast(
                ToastType.Error,
                "Failed to Clear Recent Projects",
                ex.Message);
        }
    }

    /// <summary>
    /// Matches pre-Slice-5 inline remove handler: no try/catch, no success toast.
    /// </summary>
    /// <param name="projectPath">Path of the project entry to remove from the recent list.</param>
    public Task RemoveFromRecentListAsync(string projectPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var mutations = _getMutations();
        return mutations != null
            ? mutations.RemoveRecentProjectAsync(projectPath)
            : Task.CompletedTask;
    }
}
