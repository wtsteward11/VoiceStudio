using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.App.Views;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;
using Windows.System;
using Windows.Storage;
using Windows.Foundation;
using Windows.UI;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Diagnostics;
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Views.Dialogs;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.Logging;
using VoiceStudio.App.Logging;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App
{
    public sealed partial class MainWindow : Window
    {
        private readonly KeyboardShortcutService _keyboardShortcutService;
        private readonly IUpdateService _updateService;
        private readonly PanelStateService? _panelStateService;
        private readonly RecentProjectsService? _recentProjectsService;
        private readonly CommandRouter? _commandRouter;
        private IStartupStateService? _startupStateService;
        private bool _disposed;
        private bool _isMiniTimelineVisible;
        private bool GetShowExperimentalPanels()
        {
            var featureFlags = AppServices.TryGetFeatureFlagsService();
            return featureFlags?.IsEnabled("ShowExperimentalPanels") ?? false;
        }

        // Phase 0: avoid MenuBar XAML compiler crashes by creating menu items in code.
        private MenuFlyoutSubItem? _recentProjectsSubMenu;
        private MenuFlyoutItem? _toggleMiniTimelineMenuItem;
        private MenuFlyoutItem? _customizeToolbarMenuItem;
        private MenuFlyoutItem? _checkForUpdatesMenuItem;
        private MenuFlyoutItem? _keyboardShortcutsMenuItem;
        private MenuFlyoutItem? _manageWorkspacesMenuItem;

        private GlobalTransportControl? _globalTransport;
        private StatusBarCoordinator? _statusBarCoordinator;
        private bool _recordedShellInteractiveTiming;
        private TransportShortcutCoordinator? _transportShortcutCoordinator;
        private IShellNavigationCoordinator? _shellNavigationCoordinator;
        private MainWindowNavigationShellBridge _navShellBridge = null!;
        private readonly MainWindowSearchOverlayShellBridge _searchOverlayShellBridge;
        private readonly MainWindowToolbarCustomizationShellBridge _toolbarCustomizationShellBridge;
        private readonly MainWindowCommandPaletteShellBridge _commandPaletteShellBridge;
        private readonly MainWindowToolCatalogShellBridge _toolCatalogShellBridge;
        private readonly MainWindowToolCatalogPanelHostChromeShellBridge _toolCatalogPanelHostChromeShellBridge;
        private readonly MainWindowToolbarCommandShellBridge _toolbarCommandShellBridge;
        private readonly MainWindowJumpListTaskbarProgressShellBridge _jumpListTaskbarProgressShellBridge;
        private readonly MainWindowStartupWelcomeActivationShellBridge _startupWelcomeActivationShellBridge;
        private readonly MainWindowStartupOverlayShellBridge _startupOverlayShellBridge;
        private readonly MainWindowLifetimeCleanupShellBridge _lifetimeCleanupShellBridge;
        private readonly MainWindowFileActivationShellBridge _fileActivationShellBridge;
        private readonly MainWindowJumpListDispatchShellBridge _jumpListDispatchShellBridge;
        private readonly MainWindowNotificationCenterShellBridge _notificationCenterShellBridge;
        private readonly MainWindowStatusStripClockShellBridge _statusStripClockShellBridge;
        private readonly MainWindowStatusStripMetricsShellBridge _statusStripMetricsShellBridge;
        private readonly MainWindowStatusBarCoordinatorShellBridge _statusBarCoordinatorShellBridge;
        private readonly MainWindowMenuToolActivationShellBridge _menuToolActivationShellBridge;
        private readonly MainWindowKeyboardShortcutsShellBridge _keyboardShortcutsShellBridge;
        private readonly MainWindowHelpAboutShellBridge _helpAboutShellBridge;
        private readonly MainWindowEditUndoRedoShellBridge _editUndoRedoShellBridge;
        private readonly MainWindowGlobalTransportShellBridge _globalTransportShellBridge;
        private readonly MainWindowImportWorkflowShellBridge _importWorkflowShellBridge;
        private readonly MainWindowShellChromeShellBridge _shellChromeShellBridge;
        private IProjectWorkflowCoordinator? _projectWorkflowCoordinator;
        private readonly MainWindowProjectWorkflowBridge _projectWorkflowCommandBridge;
        private readonly MainWindowRecentProjectsMutationBridge _recentProjectsMutationBridge;
        private readonly MainWindowRecentProjectsMenuPopulationShellBridge _recentProjectsMenuPopulationBridge;
        private readonly MainWindowKeyboardShortcutRegistrationShellBridge _keyboardShortcutRegistrationShellBridge;
        private readonly MainWindowPanelQuickSwitchShortcutRegistrationShellBridge _panelQuickSwitchShortcutRegistrationShellBridge;
        private readonly MainWindowPanelPreviewShellBridge _panelPreviewShellBridge;
        private readonly MainWindowPanelQuickSwitchShellBridge _panelQuickSwitchShellBridge;
        private readonly MainWindowPanelRegionFocusShellBridge _panelRegionFocusShellBridge;
        private readonly MainWindowPanelDockShellBridge _panelDockShellBridge;
        private readonly MainWindowSessionLifecycle _sessionLifecycle = new();

        internal IProjectWorkflowCoordinator? GetProjectWorkflowCoordinatorForSessionLifecycle() =>
            _projectWorkflowCoordinator;

        private Debouncer? _layoutSaveDebouncer;
        private readonly MainWindowWorkspaceSplitterShellBridge _workspaceSplitterShellBridge;
        private readonly MainWindowMenuBarShellBridge _menuBarShellBridge;

        /// <summary>
        /// Gets the unified panel registry from DI container.
        /// Use CreatePanelFromRegistry() for panel creation.
        /// </summary>
        private IPanelRegistry UnifiedPanelRegistry => AppServices.GetPanelRegistry();

        /// <summary>
        /// Creates a panel using the unified registry with DI-resolved ViewModels.
        /// </summary>
        /// <param name="panelId">The panel ID to create.</param>
        /// <returns>A UserControl instance with ViewModel set, or null if not found.</returns>
        private UserControl? CreatePanelFromRegistry(string panelId)
        {
            try
            {
                if (UnifiedPanelRegistry.TryGetDescriptor(panelId, out var descriptor) && descriptor != null)
                {
                    var panel = UnifiedPanelRegistry.CreatePanel(panelId);
                    return panel as UserControl;
                }

                // Fall back to legacy registry if panel not in unified registry
                if (_legacyPanelRegistry.TryGetValue(panelId, out var legacyEntry))
                {
                    Debug.WriteLine(
                      $"[MainWindow] Panel '{panelId}' using legacy factory (migrate to unified registry)");
                    return legacyEntry.Factory();
                }

                Debug.WriteLine($"[MainWindow] Panel '{panelId}' not found in any registry");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Failed to create panel '{panelId}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the default region for a panel. Delegates to ShellNavigationCoordinator when available.
        /// </summary>
        private PanelRegion GetPanelRegion(string panelId) => _navShellBridge.GetPanelRegion(panelId);

        /// <summary>
        /// Gets the display name for a panel. Delegates through navigation shell bridge.
        /// </summary>
        private string GetPanelTitle(string panelId) => _navShellBridge.GetPanelTitle(panelId);

        /// <summary>
        /// Opens a panel by its canonical registry ID. Delegates through navigation shell bridge.
        /// </summary>
        private Task<bool> OpenPanelByIdAsync(string panelId, PanelRegion? overrideRegion = null) =>
            _navShellBridge.OpenPanelByIdAsync(panelId, overrideRegion);

        private static void SetPanelHostMeta(Controls.PanelHost? host, string title, string icon)
        {
            if (host == null) return;
            host.PanelTitle = title;
            host.PanelIcon = icon;
        }

        /// <summary>
        /// [DEPRECATED] Legacy panel registry mapping panel IDs to their factory functions.
        /// Used for backward compatibility during migration to unified PanelRegistry.
        /// New panels should be registered via CorePanelRegistrationService.
        /// </summary>
        private readonly Dictionary<string, (PanelRegion DefaultRegion, string Title, Func<UserControl> Factory)> _legacyPanelRegistry = new(StringComparer.OrdinalIgnoreCase)
        {
            // Toggle panel only — all other panels promoted to Core/Advanced/Module registration
            ["MiniTimeline"] = (PanelRegion.Bottom, "Mini Timeline", () => new MiniTimelineView()),
        };

        private T? FindInContent<T>(string name) where T : class
        {
            return (Content as FrameworkElement)?.FindName(name) as T;
        }

        private object? FindNameOnContent(string name)
        {
            return FindInContent<object>(name);
        }

        /// <summary>
        /// Dependency bundle for project workflow coordinator. All pulls happen in MainWindow constructor.
        /// </summary>
        private sealed record WorkflowDependencies(
            IStartupStateService Startup,
            IBackendClient Backend,
            IProjectsClient ProjectsClient,
            IProjectRepository ProjectRepository,
            RecentProjectsService? RecentProjects,
            IToastNotificationService? Toast,
            ILogger<ProjectWorkflowCoordinator>? Logger);

        /// <summary>
        /// Creates the project workflow coordinator from an explicit dependency bundle.
        /// Zero ServiceProvider/AppServices calls; pure composition seam.
        /// </summary>
        private IProjectWorkflowCoordinator CreateProjectWorkflowCoordinator(
            IShellNavigationCoordinator shellNav,
            WorkflowDependencies deps,
            NavButtonActionSink navButtonSink)
        {
            var getTimeline = () => (FindNameOnContent("CenterPanelHost") as Controls.PanelHost)?.HostedPanel is TimelineView tv ? tv.ViewModel : null;
            var getMixer = () => (FindNameOnContent("RightPanelHost") as Controls.PanelHost)?.HostedPanel is EffectsMixerView em ? em.ViewModel : null;
            return ProjectWorkflowBootstrap.Create(
                shellNav,
                getTimeline,
                getMixer,
                navButtonSink.Forward,
                deps.Startup,
                deps.Backend,
                deps.ProjectsClient,
                deps.ProjectRepository,
                deps.RecentProjects,
                deps.Toast,
                deps.Logger,
                ServiceProvider.GetProjectSessionDirtyState(),
                ServiceProvider.GetCrashRecoveryService());
        }

        public MainWindow()
        {
            using var profiler = PerformanceProfiler.Start("MainWindow Construction");
            profiler.Checkpoint("Start");

            this.InitializeComponent();
            profiler.Checkpoint("InitializeComponent");

            _keyboardShortcutService = ServiceProvider.GetKeyboardShortcutService();
            profiler.Checkpoint("KeyboardShortcutService Created");

            _updateService = ServiceProvider.GetUpdateService();
            profiler.Checkpoint("UpdateService Retrieved");

            _panelStateService = ServiceProvider.GetPanelStateService();
            profiler.Checkpoint("PanelStateService Retrieved");

            _layoutSaveDebouncer = new Debouncer(() => SaveWorkspaceLayout(), 2000);
            _workspaceSplitterShellBridge = new MainWindowWorkspaceSplitterShellBridge(
                FindNameOnContent,
                () => _layoutSaveDebouncer?.Invoke());

            _recentProjectsService = ServiceProvider.GetRecentProjectsService();
            profiler.Checkpoint("RecentProjectsService Retrieved");

            _commandRouter = AppServices.TryGetCommandRouter();
            profiler.Checkpoint("CommandRouter Retrieved");

            var navButtonSink = new NavButtonActionSink();

            _panelQuickSwitchShellBridge = new MainWindowPanelQuickSwitchShellBridge();
            _panelRegionFocusShellBridge = new MainWindowPanelRegionFocusShellBridge(
                r => r switch
                {
                    PanelRegion.Left => FindNameOnContent("LeftPanelHost") as Controls.PanelHost,
                    PanelRegion.Center => FindNameOnContent("CenterPanelHost") as Controls.PanelHost,
                    PanelRegion.Right => FindNameOnContent("RightPanelHost") as Controls.PanelHost,
                    PanelRegion.Bottom => FindNameOnContent("BottomPanelHost") as Controls.PanelHost,
                    _ => null
                },
                IsGateCSmokeMode,
                (name, region, host) => _panelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator(name, region, host));

            // Shell navigation coordination (Premium Reliability Pass Task 9)
            _shellNavigationCoordinator = new ShellNavigationCoordinator(
                r => r switch
                {
                    PanelRegion.Left => FindNameOnContent("LeftPanelHost") as Controls.PanelHost,
                    PanelRegion.Center => FindNameOnContent("CenterPanelHost") as Controls.PanelHost,
                    PanelRegion.Right => FindNameOnContent("RightPanelHost") as Controls.PanelHost,
                    PanelRegion.Bottom => FindNameOnContent("BottomPanelHost") as Controls.PanelHost,
                    _ => null
                },
                FindNameOnContent,
                id => _legacyPanelRegistry.TryGetValue(id, out var e) ? e.Factory : null,
                navButtonSink.Forward,
                _panelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator,
                IsGateCSmokeMode,
                _panelStateService,
                _commandRouter);
            profiler.Checkpoint("ShellNavigationCoordinator Created");

            _navShellBridge = new MainWindowNavigationShellBridge(
                _shellNavigationCoordinator!,
                DispatcherQueue,
                FindNameOnContent,
                navButtonSink);
            profiler.Checkpoint("MainWindowNavigationShellBridge Created");

            _panelDockShellBridge = new MainWindowPanelDockShellBridge(
                r => r switch
                {
                    PanelRegion.Left => FindNameOnContent("LeftPanelHost") as Controls.PanelHost,
                    PanelRegion.Center => FindNameOnContent("CenterPanelHost") as Controls.PanelHost,
                    PanelRegion.Right => FindNameOnContent("RightPanelHost") as Controls.PanelHost,
                    PanelRegion.Bottom => FindNameOnContent("BottomPanelHost") as Controls.PanelHost,
                    _ => null
                },
                OpenPanelByIdAsync,
                _panelStateService,
                () => _layoutSaveDebouncer?.Invoke(),
                () => ServiceProvider.TryGetToastNotificationService());
            profiler.Checkpoint("MainWindowPanelDockShellBridge Created");

            _panelPreviewShellBridge = new MainWindowPanelPreviewShellBridge(DispatcherQueue);
            profiler.Checkpoint("MainWindowPanelPreviewShellBridge Created");

            var searchOverlayCoordinator = new SearchOverlayCoordinator(FindNameOnContent, _shellNavigationCoordinator!);
            profiler.Checkpoint("SearchOverlayCoordinator Created");
            _searchOverlayShellBridge = new MainWindowSearchOverlayShellBridge(searchOverlayCoordinator, FindNameOnContent);
            profiler.Checkpoint("MainWindowSearchOverlayShellBridge Created");

            // Initialize Toast Notification Service before coordinator (ToastContainer available after InitializeComponent)
            var toastContainer = FindInContent<StackPanel>("ToastContainer");
            if (toastContainer != null)
            {
                var toastService = new ToastNotificationService(toastContainer);
                ServiceProvider.RegisterToastNotificationService(toastService);
                profiler.Checkpoint("ToastNotificationService Initialized");
            }

            _toolbarCustomizationShellBridge = new MainWindowToolbarCustomizationShellBridge(
                () => this.Content?.XamlRoot,
                new ToolbarCustomizationDialogLauncher(),
                () => ServiceProvider.TryGetToastNotificationService());
            profiler.Checkpoint("MainWindowToolbarCustomizationShellBridge Created");

            _commandPaletteShellBridge = new MainWindowCommandPaletteShellBridge(
                () => ServiceProvider.GetPanelRegistry(),
                () => new ThemeManager(),
                new CommandPaletteShellLauncher(),
                () => ServiceProvider.TryGetToastNotificationService());
            profiler.Checkpoint("MainWindowCommandPaletteShellBridge Created");

            _toolbarCommandShellBridge = AppServices.GetRequiredService<MainWindowToolbarCommandShellBridge>();
            _toolbarCommandShellBridge.WireImportAudioHandler(ImportAudioFile);
            profiler.Checkpoint("MainWindowToolbarCommandShellBridge Wired");

            _toolCatalogPanelHostChromeShellBridge = new MainWindowToolCatalogPanelHostChromeShellBridge();
            _toolCatalogShellBridge = new MainWindowToolCatalogShellBridge(
                () => this.Content?.XamlRoot,
                new ToolCatalogShellLauncher(),
                () => ServiceProvider.TryGetToastNotificationService());
            _toolCatalogShellBridge.WireToolCatalogHandlers(
                (panelId, region) => OpenPanelByIdAsync(panelId, region),
                (r, t, i) => _toolCatalogPanelHostChromeShellBridge.Apply(r, t, i, FindNameOnContent));
            profiler.Checkpoint("MainWindowToolCatalogShellBridge Wired");

            _jumpListTaskbarProgressShellBridge = new MainWindowJumpListTaskbarProgressShellBridge(
                () => WinRT.Interop.WindowNative.GetWindowHandle(this));
            profiler.Checkpoint("MainWindowJumpListTaskbarProgressShellBridge Created");

            _startupWelcomeActivationShellBridge = new MainWindowStartupWelcomeActivationShellBridge(
                IsGateCSmokeMode,
                IsSafeStartupMode,
                MainWindow_KeyDown);
            profiler.Checkpoint("StartupWelcomeActivationShellBridge Created");

            _startupOverlayShellBridge = new MainWindowStartupOverlayShellBridge(
                () => FindInContent<Border>("StartupOverlay"),
                () => FindInContent<TextBlock>("StartupOverlayMessage"),
                () => FindInContent<ProgressRing>("StartupProgressRing"),
                () => FindInContent<Button>("StartupRetryButton"),
                DispatcherQueue,
                () =>
                {
                    if (_recordedShellInteractiveTiming)
                    {
                        return;
                    }

                    _recordedShellInteractiveTiming = true;
                    ColdStartTimingCollector.RecordShellInteractive();
                },
                () => AppServices.GetService<StartupRetryCoordinator>());
            profiler.Checkpoint("MainWindowStartupOverlayShellBridge Created");

            var workflowDeps = new WorkflowDependencies(
                ServiceProvider.GetStartupStateService(),
                ServiceProvider.GetBackendClient(),
                AppServices.GetProjectsClient(),
                AppServices.GetProjectRepository(),
                ServiceProvider.TryGetRecentProjectsService(),
                ServiceProvider.TryGetToastNotificationService(),
                AppServices.GetService<ILogger<ProjectWorkflowCoordinator>>());
            _projectWorkflowCoordinator = CreateProjectWorkflowCoordinator(_shellNavigationCoordinator!, workflowDeps, navButtonSink);
            profiler.Checkpoint("ProjectWorkflowCoordinator Created");
            _fileActivationShellBridge = new MainWindowFileActivationShellBridge(
                () => _projectWorkflowCoordinator,
                () => ServiceProvider.GetStartupStateService(),
                () => ServiceProvider.TryGetToastNotificationService(),
                () => _shellNavigationCoordinator);
            profiler.Checkpoint("MainWindowFileActivationShellBridge Created");
            _jumpListDispatchShellBridge = new MainWindowJumpListDispatchShellBridge(
                () => _projectWorkflowCoordinator,
                () => ServiceProvider.GetStartupStateService(),
                () => ServiceProvider.TryGetToastNotificationService());
            profiler.Checkpoint("MainWindowJumpListDispatchShellBridge Created");
            _notificationCenterShellBridge = new MainWindowNotificationCenterShellBridge(
                () => AppServices.GetService<NotificationCenterViewModel>(),
                () => FindInContent<Button>("NotificationCenterButton"),
                () => FindInContent<FrameworkElement>("NotificationCenterFlyoutRoot"),
                () => FindInContent<ListView>("NotificationCenterList"),
                () => FindInContent<Border>("UnreadBadge"),
                () => FindInContent<TextBlock>("UnreadBadgeText"),
                DispatcherQueue);
            profiler.Checkpoint("MainWindowNotificationCenterShellBridge Created");
            _statusStripClockShellBridge = new MainWindowStatusStripClockShellBridge(
                () => FindNameOnContent("ClockText") as TextBlock,
                DispatcherQueue,
                () => _disposed);
            profiler.Checkpoint("MainWindowStatusStripClockShellBridge Created");
            _statusStripMetricsShellBridge = new MainWindowStatusStripMetricsShellBridge(
                () => FindNameOnContent("CpuText") as TextBlock,
                () => FindNameOnContent("GpuText") as TextBlock,
                () => FindNameOnContent("RamText") as TextBlock,
                () => FindNameOnContent("LatencyText") as TextBlock,
                () => ServiceProvider.GetHealthVersionClient(),
                () => ServiceProvider.GetTelemetryClient());
            profiler.Checkpoint("MainWindowStatusStripMetricsShellBridge Created");
            _statusBarCoordinatorShellBridge = new MainWindowStatusBarCoordinatorShellBridge();
            profiler.Checkpoint("MainWindowStatusBarCoordinatorShellBridge Created");
            _menuToolActivationShellBridge = new MainWindowMenuToolActivationShellBridge();
            profiler.Checkpoint("MainWindowMenuToolActivationShellBridge Created");
            _keyboardShortcutsShellBridge = new MainWindowKeyboardShortcutsShellBridge();
            profiler.Checkpoint("MainWindowKeyboardShortcutsShellBridge Created");
            _helpAboutShellBridge = new MainWindowHelpAboutShellBridge();
            profiler.Checkpoint("MainWindowHelpAboutShellBridge Created");
            _editUndoRedoShellBridge = new MainWindowEditUndoRedoShellBridge();
            profiler.Checkpoint("MainWindowEditUndoRedoShellBridge Created");
            _globalTransportShellBridge = new MainWindowGlobalTransportShellBridge();
            profiler.Checkpoint("MainWindowGlobalTransportShellBridge Created");
            _importWorkflowShellBridge = new MainWindowImportWorkflowShellBridge();
            profiler.Checkpoint("MainWindowImportWorkflowShellBridge Created");
            _shellChromeShellBridge = new MainWindowShellChromeShellBridge(this, RootGrid, AppTitleBar);
            profiler.Checkpoint("MainWindowShellChromeShellBridge Created");
            _projectWorkflowCommandBridge = new MainWindowProjectWorkflowBridge(() => _projectWorkflowCoordinator);
            profiler.Checkpoint("MainWindowProjectWorkflowBridge Created");
            _recentProjectsMutationBridge = new MainWindowRecentProjectsMutationBridge(
                () => (IRecentProjectsMutationCommands?)_recentProjectsService,
                () => (IToastNotificationService?)ServiceProvider.GetToastNotificationService());
            profiler.Checkpoint("MainWindowRecentProjectsMutationBridge Created");
            _recentProjectsMenuPopulationBridge = new MainWindowRecentProjectsMenuPopulationShellBridge(
                (path, name) => _projectWorkflowCommandBridge.OpenRecentProjectAsync(path, name),
                path => _recentProjectsMutationBridge.PinRecentProjectAsync(path),
                path => _recentProjectsMutationBridge.UnpinRecentProjectAsync(path),
                path => _recentProjectsMutationBridge.RemoveFromRecentListAsync(path),
                () => _recentProjectsMutationBridge.ClearRecentProjectsAsync());
            profiler.Checkpoint("MainWindowRecentProjectsMenuPopulationShellBridge Created");

            _keyboardShortcutRegistrationShellBridge = new MainWindowKeyboardShortcutRegistrationShellBridge();
            _keyboardShortcutRegistrationShellBridge.Register(
                _keyboardShortcutService,
                new MainWindowKeyboardShortcutRegistrationDependencies(
                    CreateNewProject,
                    OpenProject,
                    SaveProject,
                    ImportAudioFile,
                    () =>
                    {
                        _editUndoRedoShellBridge.ExecuteUndo(
                            () => ServiceProvider.GetUndoRedoService(),
                            (ex, ctx) => ServiceProvider.TryGetErrorLoggingService()?.LogError(ex, ctx));
                    },
                    () =>
                    {
                        _editUndoRedoShellBridge.ExecuteRedo(
                            () => ServiceProvider.GetUndoRedoService(),
                            (ex, ctx) => ServiceProvider.TryGetErrorLoggingService()?.LogError(ex, ctx));
                    },
                    ShowCommandPalette,
                    () => { _ = ShowToolCatalogAsync(); },
                    () => _searchOverlayShellBridge.Show(),
                    () => FindNameOnContent("CenterPanelHost") as Controls.PanelHost,
                    _globalTransportShellBridge,
                    _panelRegionFocusShellBridge,
                    () =>
                    {
                        if (_keyboardShortcutsMenuItem != null)
                        {
                            KeyboardShortcutsMenuItem_Click(_keyboardShortcutsMenuItem, new RoutedEventArgs());
                        }
                    }));
            _panelQuickSwitchShortcutRegistrationShellBridge = new MainWindowPanelQuickSwitchShortcutRegistrationShellBridge();
            _panelQuickSwitchShortcutRegistrationShellBridge.RegisterAll(
                _keyboardShortcutService,
                GetPanelTitle,
                (panelId, region) => OpenPanelByIdAsync(panelId, region));
            profiler.Checkpoint("Keyboard Shortcuts Registered");

            // Menu items (not in XAML during Phase 0)
            _recentProjectsSubMenu = new MenuFlyoutSubItem { Text = "Recent Projects" };
            _toggleMiniTimelineMenuItem = new MenuFlyoutItem { Text = "Toggle Mini Timeline" };
            _toggleMiniTimelineMenuItem.Click += ToggleMiniTimelineMenuItem_Click;
            _customizeToolbarMenuItem = new MenuFlyoutItem { Text = "Customize Toolbar..." };
            _customizeToolbarMenuItem.Click += CustomizeToolbarMenuItem_Click;
            _checkForUpdatesMenuItem = new MenuFlyoutItem { Text = "Check for Updates..." };
            _checkForUpdatesMenuItem.Click += CheckForUpdatesMenuItem_Click;
            _keyboardShortcutsMenuItem = new MenuFlyoutItem { Text = "Keyboard Shortcuts" };
            _keyboardShortcutsMenuItem.Click += KeyboardShortcutsMenuItem_Click;
            _manageWorkspacesMenuItem = new MenuFlyoutItem { Text = "Manage Workspaces..." };
            _manageWorkspacesMenuItem.Click += ManageWorkspaces_Click;
            profiler.Checkpoint("Menu Items Created");

            _menuBarShellBridge = new MainWindowMenuBarShellBridge(
                () => FindInContent<ContentControl>("MenuBarHost"),
                UnifiedPanelRegistry,
                new MainWindowMenuBarShellWire
                {
                    RecentProjectsSubMenu = _recentProjectsSubMenu,
                    CommandRouter = _commandRouter,
                    ToggleMiniTimelineMenuItem = _toggleMiniTimelineMenuItem,
                    CustomizeToolbarMenuItem = _customizeToolbarMenuItem,
                    ManageWorkspacesMenuItem = _manageWorkspacesMenuItem,
                    CheckForUpdatesMenuItem = _checkForUpdatesMenuItem,
                    KeyboardShortcutsMenuItem = _keyboardShortcutsMenuItem
                },
                new MainWindowMenuBarCommandCallbacks
                {
                    NewProject = CreateNewProject,
                    OpenProject = OpenProject,
                    SaveProject = SaveProject,
                    ImportAudioFile = ImportAudioFile,
                    CloseWindow = () => Close(),
                    ExecuteUndo = ExecuteUndo,
                    ExecuteRedo = ExecuteRedo,
                    ShowGlobalSearch = ShowGlobalSearch,
                    ExecuteNavCommand = ExecuteNavCommand,
                    OpenPanelByIdAsync = OpenPanelByIdAsync,
                    OpenDocumentationFolder = OpenDocumentationFolder,
                    ShowAboutDialog = ShowAboutDialog,
                    TogglePlayback = TogglePlayback,
                    StopPlayback = StopPlayback,
                    ToggleRecording = ToggleRecording,
                    GetShowExperimentalPanels = GetShowExperimentalPanels
                });
            _menuBarShellBridge.InitializeMenuBar();
            profiler.Checkpoint("Menu Bar Initialized");

            // Enable keyboard navigation - will attach in MainWindow_Activated handler
            // Also register Activated handler for welcome dialog
            this.Activated += MainWindow_Activated;
            profiler.Checkpoint("Event Handlers Registered");

            // Startup overlay: visible until backend ready (STARTUP_ORCHESTRATION_HARDENING_PLAN)
            try
            {
                _startupStateService = ServiceProvider.GetStartupStateService();
                _startupStateService.StateChanged += StartupState_StateChanged;
                _startupOverlayShellBridge.ApplyStartupOverlay(
                    _startupStateService.CurrentState,
                    _startupStateService.FailureMessage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Startup overlay init failed: {ex.Message}");
            }
            profiler.Checkpoint("Startup Overlay Subscribed");

            // DEBUG: Add AddHandler with handledEventsToo to capture ALL pointer events (after Loaded)
            if (this.Content is FrameworkElement contentFE)
            {
                contentFE.Loaded += async (s, e) =>
                {
                    await MainWindowShellLoadedBootstrap.RunAsync(
                        contentFE,
                        new MainWindowLoadedBootstrapHooks
                        {
                            SetErrorDialogRoot = root => { ErrorDialogService.Root = root; },
                            WireNotificationCenter = () => _notificationCenterShellBridge.WireNotificationCenter(),
                            WireJumpListShell = () => _jumpListTaskbarProgressShellBridge.WireJumpList(),
                            WireTaskbarProgressShell = () => _jumpListTaskbarProgressShellBridge.WireTaskbarProgress(),
                            TryDispatchPendingJumpListActivation = () => _jumpListDispatchShellBridge.TryDispatchPendingJumpListActivation(),
                            TryDispatchPendingFileActivation = () => _fileActivationShellBridge.TryDispatchPendingFileActivation(),
                            StartBackendHealthMonitoring = () =>
                                _statusBarCoordinatorShellBridge.StartBackendHealthMonitoring(_statusBarCoordinator),
                            EnqueueRecentProjectsMenuRefresh = () =>
                                this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                                {
                                    _recentProjectsService?.EnsureRecentDataLoaded();
                                    PopulateRecentProjectsMenu();
                                }),
                            AttachSessionRecoveryHandlers = () => _sessionLifecycle.AttachRecoveryHandlers(this, contentFE),
                            InitializeThemeAsync = async () =>
                            {
                                try
                                {
                                    var themeService = AppServices.TryGetThemeService();
                                    if (themeService != null)
                                    {
                                        await themeService.InitializeAsync(contentFE);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Theme] Failed to initialize: {ex.Message}");
                                }
                            },
                            InitializeKeyboardShortcutsAsync = async () =>
                            {
                                try
                                {
                                    await _keyboardShortcutService.InitializeAsync();
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Shortcuts] Failed to load customizations: {ex.Message}");
                                }
                            },
                            ApplyMicaBackdrop = ApplyMicaBackdrop,
                            InitializeCustomTitleBar = InitializeCustomTitleBar,
                        }).ConfigureAwait(true);

#if DEBUG
                    // Log visual tree info after loaded (DEBUG only - avoid production file I/O)
                    var diagPath = System.IO.Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
              "VoiceStudio", "crashes", "visualtree_diag.txt");
                    try
                    {
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(diagPath)!);
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"[{DateTime.UtcNow:O}] Visual Tree Diagnostic (Loaded event)");
                        sb.AppendLine($"  Content.ActualWidth: {contentFE.ActualWidth}");
                        sb.AppendLine($"  Content.ActualHeight: {contentFE.ActualHeight}");
                        sb.AppendLine($"  Content.IsLoaded: {contentFE.IsLoaded}");
                        sb.AppendLine($"  Content.XamlRoot is null: {contentFE.XamlRoot == null}");
                        if (contentFE.XamlRoot != null)
                        {
                            sb.AppendLine($"  Content.XamlRoot.Size: {contentFE.XamlRoot.Size}");
                            sb.AppendLine($"  Content.XamlRoot.IsHostVisible: {contentFE.XamlRoot.IsHostVisible}");
                        }
                        System.IO.File.WriteAllText(diagPath, sb.ToString());
                    }
                    // ALLOWED: empty catch - diagnostic file write is best-effort
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Diagnostic write failed: {ex.Message}"); }

                    // Add pointer event handler (DEBUG only - avoid production file I/O on every click)
                    contentFE.AddHandler(
              UIElement.PointerPressedEvent,
              new Microsoft.UI.Xaml.Input.PointerEventHandler((sender, args) =>
              {
                        var point = args.GetCurrentPoint(null);
                        var inputDiagPath = System.IO.Path.Combine(
                  Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                  "VoiceStudio", "crashes", "input_diag.txt");
                        try
                        {
                            System.IO.File.AppendAllText(inputDiagPath, $"[{DateTime.UtcNow:O}] PointerPressed at ({point.Position.X:F0}, {point.Position.Y:F0}) Handled={args.Handled}\n");
                        }
                  // ALLOWED: empty catch - diagnostic file write is best-effort
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Input diagnostic write failed: {ex.Message}"); }
                    }),
              true); // handledEventsToo = true
#endif
                // GAP-008 Slice 3: transport attach + panel-init trigger (after shell bootstrap and DEBUG block).
                MainWindowLoadedTailBootstrap.Run(
                    new MainWindowLoadedTailHooks
                    {
                        RunTransportAttachAndAssign = () =>
                        {
                            // Transport shortcut orchestration (Transport Coherence Wave 4 Phase 1)
                            _transportShortcutCoordinator = AppServices.GetService<TransportShortcutCoordinator>();
                            _transportShortcutCoordinator?.Attach(
                                _keyboardShortcutService,
                                () => _globalTransportShellBridge.OpenRecordingPanelFromTransportShortcut(
                                    () => ServiceProvider.GetStartupStateService(),
                                    () => ServiceProvider.TryGetToastNotificationService(),
                                    () => AppServices.TryGetEventAggregator()));
                        },
                        RunPanelInitFireAndForget = () =>
                        {
                            // FIX: Defer panel initialization to the Loaded event.
                            // XamlRoot is guaranteed non-null here; it is still null during the constructor,
                            // so any popup/dialog created during panel init previously threw
                            // COMException "Catastrophic failure — XamlRoot must be explicitly set for unparented popup."
                            // Round 4 Task 1: Defer panel init until BackendReady to avoid backend calls during startup.
                            _ = RunPanelInitWhenReadyAsync(
                                FindNameOnContent("LeftPanelHost") as Controls.PanelHost,
                                FindNameOnContent("CenterPanelHost") as Controls.PanelHost,
                                FindNameOnContent("RightPanelHost") as Controls.PanelHost,
                                FindNameOnContent("BottomPanelHost") as Controls.PanelHost);
                        },
                    });
                };
            }

            // Set PanelRegion for each PanelHost
            var navRailBorder = FindInContent<FrameworkElement>("NavRailBorder");
            if (navRailBorder != null)
            {
                navRailBorder.DataContext = new Views.Shell.NavigationViewModel();
            }

            var leftPanelHost = FindNameOnContent("LeftPanelHost") as Controls.PanelHost;
            var centerPanelHost = FindNameOnContent("CenterPanelHost") as Controls.PanelHost;
            var rightPanelHost = FindNameOnContent("RightPanelHost") as Controls.PanelHost;
            var bottomPanelHost = FindNameOnContent("BottomPanelHost") as Controls.PanelHost;
            if (leftPanelHost != null) leftPanelHost.PanelRegion = PanelRegion.Left;
            if (centerPanelHost != null) centerPanelHost.PanelRegion = PanelRegion.Center;
            if (rightPanelHost != null) rightPanelHost.PanelRegion = PanelRegion.Right;
            if (bottomPanelHost != null) bottomPanelHost.PanelRegion = PanelRegion.Bottom;
            profiler.Checkpoint("PanelRegions Set");

            // Wire up panel docking handlers (IDEA 14) — GAP-008 Slice 25: MainWindowPanelDockShellBridge
            if (leftPanelHost != null) leftPanelHost.OnPanelDockRequested += _panelDockShellBridge.OnPanelDockRequested;
            if (centerPanelHost != null) centerPanelHost.OnPanelDockRequested += _panelDockShellBridge.OnPanelDockRequested;
            if (rightPanelHost != null) rightPanelHost.OnPanelDockRequested += _panelDockShellBridge.OnPanelDockRequested;
            if (bottomPanelHost != null) bottomPanelHost.OnPanelDockRequested += _panelDockShellBridge.OnPanelDockRequested;
            profiler.Checkpoint("Panel Docking Handlers Wired");

            profiler.Checkpoint("Workspace Layout Ready");

            // Subscribe to workspace profile changes
            if (_panelStateService != null)
            {
                _panelStateService.WorkspaceProfileChanged += OnWorkspaceProfileChanged;
            }
            profiler.Checkpoint("Workspace Profile Subscription");

            // Subscribe to navigation service events (for command-driven navigation)
            try
            {
                var navigationService = ServiceProvider.TryGetNavigationService();
                if (navigationService != null)
                {
                    _navShellBridge.AttachNavigationService(navigationService);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Failed to subscribe to NavigationService: {ex.Message}");
            }
            profiler.Checkpoint("NavigationService Subscription");

            // Panel initialization deferred to contentFE.Loaded (above) — XamlRoot is null here in the constructor.

            // Start status bar metrics timer (Slice 18 — MainWindowStatusStripMetricsShellBridge)
            _statusStripMetricsShellBridge.BeginMetricsTimer();

            // Update menu item state for Mini Timeline toggle (IDEA 6)
            UpdateMiniTimelineMenuItem();

            // Save workspace layout on window close
            this.Closed += MainWindow_Closed;

            // Wire up Global Search navigation
            var globalSearchView = FindNameOnContent("GlobalSearchView") as Views.GlobalSearchView;
            if (globalSearchView != null)
            {
                globalSearchView.NavigateRequested += GlobalSearchView_NavigateRequested;
            }

            // Wire up global transport strip to main Play/Stop (Task 5: delegate to orchestrator)
            var bootstrap = AppServices.GetService<TransportOrchestrationBootstrap>();
            bootstrap?.SetGetTimelineController(() =>
            {
                var host = FindNameOnContent("CenterPanelHost") as Controls.PanelHost;
                if (host?.HostedPanel is TimelineView tv && tv.ViewModel is ITimelineTransportController ctrl)
                    return ctrl;
                return null;
            });
            _globalTransport = FindNameOnContent("GlobalTransportControl") as Controls.GlobalTransportControl;
            if (_globalTransport != null)
            {
                _globalTransport.PlayRequested += OnPlayRequested;
                _globalTransport.StopRequested += OnStopRequested;
            }

            // Status bar orchestration (Transport Coherence Wave 3 Task 5) — shell wiring via Slice 19 bridge
            _statusBarCoordinator = _statusBarCoordinatorShellBridge.ResolveAttachSubscribe(
                () => AppServices.GetService<StatusBarCoordinator>(),
                DispatcherQueue,
                FindNameOnContent,
                AppServices.GetContextManager(),
                AppServices.TryGetStatusBarActivityService(),
                AppServices.GetService<GracefulDegradationService>());

            // Recent projects menu: populated on Loaded (low priority) — GAP-067 slice 7 cold-start

            // Subscribe to recent projects changes
            if (_recentProjectsService != null)
            {
                _recentProjectsService.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(RecentProjectsService.AllProjects) ||
                        e.PropertyName == nameof(RecentProjectsService.PinnedProjects) ||
                        e.PropertyName == nameof(RecentProjectsService.RecentProjects))
                    {
                        PopulateRecentProjectsMenu();
                    }
                };
            }

            profiler.Checkpoint("Status Bar Coordinated");

            // Start clock timer (1-minute wall clock on ClockText; metrics on MainWindowStatusStripMetricsShellBridge)
            _statusStripClockShellBridge.BeginClockTimer();

            _lifetimeCleanupShellBridge = new MainWindowLifetimeCleanupShellBridge(
                new MainWindowClosedPreludeChannels
                {
                    StopStatusBarTimer = () => _statusStripMetricsShellBridge.StopMetricsTimer(),
                    CancelLayoutSaveDebouncer = () => _layoutSaveDebouncer?.Cancel(),
                    SaveWorkspaceLayout = SaveWorkspaceLayout,
                    TryMarkCleanShutdown = () => _sessionLifecycle.TryMarkCleanShutdown(),
                },
                new MainWindowLifetimeCleanupCoreChannels
                {
                    GetDisposed = () => _disposed,
                    SetDisposed = () => { _disposed = true; },
                    DisposeClockTimer = () => _statusStripClockShellBridge.DisposeClockTimer(),
                    DisposePreviewHideTimer = () => _panelPreviewShellBridge.DisposePreviewHideTimer(),
                    DisposeQuickSwitchHideTimer = () => _panelQuickSwitchShellBridge.DisposeQuickSwitchHideTimer(),
                    CancelDebouncerAndSaveWorkspace = () =>
                    {
                        _layoutSaveDebouncer?.Cancel();
                        SaveWorkspaceLayout();
                    },
                    UnsubscribeContentKeyDown = () =>
                    {
                        if (this.Content is UIElement root)
                        {
                            root.KeyDown -= MainWindow_KeyDown;
                        }
                    },
                    UnsubscribeWindowActivated = () => { this.Activated -= MainWindow_Activated; },
                    UnsubscribeWindowClosed = () => { this.Closed -= MainWindow_Closed; },
                    UnsubscribeWorkspaceProfileChanged = () =>
                    {
                        if (_panelStateService != null)
                        {
                            _panelStateService.WorkspaceProfileChanged -= OnWorkspaceProfileChanged;
                        }
                    },
                    DetachNavigationService = () => _navShellBridge.DetachNavigationService(),
                    UnsubscribeStartupOverlay = () =>
                    {
                        if (_startupStateService != null)
                        {
                            _startupStateService.StateChanged -= StartupState_StateChanged;
                            _startupStateService = null;
                        }
                    },
                    DisposeSessionLifecycle = () => _sessionLifecycle.Dispose(),
                    DetachTransportShortcutsAndClear = () =>
                    {
                        _transportShortcutCoordinator?.Detach();
                        _transportShortcutCoordinator = null;
                    },
                    UnsubscribeStatusBarCoordinator = () =>
                    {
                        _statusBarCoordinator?.Unsubscribe();
                        _statusBarCoordinator = null;
                    },
                    DisposeJumpListServiceBestEffort = () =>
                    {
                        try
                        {
                            AppServices.TryGetJumpListService()?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[MainWindow] JumpListService dispose: {ex.Message}");
                        }
                    },
                    DisposeTaskbarProgressServiceBestEffort = () =>
                    {
                        try
                        {
                            AppServices.TryGetTaskbarProgressService()?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[MainWindow] TaskbarProgressService dispose: {ex.Message}");
                        }
                    },
                    CleanupNotificationCenterViewModel = () => _notificationCenterShellBridge.CleanupNotificationCenter(),
                    CleanupGlobalTransportEvents = () =>
                    {
                        if (_globalTransport != null)
                        {
                            _globalTransport.PlayRequested -= OnPlayRequested;
                            _globalTransport.StopRequested -= OnStopRequested;
                            _globalTransport = null;
                        }
                    },
                    UnsubscribeShellChromeEvents = UnsubscribeShellChromeEvents,
                });
            profiler.Checkpoint("LifetimeCleanupShellBridge Created");

            if (IsSafeStartupMode())
                Debug.WriteLine("[Startup] SAFE_STARTUP enabled -- skipping welcome/overlays");

            _searchOverlayShellBridge.EnsureGlobalSearchOverlayCollapsed();
            var globalSearchOverlay = FindNameOnContent("GlobalSearchOverlay") as FrameworkElement;
            var collaborationPanel = FindNameOnContent("CollaborationPanel") as FrameworkElement;
            if (collaborationPanel != null)
                collaborationPanel.Visibility = Visibility.Collapsed;
            Debug.WriteLine($"[Startup] GlobalSearchOverlay={globalSearchOverlay?.Visibility ?? Visibility.Collapsed}, CollaborationPanel={collaborationPanel?.Visibility ?? Visibility.Collapsed}");

            profiler.Checkpoint("MainWindow Construction Complete");

            ColdStartTimingCollector.CaptureMainWindowConstructionCheckpoints(profiler);

            Debug.WriteLine(profiler.GetReport());
        }

        #region Navigation Button Click Handlers

        /// <summary>
        /// Executes a navigation command. Delegates through <see cref="MainWindowNavigationShellBridge"/>.
        /// </summary>
        private void ExecuteNavCommand(string commandId, string fallbackPanelId, PanelRegion fallbackRegion, string buttonName) =>
            _navShellBridge.ExecuteNavCommand(commandId, fallbackPanelId, fallbackRegion, buttonName);

        /// <summary>
        /// Async variant for smoke tests and callers that need to wait for panel load.
        /// </summary>
        private Task ExecuteNavCommandAsync(string commandId, string fallbackPanelId, PanelRegion fallbackRegion, string buttonName) =>
            _navShellBridge.ExecuteNavCommandAsync(commandId, fallbackPanelId, fallbackRegion, buttonName);

        /// <summary>
        /// Updates rail toggle checked state (partial call sites; implementation on navigation shell bridge).
        /// </summary>
        private void SetActiveNavButton(string activeButtonName) => _navShellBridge.SetActiveNavButton(activeButtonName);

        #endregion Navigation Button Click Handlers

        #region Panel Preview on Hover (IDEA 20) — GAP-008 Slice 23 shell

        /// <summary>
        /// Handles pointer entered event for navigation buttons to show panel preview.
        /// </summary>
        private void NavButton_PointerEntered(object sender, PointerRoutedEventArgs e) =>
            _panelPreviewShellBridge.OnNavButtonPointerEntered(sender, e);

        /// <summary>
        /// Handles pointer exited event for navigation buttons to hide panel preview.
        /// </summary>
        private void NavButton_PointerExited(object sender, PointerRoutedEventArgs e) =>
            _panelPreviewShellBridge.OnNavButtonPointerExited(sender, e);

        #endregion Panel Preview on Hover (IDEA 20) — GAP-008 Slice 23 shell

        // Panel Docking (IDEA 14) — GAP-008 Slice 25: MainWindowPanelDockShellBridge ( ctor + event wiring ).

        private async void GlobalSearchView_NavigateRequested(object? sender, Views.SearchNavigationEventArgs e) =>
            await _searchOverlayShellBridge.OnNavigateRequestedAsync(e).ConfigureAwait(true);

        private void StartupState_StateChanged(object? sender, StartupStateChangedEventArgs e) =>
            _startupOverlayShellBridge.OnStartupStateChanged(e);

        private async void StartupRetryButton_Click(object sender, RoutedEventArgs e) =>
            await _startupOverlayShellBridge.OnRetryButtonClickAsync().ConfigureAwait(true);

        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
        {
            try
            {
                await _startupWelcomeActivationShellBridge.HandleActivatedAsync(this, e).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Activated handler failed: {ex.Message}", "MainWindow.MainWindow_Activated");
            }
        }

        private static bool IsSafeStartupMode()
        {
            var v = Environment.GetEnvironmentVariable("VOICESTUDIO_SAFE_STARTUP");
            return string.Equals(v, "1", StringComparison.Ordinal) || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGateCSmokeMode()
        {
            try
            {
                static bool IsTruthy(string? value)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return false;
                    }

                    return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                        || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                }

                if (IsTruthy(Environment.GetEnvironmentVariable("VOICE_STUDIO_SMOKE_EXIT"))
                    || IsTruthy(Environment.GetEnvironmentVariable("VOICE_STUDIO_SMOKE_UI")))
                {
                    return true;
                }

                var raw = Environment.CommandLine ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(raw)
                    && (raw.IndexOf("--smoke", StringComparison.OrdinalIgnoreCase) >= 0
                        || raw.IndexOf("--ui-smoke", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }

                foreach (var arg in Environment.GetCommandLineArgs())
                {
                    if (arg.Equals("--smoke-exit", StringComparison.OrdinalIgnoreCase)
                        || arg.Equals("--smoke-ui", StringComparison.OrdinalIgnoreCase)
                        || arg.Equals("--ui-smoke", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Switches to a panel and shows visual feedback (IDEA 1).
        /// </summary>
        [Obsolete("Use OpenPanelByIdAsync. Direct content assignment is forbidden.", error: true)]
        private void SwitchToPanel(PanelRegion region, string panelName, Func<UserControl> panelFactory)
        {
            // Get the target PanelHost
            Controls.PanelHost? targetHost = region switch
            {
                PanelRegion.Left => FindNameOnContent("LeftPanelHost") as Controls.PanelHost,
                PanelRegion.Center => FindNameOnContent("CenterPanelHost") as Controls.PanelHost,
                PanelRegion.Right => FindNameOnContent("RightPanelHost") as Controls.PanelHost,
                PanelRegion.Bottom => FindNameOnContent("BottomPanelHost") as Controls.PanelHost,
                _ => null
            };

            if (targetHost == null)
                return;

            // Switch panel content
            targetHost.HostedPanel = panelFactory();

            if (IsGateCSmokeMode())
            {
                // Smoke runs should avoid extra UI animations/popups that can introduce timing flake or stalls.
                return;
            }

            // Show visual indicator
            _panelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator(panelName, region, targetHost);
        }

        private async void CheckForUpdatesMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            await _menuToolActivationShellBridge
                .RunCheckForUpdatesAsync(
                    () => ServiceProvider.GetViewModelContext(),
                    _updateService,
                    () => ServiceProvider.GetErrorDialogService())
                .ConfigureAwait(true);

        /// <summary>
        /// Toggles Mini Timeline visibility in BottomPanelHost (IDEA 6).
        /// </summary>
        private async void ToggleMiniTimelineMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            await _menuToolActivationShellBridge
                .RunToggleMiniTimelineAsync(
                    () => _isMiniTimelineVisible,
                    v => _isMiniTimelineVisible = v,
                    () => FindNameOnContent("BottomPanelHost") as Controls.PanelHost,
                    (panelId, region) => OpenPanelByIdAsync(panelId, region),
                    UpdateMiniTimelineMenuItem,
                    () => ServiceProvider.TryGetToastNotificationService())
                .ConfigureAwait(true);

        /// <summary>
        /// Updates the Mini Timeline menu item text based on current state (IDEA 6).
        /// </summary>
        private void UpdateMiniTimelineMenuItem()
        {
            if (_toggleMiniTimelineMenuItem != null)
            {
                _toggleMiniTimelineMenuItem.Text = _isMiniTimelineVisible
                    ? "Show Macro View"
                    : "Show Mini Timeline";
            }
        }

        private async void CustomizeToolbarMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            await _toolbarCustomizationShellBridge.ShowCustomizationDialogAsync().ConfigureAwait(true);

        private async void KeyboardShortcutsMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            await _keyboardShortcutsShellBridge
                .RunKeyboardShortcutsMenuFlowAsync(
                    () => this.Content?.XamlRoot,
                    () => AppServices.GetRequiredService<KeyboardCustomizationViewModel>(),
                    () => ServiceProvider.GetToastNotificationService())
                .ConfigureAwait(true);

        private void ShowCommandPalette()
        {
            _commandPaletteShellBridge.Show();
        }

        private async Task ShowToolCatalogAsync()
        {
            await _toolCatalogShellBridge.RunShowAsync().ConfigureAwait(true);
        }

        private void ShowGlobalSearch() => _searchOverlayShellBridge.Show();

        private void GlobalSearchOverlay_Tapped(object _, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) =>
            _searchOverlayShellBridge.OnOverlayTappedForDismiss(e);

        // Collaboration Panel Toggle (IDEA 25)
        private void CollaboratorsToggleButton_Click(object sender, RoutedEventArgs e) =>
            _menuToolActivationShellBridge.ToggleCollaborationPanelVisibility(
                () => FindNameOnContent("CollaborationPanel") as FrameworkElement);

        private void CollaborationIndicator_CloseRequested(object? sender, EventArgs e) =>
            _menuToolActivationShellBridge.HideCollaborationPanel(
                () => FindNameOnContent("CollaborationPanel") as FrameworkElement);

        private async void SaveProject() =>
            await _projectWorkflowCommandBridge.SaveProjectAsync().ConfigureAwait(true);

        private async void CreateNewProject() =>
            await _projectWorkflowCommandBridge.CreateNewProjectAsync().ConfigureAwait(true);

        private async void OpenProject() =>
            await _projectWorkflowCommandBridge.OpenProjectAsync().ConfigureAwait(true);

        /// <summary>
        /// Thin wrapper for import workflow. Delegates to IImportWorkflowService (Transport Coherence Wave 4 Phase 2).
        /// Gated: blocks until backend ready (Round 5 Task 2 — audit non-registry paths).
        /// </summary>
        public void ImportAudioFile() =>
            _importWorkflowShellBridge.ImportAudioFile(
                () => ServiceProvider.GetStartupStateService(),
                () => AppServices.GetService<IImportWorkflowService>(),
                (msg, title) => AppServices.TryGetToastNotificationService()?.ShowInfo(msg, title),
                () => WinRT.Interop.WindowNative.GetWindowHandle(this));

        private void ExecuteUndo() =>
            _editUndoRedoShellBridge.ExecuteUndo(
                () => ServiceProvider.GetUndoRedoService(),
                (ex, ctx) => ServiceProvider.TryGetErrorLoggingService()?.LogError(ex, ctx));

        private void ExecuteRedo() =>
            _editUndoRedoShellBridge.ExecuteRedo(
                () => ServiceProvider.GetUndoRedoService(),
                (ex, ctx) => ServiceProvider.TryGetErrorLoggingService()?.LogError(ex, ctx));

        private void OpenDocumentationFolder() =>
            _helpAboutShellBridge.OpenDocumentationFolder(
                () => Environment.GetEnvironmentVariable("VOICESTUDIO_REPO_ROOT"),
                AppContext.BaseDirectory,
                (a, b) => ServiceProvider.TryGetToastNotificationService()?.ShowWarning(a, b),
                (ex, ctx) => ServiceProvider.TryGetErrorLoggingService()?.LogError(ex, ctx),
                (a, b) => ServiceProvider.TryGetToastNotificationService()?.ShowError(a, b));

        private async void ShowAboutDialog() =>
            await _helpAboutShellBridge.ShowAboutDialogAsync(
                () => (Content as FrameworkElement)?.XamlRoot,
                (ex, ctx) => ServiceProvider.TryGetErrorLoggingService()?.LogError(ex, ctx),
                (a, b) => ServiceProvider.TryGetToastNotificationService()?.ShowError(a, b));

        private void PopulateRecentProjectsMenu() =>
            _recentProjectsMenuPopulationBridge.Populate(_recentProjectsSubMenu, _recentProjectsService);

        private void OnPlayRequested(object? sender, EventArgs e) => TogglePlayback();
        private void OnStopRequested(object? sender, EventArgs e) => StopPlayback();

        private async void TogglePlayback() =>
            await _globalTransportShellBridge.TogglePlaybackAsync(
                () => ServiceProvider.GetStartupStateService(),
                () => ServiceProvider.TryGetToastNotificationService(),
                () => AppServices.GetService<IGlobalTransportOrchestrator>());

        private void StopPlayback() =>
            _globalTransportShellBridge.StopPlayback(
                () => ServiceProvider.GetStartupStateService(),
                () => ServiceProvider.TryGetToastNotificationService(),
                () => AppServices.GetService<IGlobalTransportOrchestrator>());

        private async void ToggleRecording() =>
            await _globalTransportShellBridge.ToggleRecordingAsync(
                () => FindNameOnContent("RightPanelHost") as Controls.PanelHost,
                (panelId, region) => OpenPanelByIdAsync(panelId, region),
                () => ServiceProvider.GetStartupStateService(),
                () => ServiceProvider.TryGetToastNotificationService(),
                (ex, ctx) => ServiceProvider.TryGetErrorLoggingService()?.LogError(ex, ctx));

        private void MainWindow_Closed(object sender, WindowEventArgs e)
        {
            _lifetimeCleanupShellBridge.OnClosedPrelude();
            Cleanup();
        }

        private void MainWindow_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var modifiers = VirtualKeyModifiers.None;
            if (InputHelper.IsControlPressed())
                modifiers |= VirtualKeyModifiers.Control;
            if (InputHelper.IsShiftPressed())
                modifiers |= VirtualKeyModifiers.Shift;
            var altState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
            if ((altState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down)
                modifiers |= VirtualKeyModifiers.Menu;

            if (_keyboardShortcutService.TryHandleKeyDown(e.Key, modifiers))
            {
                e.Handled = true;
            }
        }

        private void NotificationCenterMarkAllRead_Click(object sender, RoutedEventArgs e) =>
            _notificationCenterShellBridge.OnMarkAllReadClick();

        private void NotificationCenterDismissItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is AppNotificationItem item)
            {
                _notificationCenterShellBridge.OnDismissItemClick(item);
            }
        }

        private void Cleanup()
        {
            _lifetimeCleanupShellBridge.RunCleanupCore();
        }

        ~MainWindow()
        {
            Cleanup();
        }
    }
}