// VoiceStudio - Project workflow bootstrap (MainWindow open-project seam extraction)
// Creates ProjectWorkflowCoordinator; MainWindow delegates only. Per MAINWINDOW_DECOMPOSITION_PLAN.md
// No new AppServices registration. Thin factory with explicit inputs; no service locator.

using System;
using Microsoft.Extensions.Logging;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Bootstrap factory for <see cref="ProjectWorkflowCoordinator"/>.
/// MainWindow delegates coordinator creation here; no inline orchestration.
/// Accepts explicit dependencies; no ServiceProvider/AppServices reach.
/// </summary>
public static class ProjectWorkflowBootstrap
{
    /// <summary>
    /// Creates the project workflow coordinator. All dependencies passed explicitly.
    /// </summary>
    /// <param name="shellNav">Shell navigation coordinator (required).</param>
    /// <param name="getTimeline">Resolves TimelineViewModel from content (required).</param>
    /// <param name="getMixer">Resolves EffectsMixerViewModel from content (required).</param>
    /// <param name="setActiveNavButton">Callback to update shell nav button (required).</param>
    /// <param name="startup">Startup state service (required).</param>
    /// <param name="backend">Backend client for project loading (required).</param>
    /// <param name="projectsClient">Projects transport facade for save (required).</param>
    /// <param name="projectRepository">Local project file repository (required for shell file activation).</param>
    /// <param name="recentProjects">Recent projects service (optional).</param>
    /// <param name="toast">Toast notification service (optional; may be null at init).</param>
    /// <param name="logger">Logger for workflow failures (optional).</param>
    /// <param name="sessionDirty">Project dirty state for open/save integration (optional).</param>
    /// <param name="crashRecovery">Session recovery metadata after unified save (optional).</param>
    /// <returns>Coordinator instance; ownership boundary: coordinator owns open/save/create.</returns>
    public static IProjectWorkflowCoordinator Create(
        IShellNavigationCoordinator shellNav,
        Func<TimelineViewModel?> getTimeline,
        Func<EffectsMixerViewModel?> getMixer,
        Action<string> setActiveNavButton,
        IStartupStateService startup,
        IBackendClient backend,
        IProjectsClient projectsClient,
        IProjectRepository projectRepository,
        RecentProjectsService? recentProjects = null,
        IToastNotificationService? toast = null,
        ILogger<ProjectWorkflowCoordinator>? logger = null,
        IProjectSessionDirtyState? sessionDirty = null,
        CrashRecoveryService? crashRecovery = null)
    {
        if (shellNav == null)
            throw new ArgumentNullException(nameof(shellNav));
        if (getTimeline == null)
            throw new ArgumentNullException(nameof(getTimeline));
        if (getMixer == null)
            throw new ArgumentNullException(nameof(getMixer));
        if (setActiveNavButton == null)
            throw new ArgumentNullException(nameof(setActiveNavButton));
        if (startup == null)
            throw new ArgumentNullException(nameof(startup));
        if (backend == null)
            throw new ArgumentNullException(nameof(backend));
        if (projectsClient == null)
            throw new ArgumentNullException(nameof(projectsClient));
        if (projectRepository == null)
            throw new ArgumentNullException(nameof(projectRepository));

        return new ProjectWorkflowCoordinator(
            startup,
            shellNav,
            new TimelineProjectCreateHandler(getTimeline),
            new TimelineProjectOpenHandler(getTimeline, backend, projectRepository, sessionDirty, recentProjects),
            new UnifiedProjectSaveHandler(getTimeline, getMixer, projectsClient, sessionDirty, crashRecovery),
            setActiveNavButton,
            recentProjects,
            toast,
            logger);
    }
}
