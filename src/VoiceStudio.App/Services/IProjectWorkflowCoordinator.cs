// VoiceStudio - Project workflow coordination (Premium Proof Closure C1)
// Extracted from MainWindow.xaml.cs per MAINWINDOW_DECOMPOSITION_PLAN.md

using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Coordinates project workflows: create, open, save, open recent.
/// MainWindow delegates menu/commands to this coordinator; coordinator owns workflow logic.
/// </summary>
public interface IProjectWorkflowCoordinator
{
    Task CreateNewProjectAsync(CancellationToken ct = default);
    Task OpenProjectAsync(CancellationToken ct = default);
    Task SaveProjectAsync(CancellationToken ct = default);

    /// <summary>
    /// Autosave path: same <see cref="IProjectSaveHandler"/> as manual save; failures are logged, not toast-spammed.
    /// </summary>
    Task TryAutosaveProjectAsync(CancellationToken ct = default);

    Task OpenRecentProjectAsync(string projectId, string projectName, CancellationToken ct = default);
}
