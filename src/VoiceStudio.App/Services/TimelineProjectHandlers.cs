// VoiceStudio - Timeline project workflow handlers (Credible Hardening Task 3)
// Adapters that implement IProjectCreateHandler, IProjectOpenHandler, IProjectSaveHandler.
// Resolve TimelineViewModel/EffectsMixerViewModel at call time; no direct VM reference in coordinator.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Implements project workflow handlers by delegating to Timeline and EffectsMixer ViewModels.
/// MainWindow creates this with Func getters; coordinator receives the interfaces.
/// </summary>
public sealed class TimelineProjectCreateHandler : IProjectCreateHandler
{
    private readonly Func<TimelineViewModel?> _getTimeline;

    public TimelineProjectCreateHandler(Func<TimelineViewModel?> getTimeline)
    {
        _getTimeline = getTimeline ?? throw new ArgumentNullException(nameof(getTimeline));
    }

    public async Task CreateNewAsync(CancellationToken ct = default)
    {
        var vm = _getTimeline();
        if (vm?.CreateProjectCommand.CanExecute(null) == true)
            await vm.CreateProjectCommand.ExecuteAsync(null);
        await Task.CompletedTask;
    }
}

/// <summary>
/// Handles open project picker and open by ID.
/// </summary>
public sealed class TimelineProjectOpenHandler : IProjectOpenHandler
{
    private readonly Func<TimelineViewModel?> _getTimeline;
    private readonly IBackendClient _backendClient;
    private readonly IProjectSessionDirtyState? _sessionDirty;

    public TimelineProjectOpenHandler(
        Func<TimelineViewModel?> getTimeline,
        IBackendClient backendClient,
        IProjectSessionDirtyState? sessionDirty = null)
    {
        _getTimeline = getTimeline ?? throw new ArgumentNullException(nameof(getTimeline));
        _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
        _sessionDirty = sessionDirty;
    }

    public async Task OpenProjectPickerAsync(CancellationToken ct = default)
    {
        var vm = _getTimeline();
        if (vm?.LoadProjectsCommand.CanExecute(null) == true)
            await vm.LoadProjectsCommand.ExecuteAsync(null);
        await Task.CompletedTask;
    }

    public async Task OpenProjectByIdAsync(string projectId, string projectName, CancellationToken ct = default)
    {
        var vm = _getTimeline();
        if (vm == null)
            return;

        _sessionDirty?.EnterSuppressDirtyNotifications();
        try
        {
            if (vm.Projects.Count == 0)
                await vm.LoadProjectsCommand.ExecuteAsync(null);

            var project = vm.Projects.OfType<Project>().FirstOrDefault(p => p.Id == projectId);
            if (project != null)
            {
                vm.SelectedProject = project;
                return;
            }

            var loadedProject = await _backendClient.GetProjectAsync(projectId);
            if (loadedProject != null)
            {
                vm.Projects.Add(loadedProject);
                vm.SelectedProject = loadedProject;
            }
            else
                throw new Exception("Project not found");
        }
        finally
        {
            _sessionDirty?.ExitSuppressDirtyNotifications();
        }
    }
}

/// <summary>
/// Shell Save: timeline track snapshot, mixer state, and backend project update (SQLite authority — no parallel local JSON write).
/// </summary>
public sealed class UnifiedProjectSaveHandler : IProjectSaveHandler
{
    private readonly Func<TimelineViewModel?> _getTimeline;
    private readonly Func<EffectsMixerViewModel?> _getMixer;
    private readonly IProjectsClient _projectsClient;
    private readonly IProjectSessionDirtyState? _sessionDirty;
    private readonly CrashRecoveryService? _crashRecovery;

    public UnifiedProjectSaveHandler(
        Func<TimelineViewModel?> getTimeline,
        Func<EffectsMixerViewModel?> getMixer,
        IProjectsClient projectsClient,
        IProjectSessionDirtyState? sessionDirty = null,
        CrashRecoveryService? crashRecovery = null)
    {
        _getTimeline = getTimeline ?? throw new ArgumentNullException(nameof(getTimeline));
        _getMixer = getMixer ?? throw new ArgumentNullException(nameof(getMixer));
        _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));
        _sessionDirty = sessionDirty;
        _crashRecovery = crashRecovery;
    }

    public async Task SaveProjectAsync(CancellationToken cancellationToken = default)
    {
        var timelineVm = _getTimeline();
        if (timelineVm?.SelectedProject == null)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return;
        }

        timelineVm.SnapTracksOntoSelectedProject();
        var project = timelineVm.SelectedProject;

        var mixerVm = _getMixer();
        if (mixerVm?.SaveMixerStateCommand.CanExecute(null) == true)
            await mixerVm.SaveMixerStateCommand.ExecuteAsync(null).ConfigureAwait(false);

        await _projectsClient
            .UpdateProjectAsync(project.Id, project.Name, project.Description, project.VoiceProfileIds, cancellationToken)
            .ConfigureAwait(false);

        _sessionDirty?.MarkProjectClean();
        _crashRecovery?.SetActiveProject(project.Id, null, project.Name);
        await (_crashRecovery?.SaveSessionAsync() ?? Task.CompletedTask).ConfigureAwait(false);
    }
}
