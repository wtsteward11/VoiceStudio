using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 4: thin façade from MainWindow File menu and open-recent handler to
/// <see cref="IProjectWorkflowCoordinator"/>. Does not own coordinator lifetime; does not build recent menus (Choice A).
/// </summary>
public sealed class MainWindowProjectWorkflowBridge
{
    private readonly Func<IProjectWorkflowCoordinator?> _getCoordinator;

    public MainWindowProjectWorkflowBridge(Func<IProjectWorkflowCoordinator?> getCoordinator)
    {
        ArgumentNullException.ThrowIfNull(getCoordinator);
        _getCoordinator = getCoordinator;
    }

    public Task CreateNewProjectAsync(CancellationToken ct = default)
    {
        var c = _getCoordinator();
        return c != null ? c.CreateNewProjectAsync(ct) : Task.CompletedTask;
    }

    public Task OpenProjectAsync(CancellationToken ct = default)
    {
        var c = _getCoordinator();
        return c != null ? c.OpenProjectAsync(ct) : Task.CompletedTask;
    }

    public Task SaveProjectAsync(CancellationToken ct = default)
    {
        var c = _getCoordinator();
        return c != null ? c.SaveProjectAsync(ct) : Task.CompletedTask;
    }

    public Task OpenRecentProjectAsync(string projectId, string projectName, CancellationToken ct = default)
    {
        var c = _getCoordinator();
        return c != null ? c.OpenRecentProjectAsync(projectId, projectName, ct) : Task.CompletedTask;
    }
}
