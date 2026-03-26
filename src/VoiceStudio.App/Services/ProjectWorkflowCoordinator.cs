// VoiceStudio - Project workflow coordination (Premium Proof Closure C1)
// Extracted from MainWindow.xaml.cs per MAINWINDOW_DECOMPOSITION_PLAN.md
// Credible Hardening Task 3: Coordinator depends on IProjectCreateHandler, IProjectOpenHandler, IProjectSaveHandler.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Services;

/// <summary>
/// Coordinates project workflows: create, open, save, open recent.
/// MainWindow delegates menu/commands to this coordinator; coordinator owns workflow logic.
/// Depends on narrow interfaces, not on ViewModel types.
/// </summary>
public sealed class ProjectWorkflowCoordinator : IProjectWorkflowCoordinator
{
    private readonly IStartupStateService _startup;
    private readonly IShellNavigationCoordinator _shellNav;
    private readonly IProjectCreateHandler _createHandler;
    private readonly IProjectOpenHandler _openHandler;
    private readonly IProjectSaveHandler _saveHandler;
    private readonly Action<string> _setActiveNavButton;
    private readonly RecentProjectsService? _recentProjectsService;
    private readonly IToastNotificationService? _toastService;
    private readonly ILogger<ProjectWorkflowCoordinator>? _logger;

    public ProjectWorkflowCoordinator(
        IStartupStateService startup,
        IShellNavigationCoordinator shellNav,
        IProjectCreateHandler createHandler,
        IProjectOpenHandler openHandler,
        IProjectSaveHandler saveHandler,
        Action<string> setActiveNavButton,
        RecentProjectsService? recentProjectsService = null,
        IToastNotificationService? toastService = null,
        ILogger<ProjectWorkflowCoordinator>? logger = null)
    {
        _startup = startup ?? throw new ArgumentNullException(nameof(startup));
        _shellNav = shellNav ?? throw new ArgumentNullException(nameof(shellNav));
        _createHandler = createHandler ?? throw new ArgumentNullException(nameof(createHandler));
        _openHandler = openHandler ?? throw new ArgumentNullException(nameof(openHandler));
        _saveHandler = saveHandler ?? throw new ArgumentNullException(nameof(saveHandler));
        _setActiveNavButton = setActiveNavButton ?? throw new ArgumentNullException(nameof(setActiveNavButton));
        _recentProjectsService = recentProjectsService;
        _toastService = toastService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task CreateNewProjectAsync(CancellationToken ct = default)
    {
        if (!_startup.IsReady)
        {
            _toastService?.ShowInfo("Starting VoiceStudio services…", "Please wait");
            return;
        }
        try
        {
            await _createHandler.CreateNewAsync(ct);
        }
        catch (Exception ex)
        {
            _toastService?.ShowToast(ToastType.Error, "Create Project Failed", ex.Message);
            _logger?.LogWarning(ex, "Workflow failed: {Operation}", "CreateNewProject");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Navigation to Timeline and NavStudio occurs before picker completion. On picker failure,
    /// error is surfaced via toast; nav state is not rolled back. This is intentional: the user
    /// has entered the open flow and Timeline is the target panel.
    /// </remarks>
    public async Task OpenProjectAsync(CancellationToken ct = default)
    {
        if (!_startup.IsReady)
        {
            _toastService?.ShowInfo("Starting VoiceStudio services…", "Please wait");
            return;
        }
        if (await _shellNav.OpenPanelByIdAsync("Timeline", PanelRegion.Center))
        {
            _setActiveNavButton("NavStudio");
            await Task.Delay(100, ct);
            try
            {
                await _openHandler.OpenProjectPickerAsync(ct);
            }
            catch (Exception ex)
            {
                _toastService?.ShowToast(ToastType.Error, "Open Project Failed", ex.Message);
                _logger?.LogWarning(ex, "Workflow failed: {Operation}", "OpenProject");
            }
        }
    }

    /// <inheritdoc />
    public async Task SaveProjectAsync(CancellationToken ct = default)
    {
        if (!_startup.IsReady)
        {
            _toastService?.ShowInfo("Starting VoiceStudio services…", "Please wait");
            return;
        }
        try
        {
            await _saveHandler.SaveMixerStateIfNeededAsync(ct);
        }
        catch (Exception ex)
        {
            _toastService?.ShowToast(ToastType.Error, "Save Project Failed", ex.Message);
            _logger?.LogWarning(ex, "Workflow failed: {Operation}", "SaveProject");
        }
    }

    /// <inheritdoc />
    public async Task OpenRecentProjectAsync(string projectId, string projectName, CancellationToken ct = default)
    {
        if (!_startup.IsReady)
        {
            _toastService?.ShowInfo("Starting VoiceStudio services…", "Please wait");
            return;
        }
        try
        {
            await _openHandler.OpenProjectByIdAsync(projectId, projectName, ct);
            if (_recentProjectsService != null)
                await _recentProjectsService.AddRecentProjectAsync(projectId, projectName);
            _toastService?.ShowToast(ToastType.Success, "Project Opened", $"Opened project: {projectName}");
        }
        catch (Exception ex)
        {
            _toastService?.ShowToast(ToastType.Error, "Project Not Found",
                ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? $"Could not open project: {projectName}. It may have been deleted."
                    : ex.Message);
            _logger?.LogWarning(ex, "Workflow failed: {Operation}", "OpenRecentProject");
            if (_recentProjectsService != null)
                await _recentProjectsService.RemoveRecentProjectAsync(projectId);
        }
    }
}
