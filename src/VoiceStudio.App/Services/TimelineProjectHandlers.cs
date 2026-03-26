// VoiceStudio - Timeline project workflow handlers (Credible Hardening Task 3)
// Adapters that implement IProjectCreateHandler, IProjectOpenHandler, IProjectSaveHandler.
// Resolve TimelineViewModel/EffectsMixerViewModel at call time; no direct VM reference in coordinator.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;
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

    public TimelineProjectOpenHandler(Func<TimelineViewModel?> getTimeline, IBackendClient backendClient)
    {
        _getTimeline = getTimeline ?? throw new ArgumentNullException(nameof(getTimeline));
        _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
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

        if (vm.Projects.Count == 0)
            await vm.LoadProjectsCommand.ExecuteAsync(null);

        var project = vm.Projects.OfType<Project>().FirstOrDefault(p => p.Id == projectId);
        if (project != null)
        {
            vm.SelectedProject = project;
            return;
        }

        try
        {
            var loadedProject = await _backendClient.GetProjectAsync(projectId);
            if (loadedProject != null)
            {
                vm.Projects.Add(loadedProject);
                vm.SelectedProject = loadedProject;
            }
            else
                throw new Exception("Project not found");
        }
        catch
        {
            throw;
        }
    }
}

/// <summary>
/// Handles saving mixer state when a project is selected.
/// </summary>
public sealed class MixerProjectSaveHandler : IProjectSaveHandler
{
    private readonly Func<TimelineViewModel?> _getTimeline;
    private readonly Func<EffectsMixerViewModel?> _getMixer;

    public MixerProjectSaveHandler(Func<TimelineViewModel?> getTimeline, Func<EffectsMixerViewModel?> getMixer)
    {
        _getTimeline = getTimeline ?? throw new ArgumentNullException(nameof(getTimeline));
        _getMixer = getMixer ?? throw new ArgumentNullException(nameof(getMixer));
    }

    public async Task SaveMixerStateIfNeededAsync(CancellationToken ct = default)
    {
        var timelineVm = _getTimeline();
        if (timelineVm?.SelectedProject == null)
        {
            await Task.CompletedTask;
            return;
        }

        var mixerVm = _getMixer();
        if (mixerVm?.SaveMixerStateCommand.CanExecute(null) == true)
        {
            try
            {
                await mixerVm.SaveMixerStateCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "MixerProjectSaveHandler");
            }
        }
        await Task.CompletedTask;
    }
}
