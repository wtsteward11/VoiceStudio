using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
using System.IO;
using Microsoft.UI.Xaml.Media.Animation;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Views.Dialogs;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel;
using Microsoft.Extensions.Logging;
using VoiceStudio.App.Logging;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Events;

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
        private const string ShowWelcomeKey = "ShowWelcomeDialog";
        private bool _disposed;
        private bool _welcomeDialogShown;
        private System.Threading.Timer? _clockTimer;
        private PanelPreviewPopup? _panelPreviewPopup;
        private System.Threading.Timer? _previewHideTimer;
        private Popup? _panelQuickSwitchPopup;
        private PanelQuickSwitchIndicator? _panelQuickSwitchIndicator;
        private DispatcherTimer? _quickSwitchHideTimer;
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

        // Workspace splitter drag state (WinUI 3 has no built-in GridSplitter)
        private enum SplitterKind { None, Vertical1, Vertical2, Horizontal }
        private SplitterKind _activeSplitter;

        private GlobalTransportControl? _globalTransport;
        private StatusBarCoordinator? _statusBarCoordinator;
        private TransportShortcutCoordinator? _transportShortcutCoordinator;
        private IShellNavigationCoordinator? _shellNavigationCoordinator;
        private ISearchOverlayCoordinator? _searchOverlayCoordinator;
        private IProjectWorkflowCoordinator? _projectWorkflowCoordinator;
        private readonly MainWindowSessionLifecycle _sessionLifecycle = new();

        internal IProjectWorkflowCoordinator? GetProjectWorkflowCoordinatorForSessionLifecycle() =>
            _projectWorkflowCoordinator;

        private Debouncer? _layoutSaveDebouncer;
        private double _splitterStartX;
        private double _splitterStartY;
        private double _splitterStartLeft;
        private double _splitterStartCenter;
        private double _splitterStartRight;
        private double _splitterStartTop;
        private double _splitterStartBottom;
        private const double MinStarValue = 0.5;

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
        private PanelRegion GetPanelRegion(string panelId)
        {
            return _shellNavigationCoordinator?.GetPanelRegion(panelId) ?? PanelRegion.Center;
        }

        /// <summary>
        /// Gets the display name for a panel. Delegates to ShellNavigationCoordinator when available.
        /// </summary>
        private string GetPanelTitle(string panelId)
        {
            return _shellNavigationCoordinator?.GetPanelTitle(panelId) ?? panelId;
        }

        /// <summary>
        /// Opens a panel by its canonical registry ID. Delegates to ShellNavigationCoordinator.
        /// </summary>
        private async Task<bool> OpenPanelByIdAsync(string panelId, PanelRegion? overrideRegion = null)
        {
            return _shellNavigationCoordinator != null
                ? await _shellNavigationCoordinator.OpenPanelByIdAsync(panelId, overrideRegion)
                : false;
        }

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
            RecentProjectsService? RecentProjects,
            IToastNotificationService? Toast,
            ILogger<ProjectWorkflowCoordinator>? Logger);

        /// <summary>
        /// Creates the project workflow coordinator from an explicit dependency bundle.
        /// Zero ServiceProvider/AppServices calls; pure composition seam.
        /// </summary>
        private IProjectWorkflowCoordinator CreateProjectWorkflowCoordinator(IShellNavigationCoordinator shellNav, WorkflowDependencies deps)
        {
            var getTimeline = () => (FindNameOnContent("CenterPanelHost") as Controls.PanelHost)?.Content is TimelineView tv ? tv.ViewModel : null;
            var getMixer = () => (FindNameOnContent("RightPanelHost") as Controls.PanelHost)?.Content is EffectsMixerView em ? em.ViewModel : null;
            return ProjectWorkflowBootstrap.Create(
                shellNav,
                getTimeline,
                getMixer,
                SetActiveNavButton,
                deps.Startup,
                deps.Backend,
                deps.ProjectsClient,
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

            _recentProjectsService = ServiceProvider.GetRecentProjectsService();
            profiler.Checkpoint("RecentProjectsService Retrieved");

            _commandRouter = AppServices.TryGetCommandRouter();
            profiler.Checkpoint("CommandRouter Retrieved");

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
                SetActiveNavButton,
                ShowPanelQuickSwitchIndicator,
                IsGateCSmokeMode,
                _panelStateService,
                _commandRouter);
            profiler.Checkpoint("ShellNavigationCoordinator Created");

            _searchOverlayCoordinator = new SearchOverlayCoordinator(FindNameOnContent, _shellNavigationCoordinator!);
            profiler.Checkpoint("SearchOverlayCoordinator Created");

            // Initialize Toast Notification Service before coordinator (ToastContainer available after InitializeComponent)
            var toastContainer = FindInContent<StackPanel>("ToastContainer");
            if (toastContainer != null)
            {
                var toastService = new ToastNotificationService(toastContainer);
                ServiceProvider.RegisterToastNotificationService(toastService);
                profiler.Checkpoint("ToastNotificationService Initialized");
            }

            var workflowDeps = new WorkflowDependencies(
                ServiceProvider.GetStartupStateService(),
                ServiceProvider.GetBackendClient(),
                AppServices.GetProjectsClient(),
                ServiceProvider.TryGetRecentProjectsService(),
                ServiceProvider.TryGetToastNotificationService(),
                AppServices.GetService<ILogger<ProjectWorkflowCoordinator>>());
            _projectWorkflowCoordinator = CreateProjectWorkflowCoordinator(_shellNavigationCoordinator!, workflowDeps);
            profiler.Checkpoint("ProjectWorkflowCoordinator Created");

            RegisterKeyboardShortcuts();
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

            InitializeMenuBar();
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
                UpdateStartupOverlay(_startupStateService.CurrentState, _startupStateService.FailureMessage);
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
                    // Initialize canonical XamlRoot for dialogs (prevents "XamlRoot must be explicitly set for unparented popup")
                    ErrorDialogService.Root = contentFE.XamlRoot;

                    _sessionLifecycle.AttachRecoveryHandlers(this, contentFE);

                    // Initialize theme service with root element
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
                // Transport shortcut orchestration (Transport Coherence Wave 4 Phase 1)
                _transportShortcutCoordinator = AppServices.GetService<TransportShortcutCoordinator>();
                _transportShortcutCoordinator?.Attach(_keyboardShortcutService, OpenRecordingPanelFromTransportShortcut);

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

            // Wire up panel docking handlers (IDEA 14)
            if (leftPanelHost != null) leftPanelHost.OnPanelDockRequested += PanelHost_OnPanelDockRequested;
            if (centerPanelHost != null) centerPanelHost.OnPanelDockRequested += PanelHost_OnPanelDockRequested;
            if (rightPanelHost != null) rightPanelHost.OnPanelDockRequested += PanelHost_OnPanelDockRequested;
            if (bottomPanelHost != null) bottomPanelHost.OnPanelDockRequested += PanelHost_OnPanelDockRequested;
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
                    navigationService.NavigationChanged += OnNavigationChanged;
                    Debug.WriteLine("[MainWindow] Subscribed to NavigationService.NavigationChanged");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Failed to subscribe to NavigationService: {ex.Message}");
            }
            profiler.Checkpoint("NavigationService Subscription");

            // Panel initialization deferred to contentFE.Loaded (above) — XamlRoot is null here in the constructor.

            // Start status bar metrics timer
            StartStatusBarTimer();

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
                if (host?.Content is TimelineView tv && tv.ViewModel is ITimelineTransportController ctrl)
                    return ctrl;
                return null;
            });
            _globalTransport = FindNameOnContent("GlobalTransportControl") as Controls.GlobalTransportControl;
            if (_globalTransport != null)
            {
                _globalTransport.PlayRequested += OnPlayRequested;
                _globalTransport.StopRequested += OnStopRequested;
            }

            // Status bar orchestration (Transport Coherence Wave 3 Task 5)
            _statusBarCoordinator = AppServices.GetService<StatusBarCoordinator>();
            if (_statusBarCoordinator != null)
            {
                _statusBarCoordinator.Attach(DispatcherQueue, FindNameOnContent);
                _statusBarCoordinator.Subscribe(
                    AppServices.GetContextManager(),
                    AppServices.TryGetStatusBarActivityService(),
                    AppServices.GetService<GracefulDegradationService>());
            }

            // Populate Recent Projects menu (IDEA 16)
            PopulateRecentProjectsMenu();

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

            // Start clock timer
            UpdateClock();
            _clockTimer = new System.Threading.Timer(_ =>
            {
                if (!_disposed)
                {
                    this.DispatcherQueue.TryEnqueue(() => UpdateClock());
                }
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

            if (IsSafeStartupMode())
                Debug.WriteLine("[Startup] SAFE_STARTUP enabled -- skipping welcome/overlays");

            var globalSearchOverlay = FindNameOnContent("GlobalSearchOverlay") as FrameworkElement;
            var collaborationPanel = FindNameOnContent("CollaborationPanel") as FrameworkElement;
            if (globalSearchOverlay != null)
                globalSearchOverlay.Visibility = Visibility.Collapsed;
            if (collaborationPanel != null)
                collaborationPanel.Visibility = Visibility.Collapsed;
            Debug.WriteLine($"[Startup] GlobalSearchOverlay={globalSearchOverlay?.Visibility ?? Visibility.Collapsed}, CollaborationPanel={collaborationPanel?.Visibility ?? Visibility.Collapsed}");

            profiler.Checkpoint("MainWindow Construction Complete");

            Debug.WriteLine(profiler.GetReport());
        }

        #region Navigation Button Click Handlers

        /// <summary>
        /// Executes a navigation command. Delegates to ShellNavigationCoordinator.
        /// </summary>
        private void ExecuteNavCommand(string commandId, string fallbackPanelId, PanelRegion fallbackRegion, string buttonName)
        {
            _ = ExecuteNavCommandAsync(commandId, fallbackPanelId, fallbackRegion, buttonName);
        }

        /// <summary>
        /// Async variant for smoke tests and callers that need to wait for panel load.
        /// </summary>
        private async Task ExecuteNavCommandAsync(string commandId, string fallbackPanelId, PanelRegion fallbackRegion, string buttonName)
        {
            if (_shellNavigationCoordinator != null)
                await _shellNavigationCoordinator.ExecuteNavCommandAsync(commandId, fallbackPanelId, fallbackRegion, buttonName);
        }

        #endregion Navigation Button Click Handlers

        #region Command-Driven Navigation

        /// <summary>
        /// Handles navigation events from the NavigationService (command-driven navigation).
        /// </summary>
        private void OnNavigationChanged(object? sender, VoiceStudio.Core.Models.NavigationEventArgs e)
        {
            if (string.IsNullOrEmpty(e.NewPanelId))
            {
                return;
            }

            var panelId = e.NewPanelId.ToLowerInvariant();
            Debug.WriteLine($"[MainWindow] OnNavigationChanged: {panelId}");

            DispatcherQueue.TryEnqueue(() =>
            {
                _ = OnNavigationChangedCoreAsync(panelId, e.NewPanelId);
            });
        }

        private async Task OnNavigationChangedCoreAsync(string panelId, string originalPanelId)
        {
            try
            {
                var canonicalId = _shellNavigationCoordinator?.ResolvePanelIdAlias(panelId) ?? originalPanelId;

                if (await OpenPanelByIdAsync(canonicalId))
                {
                    // Nav button state updated via NavigationViewModel bindings
                }
                else
                {
                    Debug.WriteLine($"[MainWindow] Unknown panel ID in navigation: {panelId}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Navigation failed: {ex.Message}");
            }
        }

        #endregion Command-Driven Navigation

        private void SetActiveNavButton(string activeButtonName)
        {
            var navButtons = new[]
            {
        FindNameOnContent("NavStudio") as ToggleButton,
        FindNameOnContent("NavProfiles") as ToggleButton,
        FindNameOnContent("NavLibrary") as ToggleButton,
        FindNameOnContent("NavEffects") as ToggleButton,
        FindNameOnContent("NavTrain") as ToggleButton,
        FindNameOnContent("NavAnalyze") as ToggleButton,
        FindNameOnContent("NavSettings") as ToggleButton,
        FindNameOnContent("NavLogs") as ToggleButton
      };

            foreach (var navButton in navButtons)
            {
                if (navButton == null)
                {
                    continue;
                }

                navButton.IsChecked = string.Equals(navButton.Name, activeButtonName, StringComparison.Ordinal);
            }
        }

        #region Panel Preview on Hover (IDEA 20)

        /// <summary>
        /// Handles pointer entered event for navigation buttons to show panel preview.
        /// </summary>
        private void NavButton_PointerEntered(object sender, PointerRoutedEventArgs _)
        {
            if (sender is not ToggleButton button)
                return;

            // Cancel any pending hide timer
            _previewHideTimer?.Dispose();
            _previewHideTimer = null;

            // Get panel info based on button name
            var panelInfo = GetPanelInfoForButton(button.Name);
            if (panelInfo == null)
                return;

            // Create or get preview popup
            if (_panelPreviewPopup == null)
            {
                _panelPreviewPopup = new PanelPreviewPopup();
            }

            // Create preview content
            var previewContent = CreatePreviewContent(panelInfo.Value.PanelId);

            // Show preview
            _panelPreviewPopup.Show(button, panelInfo.Value.Title, panelInfo.Value.Description, panelInfo.Value.IconGlyph, previewContent);
        }

        /// <summary>
        /// Handles pointer exited event for navigation buttons to hide panel preview.
        /// </summary>
        private void NavButton_PointerExited(object _, PointerRoutedEventArgs __)
        {
            // Delay hiding to allow moving to preview popup
            _previewHideTimer?.Dispose();
            _previewHideTimer = new System.Threading.Timer(_ =>
            {
                this.DispatcherQueue.TryEnqueue(() => _panelPreviewPopup?.Hide());
            }, null, TimeSpan.FromMilliseconds(300), System.Threading.Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Gets panel information for a navigation button.
        /// </summary>
        private (string PanelId, string Title, string Description, string IconGlyph)? GetPanelInfoForButton(string buttonName)
        {
            return buttonName switch
            {
                "NavStudio" => ("Timeline", "Studio", "Main workspace for voice synthesis and editing. Access timeline, mixer, and all core tools.", "\uE8A5"),
                "NavProfiles" => ("Profiles", "Profiles", "Manage voice profiles and voice cloning models. Create, edit, and organize your voice library.", "\uE77B"),
                "NavLibrary" => ("Library", "Library", "Browse and organize your audio files, voice samples, and project assets.", "\uE8F1"),
                "NavEffects" => ("EffectsMixer", "Effects & Mixer", "Apply audio effects, adjust mixing parameters, and fine-tune your voice output.", "\uE8F5"),
                "NavTrain" => ("Training", "Voice Training", "Train custom voice models and improve voice cloning quality.", "\uE8F6"),
                "NavAnalyze" => ("Analyzer", "Analyzer", "Analyze audio quality, waveforms, spectral analysis, and voice characteristics.", "\uE890"),
                "NavSettings" => ("Settings", "Settings", "Configure application settings, preferences, and system options.", "\uE713"),
                "NavLogs" => ("Diagnostics", "Diagnostics", "View system logs, diagnostics, and debugging information.", "\uE8F7"),
                _ => null
            };
        }

        /// <summary>
        /// Creates preview content for a panel.
        /// </summary>
        private UIElement? CreatePreviewContent(string panelId)
        {
            var stackPanel = new StackPanel { Spacing = 8 };

            switch (panelId)
            {
                case "Timeline":
                    stackPanel.Children.Add(new TextBlock { Text = "• Main workspace", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Timeline and mixer", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Core synthesis tools", FontSize = 12 });
                    break;

                case "Profiles":
                    stackPanel.Children.Add(new TextBlock { Text = "• Voice profile management", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Quality score tracking", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Profile organization", FontSize = 12 });
                    break;

                case "Library":
                    stackPanel.Children.Add(new TextBlock { Text = "• Audio file browser", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Asset organization", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Quick preview", FontSize = 12 });
                    break;

                case "EffectsMixer":
                    stackPanel.Children.Add(new TextBlock { Text = "• Audio effects chain", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Mixing controls", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Real-time processing", FontSize = 12 });
                    break;

                case "Training":
                    stackPanel.Children.Add(new TextBlock { Text = "• Model training interface", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Training progress tracking", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Quality metrics", FontSize = 12 });
                    break;

                case "Analyzer":
                    stackPanel.Children.Add(new TextBlock { Text = "• Waveform visualization", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Spectral analysis", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Quality metrics", FontSize = 12 });
                    break;

                case "Settings":
                    stackPanel.Children.Add(new TextBlock { Text = "• Application preferences", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Engine configuration", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• System settings", FontSize = 12 });
                    break;

                case "Diagnostics":
                    stackPanel.Children.Add(new TextBlock { Text = "• System diagnostics", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Error logs", FontSize = 12 });
                    stackPanel.Children.Add(new TextBlock { Text = "• Performance metrics", FontSize = 12 });
                    break;

                default:
                    return null;
            }

            return stackPanel;
        }

        #endregion Panel Preview on Hover (IDEA 20)

        #region Panel Docking (IDEA 14)

        /// <summary>
        /// Handles panel dock requests from PanelHost controls.
        /// </summary>
        private void PanelHost_OnPanelDockRequested(object? sender, PanelDockEventArgs e)
        {
            if (e.SourcePanelHost == null)
                return;

            // Get the target PanelHost based on target region
            Controls.PanelHost? targetHost = e.TargetRegion switch
            {
                PanelRegion.Left => FindNameOnContent("LeftPanelHost") as Controls.PanelHost,
                PanelRegion.Center => FindNameOnContent("CenterPanelHost") as Controls.PanelHost,
                PanelRegion.Right => FindNameOnContent("RightPanelHost") as Controls.PanelHost,
                PanelRegion.Bottom => FindNameOnContent("BottomPanelHost") as Controls.PanelHost,
                _ => null
            };

            if (targetHost == null || targetHost == e.SourcePanelHost)
                return;

            // Swap panel contents
            var sourceContent = e.SourcePanelHost.Content;
            var targetContent = targetHost.Content;

            // Animate the swap
            AnimatePanelDock(e.SourcePanelHost, targetHost, sourceContent, targetContent);
        }

        /// <summary>
        /// Animates panel docking with visual feedback.
        /// After animation, unloads stale cache entries and reloads via OpenPanelByIdAsync (no direct Content bypass).
        /// </summary>
        private void AnimatePanelDock(Controls.PanelHost sourceHost, Controls.PanelHost targetHost, UIElement? sourceContent, UIElement? targetContent)
        {
            var sourceRegion = sourceHost.PanelRegion;
            var targetRegion = targetHost.PanelRegion;
            var sourcePanelId = Controls.PanelHost.TryGetPanelIdFromContent(sourceContent, out var s) ? s : null;
            var targetPanelId = Controls.PanelHost.TryGetPanelIdFromContent(targetContent, out var t) ? t : null;

            var sourceFadeOut = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            Storyboard.SetTarget(sourceFadeOut, sourceHost);
            Storyboard.SetTargetProperty(sourceFadeOut, "Opacity");

            var targetFadeIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                BeginTime = TimeSpan.FromMilliseconds(200)
            };
            Storyboard.SetTarget(targetFadeIn, targetHost);
            Storyboard.SetTargetProperty(targetFadeIn, "Opacity");

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            storyboard.Children.Add(sourceFadeOut);
            storyboard.Children.Add(targetFadeIn);

            storyboard.Completed += (_, _) =>
            {
                sourceHost.Opacity = 1;
                targetHost.Opacity = 1;
                _ = CompletePanelDockAsync(sourceHost, targetHost, sourceRegion, targetRegion, sourcePanelId, targetPanelId);
            };

            storyboard.Begin();
        }

        private async Task CompletePanelDockAsync(
          Controls.PanelHost sourceHost,
          Controls.PanelHost targetHost,
          PanelRegion sourceRegion,
          PanelRegion targetRegion,
          string? sourcePanelId,
          string? targetPanelId)
        {
            if (!string.IsNullOrEmpty(sourcePanelId))
                await sourceHost.UnloadPanelAsync(sourcePanelId);
            if (!string.IsNullOrEmpty(targetPanelId))
                await targetHost.UnloadPanelAsync(targetPanelId);

            if (!string.IsNullOrEmpty(sourcePanelId))
                _panelStateService?.MigratePanelState(sourcePanelId, sourceRegion, targetRegion);
            if (!string.IsNullOrEmpty(targetPanelId))
                _panelStateService?.MigratePanelState(targetPanelId, targetRegion, sourceRegion);

            if (!string.IsNullOrEmpty(targetPanelId))
                await OpenPanelByIdAsync(targetPanelId, sourceRegion);
            if (!string.IsNullOrEmpty(sourcePanelId))
                await OpenPanelByIdAsync(sourcePanelId, targetRegion);

            _layoutSaveDebouncer?.Invoke();

            var toastService = ServiceProvider.TryGetToastNotificationService();
            if (!string.IsNullOrEmpty(sourcePanelId) && !string.IsNullOrEmpty(targetPanelId))
            {
                toastService?.ShowSuccess("Panel Swapped",
                    $"Swapped {targetPanelId} ({sourceRegion}) \u2194 {sourcePanelId} ({targetRegion})");
            }
            else
            {
                var movedName = sourcePanelId ?? targetPanelId ?? "Panel";
                var destRegion = !string.IsNullOrEmpty(sourcePanelId) ? targetRegion : sourceRegion;
                toastService?.ShowSuccess("Panel Moved", $"Moved {movedName} -> {destRegion}");
            }
        }

        #endregion Panel Docking (IDEA 14)

        private async void GlobalSearchView_NavigateRequested(object? sender, Views.SearchNavigationEventArgs e)
        {
            if (_searchOverlayCoordinator != null)
                await _searchOverlayCoordinator.HandleNavigateRequestedAsync(e.Result).ConfigureAwait(true);
        }

        private void StartupState_StateChanged(object? sender, StartupStateChangedEventArgs e)
        {
            this.DispatcherQueue.TryEnqueue(() =>
                UpdateStartupOverlay(e.NewState, e.FailureMessage));
        }

        private void UpdateStartupOverlay(StartupState state, string? failureMessage)
        {
            var overlay = FindInContent<Border>("StartupOverlay");
            var message = FindInContent<TextBlock>("StartupOverlayMessage");
            var progress = FindInContent<ProgressRing>("StartupProgressRing");
            var retryBtn = FindInContent<Button>("StartupRetryButton");
            if (overlay == null)
                return;

            var showOverlay = state == StartupState.Starting || state == StartupState.BackendStarting || state == StartupState.BackendFailed;
            overlay.Visibility = showOverlay ? Visibility.Visible : Visibility.Collapsed;

            if (showOverlay)
            {
                if (state == StartupState.BackendFailed)
                {
                    if (message != null)
                        message.Text = string.IsNullOrEmpty(failureMessage) ? "Backend failed to start." : failureMessage;
                    if (progress != null)
                        progress.IsActive = false;
                    if (retryBtn != null)
                        retryBtn.Visibility = Visibility.Visible;
                }
                else
                {
                    if (message != null)
                        message.Text = "Starting VoiceStudio services…";
                    if (progress != null)
                        progress.IsActive = true;
                    if (retryBtn != null)
                        retryBtn.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void StartupRetryButton_Click(object sender, RoutedEventArgs e)
        {
            var coordinator = AppServices.GetService<StartupRetryCoordinator>();
            if (coordinator == null)
                return;
            var messageEl = FindInContent<TextBlock>("StartupOverlayMessage");
            var progressEl = FindInContent<ProgressRing>("StartupProgressRing");
            var retryBtn = FindInContent<Button>("StartupRetryButton");

            var progress = new Progress<StartupRetryProgress>(p =>
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    if (messageEl != null)
                        messageEl.Text = p.Message;
                    if (progressEl != null)
                        progressEl.IsActive = true;
                    if (retryBtn != null)
                        retryBtn.Visibility = Visibility.Collapsed;
                });
            });

            if (messageEl != null)
                messageEl.Text = "Retrying…";
            if (progressEl != null)
                progressEl.IsActive = true;
            if (retryBtn != null)
                retryBtn.Visibility = Visibility.Collapsed;

            await coordinator.RetryAsync(progress);
        }

        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
        {
            try
            {
                if (IsGateCSmokeMode())
                {
                    return;
                }

                if (this.Content is UIElement root)
                {
                    root.KeyDown -= MainWindow_KeyDown;
                    root.KeyDown += MainWindow_KeyDown;
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "MainWindow.MainWindow_Activated");
            }

            if (e.WindowActivationState != WindowActivationState.CodeActivated)
                return;

            if (IsSafeStartupMode())
            {
                _welcomeDialogShown = true;
                return;
            }

            if (_welcomeDialogShown)
                return;

            var showWelcome = Helpers.UnpackagedSettingsHelper.GetValue<bool>(ShowWelcomeKey, true);

            try
            {
                if (showWelcome && this.Content?.XamlRoot is not null)
                {
                    _welcomeDialogShown = true;
                    var welcomeDialog = new WelcomeView();
                    welcomeDialog.XamlRoot = this.Content.XamlRoot;
                    try
                    {
                        var showTask = welcomeDialog.ShowAsync();
                        var timeoutTask = Task.Delay(5000);
                        var completed = await Task.WhenAny(showTask.AsTask(), timeoutTask);
                        if (completed == timeoutTask)
                        {
                            Debug.WriteLine("[Startup] Welcome dialog ShowAsync timed out after 5s; continuing");
                            welcomeDialog.Hide();
                        }
                        else
                        {
                            var result = await showTask;
                            Helpers.UnpackagedSettingsHelper.SetValue(ShowWelcomeKey, welcomeDialog.ShowOnStartup);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Startup] Welcome dialog failed: {ex.Message}");
                    }
                }
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

        private void RegisterKeyboardShortcuts()
        {
            // File operations
            _keyboardShortcutService.RegisterShortcut(
                "file.new",
                VirtualKey.N,
                VirtualKeyModifiers.Control,
                () => CreateNewProject(),
                "New Project");

            _keyboardShortcutService.RegisterShortcut(
                "file.open",
                VirtualKey.O,
                VirtualKeyModifiers.Control,
                () => OpenProject(),
                "Open Project");

            _keyboardShortcutService.RegisterShortcut(
                "file.save",
                VirtualKey.S,
                VirtualKeyModifiers.Control,
                () => SaveProject(),
                "Save Project");

            _keyboardShortcutService.RegisterShortcut(
                "file.import",
                VirtualKey.I,
                VirtualKeyModifiers.Control,
                () => ImportAudioFile(),
                "Import Audio");

            // Playback shortcuts: registered by TransportShortcutCoordinator in Loaded (Wave 4 Phase 1)

            // Edit operations
            _keyboardShortcutService.RegisterShortcut(
                "edit.undo",
                VirtualKey.Z,
                VirtualKeyModifiers.Control,
                () =>
                {
                    try
                    {
                        var undoService = ServiceProvider.GetUndoRedoService();
                        if (undoService.CanUndo)
                        {
                            undoService.Undo();
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "MainWindow.RegisterKeyboardShortcuts");
                    }
                },
                "Undo");

            _keyboardShortcutService.RegisterShortcut(
                "edit.redo",
                VirtualKey.Y,
                VirtualKeyModifiers.Control,
                () =>
                {
                    try
                    {
                        var undoService = ServiceProvider.GetUndoRedoService();
                        if (undoService.CanRedo)
                        {
                            undoService.Redo();
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "MainWindow.Unknown");
                    }
                },
                "Redo");

            // Navigation
            _keyboardShortcutService.RegisterShortcut(
                "nav.commandpalette",
                VirtualKey.P,
                VirtualKeyModifiers.Control,
                () => ShowCommandPalette(),
                "Command Palette");

            _keyboardShortcutService.RegisterShortcut(
                "nav.toolcatalog",
                VirtualKey.T,
                VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
                () => { _ = ShowToolCatalogAsync(); },
                "Tool Catalog");

            // Global Search (IDEA 5)
            _keyboardShortcutService.RegisterShortcut(
                "nav.globalsearch",
                VirtualKey.K,
                VirtualKeyModifiers.Control,
                () => ShowGlobalSearch(),
                "Global Search");

            // Zoom
            _keyboardShortcutService.RegisterShortcut(
                "zoom.in",
                VirtualKey.Add,
                VirtualKeyModifiers.Control,
                () => ZoomIn(),
                "Zoom In");

            _keyboardShortcutService.RegisterShortcut(
                "zoom.out",
                VirtualKey.Subtract,
                VirtualKeyModifiers.Control,
                () => ZoomOut(),
                "Zoom Out");

            _keyboardShortcutService.RegisterShortcut(
                "zoom.reset",
                VirtualKey.Number0,
                VirtualKeyModifiers.Control,
                () => ResetZoom(),
                "Reset Zoom");

            // Help - Phase 5.2.7: Fixed to use F1 (standard help key) + Shift+/ for ? key
            _keyboardShortcutService.RegisterShortcut(
                "help.shortcuts",
                VirtualKey.F1,
                VirtualKeyModifiers.Shift,
                () =>
                {
                    if (_keyboardShortcutsMenuItem != null)
                    {
                        KeyboardShortcutsMenuItem_Click(_keyboardShortcutsMenuItem, new RoutedEventArgs());
                    }
                },
                "Keyboard Shortcuts");

            // Also register Shift+/ (?) for help on US keyboards
            _keyboardShortcutService.RegisterShortcut(
                "help.shortcuts.alt",
                (VirtualKey)191, // Forward slash key
                VirtualKeyModifiers.Shift,
                () =>
                {
                    if (_keyboardShortcutsMenuItem != null)
                    {
                        KeyboardShortcutsMenuItem_Click(_keyboardShortcutsMenuItem, new RoutedEventArgs());
                    }
                },
                "Keyboard Shortcuts (?)");

            // Panel Quick-Switch (IDEA 1): Ctrl+1-9 for direct panel switching
            // Left PanelHost: Ctrl+1-3
            RegisterPanelQuickSwitchShortcut(1, PanelRegion.Left, 0, "Profiles");
            RegisterPanelQuickSwitchShortcut(2, PanelRegion.Left, 1, "Library");
            RegisterPanelQuickSwitchShortcut(3, PanelRegion.Left, 2, "Training");

            // Center PanelHost: Ctrl+4-6
            RegisterPanelQuickSwitchShortcut(4, PanelRegion.Center, 0, "Timeline");
            RegisterPanelQuickSwitchShortcut(5, PanelRegion.Center, 1, "VoiceSynthesis");
            RegisterPanelQuickSwitchShortcut(6, PanelRegion.Center, 2, "TextSpeechEditor");

            // Right PanelHost: Ctrl+7-9
            RegisterPanelQuickSwitchShortcut(7, PanelRegion.Right, 0, "EffectsMixer");
            RegisterPanelQuickSwitchShortcut(8, PanelRegion.Right, 1, "Analyzer");
            RegisterPanelQuickSwitchShortcut(9, PanelRegion.Right, 2, "QualityControl");

            // GAP-E02: Panel region cycling and direct focus
            _keyboardShortcutService.RegisterShortcut(
                "panel.cycleNext",
                VirtualKey.Tab,
                VirtualKeyModifiers.Control,
                CyclePanelNext,
                "Cycle to Next Panel");

            _keyboardShortcutService.RegisterShortcut(
                "panel.cyclePrevious",
                VirtualKey.Tab,
                VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
                CyclePanelPrevious,
                "Cycle to Previous Panel");

            _keyboardShortcutService.RegisterShortcut(
                "panel.focusLeft",
                VirtualKey.Number1,
                VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu,
                () => FocusPanelRegion(PanelRegion.Left),
                "Focus Left Panel");

            _keyboardShortcutService.RegisterShortcut(
                "panel.focusCenter",
                VirtualKey.Number2,
                VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu,
                () => FocusPanelRegion(PanelRegion.Center),
                "Focus Center Panel");

            _keyboardShortcutService.RegisterShortcut(
                "panel.focusRight",
                VirtualKey.Number3,
                VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu,
                () => FocusPanelRegion(PanelRegion.Right),
                "Focus Right Panel");

            _keyboardShortcutService.RegisterShortcut(
                "panel.focusBottom",
                VirtualKey.Number4,
                VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu,
                () => FocusPanelRegion(PanelRegion.Bottom),
                "Focus Bottom Panel");
        }

        /// <summary>
        /// Registers a panel quick-switch shortcut (IDEA 1).
        /// </summary>
        private void RegisterPanelQuickSwitchShortcut(int number, PanelRegion region, int unused, string panelId)
        {
            VirtualKey key = number switch
            {
                1 => VirtualKey.Number1,
                2 => VirtualKey.Number2,
                3 => VirtualKey.Number3,
                4 => VirtualKey.Number4,
                5 => VirtualKey.Number5,
                6 => VirtualKey.Number6,
                7 => VirtualKey.Number7,
                8 => VirtualKey.Number8,
                9 => VirtualKey.Number9,
                _ => VirtualKey.Number1
            };

            var title = GetPanelTitle(panelId);
            _keyboardShortcutService.RegisterShortcut(
                $"nav.panel.{number}",
                key,
                VirtualKeyModifiers.Control,
                () => { _ = OpenPanelByIdAsync(panelId, region); },
                $"Switch to {title}");
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
            targetHost.Content = panelFactory();

            if (IsGateCSmokeMode())
            {
                // Smoke runs should avoid extra UI animations/popups that can introduce timing flake or stalls.
                return;
            }

            // Show visual indicator
            ShowPanelQuickSwitchIndicator(panelName, region, targetHost);
        }

        /// <summary>
        /// Shows the panel quick-switch visual indicator (IDEA 1).
        /// </summary>
        private void ShowPanelQuickSwitchIndicator(string panelName, PanelRegion region, Controls.PanelHost targetHost)
        {
            // Initialize popup if needed
            if (_panelQuickSwitchPopup == null)
            {
                _panelQuickSwitchIndicator = new PanelQuickSwitchIndicator();
                _panelQuickSwitchPopup = new Popup
                {
                    Child = _panelQuickSwitchIndicator,
                    IsLightDismissEnabled = false
                };
            }

            // Set panel info
            _panelQuickSwitchIndicator?.SetPanelInfo(panelName, region);

            // Position popup at center of target PanelHost
            var rootElement = targetHost.XamlRoot?.Content as FrameworkElement;
            if (rootElement != null)
            {
                var transform = targetHost.TransformToVisual(rootElement);
                var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                _panelQuickSwitchPopup.HorizontalOffset = point.X + (targetHost.ActualWidth / 2) - ((_panelQuickSwitchIndicator?.ActualWidth ?? 0) / 2);
                _panelQuickSwitchPopup.VerticalOffset = point.Y + (targetHost.ActualHeight / 2) - ((_panelQuickSwitchIndicator?.ActualHeight ?? 0) / 2);
            }

            _panelQuickSwitchPopup.XamlRoot = targetHost.XamlRoot;
            _panelQuickSwitchPopup.IsOpen = true;

            // Animate in
            if (_panelQuickSwitchIndicator != null)
            {
                var fadeIn = new Microsoft.UI.Xaml.Media.Animation.FadeInThemeAnimation();
                fadeIn.Duration = TimeSpan.FromMilliseconds(200); // VSQ.Animation.Duration.Fast * 2
                Storyboard.SetTarget(fadeIn, _panelQuickSwitchIndicator);
                var storyboard = new Storyboard();
                storyboard.Children.Add(fadeIn);
                storyboard.Begin();
            }

            // Hide after 1.5 seconds
            _quickSwitchHideTimer?.Stop();

            _quickSwitchHideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500) // 1.5 seconds display time
            };
            _quickSwitchHideTimer.Tick += (_, _) =>
            {
                _quickSwitchHideTimer.Stop();
                HidePanelQuickSwitchIndicator();
            };
            _quickSwitchHideTimer.Start();
        }

        /// <summary>
        /// Hides the panel quick-switch visual indicator.
        /// </summary>
        private void HidePanelQuickSwitchIndicator()
        {
            if (_panelQuickSwitchPopup?.IsOpen != true || _panelQuickSwitchIndicator == null)
                return;

            // Animate out
            var fadeOut = new Microsoft.UI.Xaml.Media.Animation.FadeOutThemeAnimation();
            fadeOut.Duration = TimeSpan.FromMilliseconds(200); // VSQ.Animation.Duration.Fast * 2
            Storyboard.SetTarget(fadeOut, _panelQuickSwitchIndicator);
            var storyboard = new Storyboard();
            storyboard.Children.Add(fadeOut);
            storyboard.Begin();

            // Close after animation
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _panelQuickSwitchPopup.IsOpen = false;
            };
            timer.Start();
        }

        #region GAP-E02: Panel Region Focus and Cycling

        /// <summary>
        /// List of panel hosts in cycling order (Left → Center → Right → Bottom).
        /// </summary>
        private readonly PanelRegion[] _panelCycleOrder = [PanelRegion.Left, PanelRegion.Center, PanelRegion.Right, PanelRegion.Bottom];

        /// <summary>
        /// Tracks the currently focused panel region for cycling.
        /// </summary>
        private int _currentPanelIndex;

        /// <summary>
        /// Cycles focus to the next panel region (Ctrl+Tab).
        /// </summary>
        private void CyclePanelNext()
        {
            _currentPanelIndex = (_currentPanelIndex + 1) % _panelCycleOrder.Length;
            FocusPanelRegion(_panelCycleOrder[_currentPanelIndex]);
        }

        /// <summary>
        /// Cycles focus to the previous panel region (Ctrl+Shift+Tab).
        /// </summary>
        private void CyclePanelPrevious()
        {
            _currentPanelIndex = (_currentPanelIndex - 1 + _panelCycleOrder.Length) % _panelCycleOrder.Length;
            FocusPanelRegion(_panelCycleOrder[_currentPanelIndex]);
        }

        /// <summary>
        /// Focuses a specific panel region for keyboard navigation.
        /// </summary>
        private void FocusPanelRegion(PanelRegion region)
        {
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

            // Update current index for cycling continuity
            _currentPanelIndex = Array.IndexOf(_panelCycleOrder, region);

            // Focus the panel host
            if (targetHost.Content is FrameworkElement content)
            {
                // Try to focus the content first (more useful for interaction)
                content.Focus(FocusState.Keyboard);
            }
            else
            {
                // Fall back to the panel host itself
                targetHost.Focus(FocusState.Keyboard);
            }

            // Show visual indicator
            string panelName = region switch
            {
                PanelRegion.Left => "Left Panel",
                PanelRegion.Center => "Center Panel",
                PanelRegion.Right => "Right Panel",
                PanelRegion.Bottom => "Bottom Panel",
                _ => "Panel"
            };

            if (!IsGateCSmokeMode())
            {
                ShowPanelQuickSwitchIndicator(panelName, region, targetHost);
            }
        }

        #endregion

        private async void CheckForUpdatesMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                // Create UpdateViewModel with the context and update service
                var context = ServiceProvider.GetViewModelContext();
                var updateViewModel = new ViewModels.UpdateViewModel(context, _updateService);

                // Create and show update dialog
                var updateDialog = new Views.UpdateDialog(updateViewModel);
                await updateDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                // Show error if update check fails
                var errorService = ServiceProvider.GetErrorDialogService();
                await errorService.ShowErrorAsync(
                    "Update Check Failed",
                    $"Unable to check for updates: {ex.Message}",
                    "OK");
            }
        }

        /// <summary>
        /// Toggles Mini Timeline visibility in BottomPanelHost (IDEA 6).
        /// </summary>
        private async void ToggleMiniTimelineMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _isMiniTimelineVisible = !_isMiniTimelineVisible;

            var bottomPanelHost = FindNameOnContent("BottomPanelHost") as Controls.PanelHost;
            if (bottomPanelHost != null)
            {
                if (_isMiniTimelineVisible)
                {
                    await OpenPanelByIdAsync("MiniTimeline", PanelRegion.Bottom);
                    SetPanelHostMeta(bottomPanelHost, "Mini Timeline", "🎬");
                }
                else
                {
                    await OpenPanelByIdAsync("Macro", PanelRegion.Bottom);
                    SetPanelHostMeta(bottomPanelHost, "Macros", "⚡");
                }
            }

            UpdateMiniTimelineMenuItem();

            // Show toast notification
            var toastService = ServiceProvider.TryGetToastNotificationService();
            toastService?.ShowSuccess(
                "Panel Switched",
                _isMiniTimelineVisible ? "Mini Timeline is now visible" : "Macro View is now visible");
        }

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

        private async void CustomizeToolbarMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                var dialog = new ToolbarCustomizationDialog();
                dialog.XamlRoot = this.Content?.XamlRoot;
                await dialog.ShowAsync();

                // Toolbar will automatically refresh via ConfigurationChanged event
            }
            catch (Exception ex)
            {
                var toastService = ServiceProvider.TryGetToastNotificationService();
                toastService?.ShowError(
                    "Customization Failed",
                    $"Could not open toolbar customization: {ex.Message}");
            }
        }

        private async void KeyboardShortcutsMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                // Show keyboard shortcuts cheat sheet (IDEA 29)
                var shortcutsView = new Views.KeyboardShortcutsView();
                var dialog = new ContentDialog
                {
                    Title = "Keyboard Shortcuts",
                    Content = shortcutsView,
                    CloseButtonText = "Close",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot,
                    Width = 800,
                    Height = 600
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                // Show error if opening documentation fails
                var toastService = ServiceProvider.GetToastNotificationService();
                toastService?.ShowToast(
                    Services.ToastType.Error,
                    "Failed to Open Documentation",
                    $"Unable to open keyboard shortcuts documentation: {ex.Message}");
            }
        }

        private void ShowCommandPalette()
        {
            try
            {
                var panelRegistry = ServiceProvider.GetPanelRegistry();
                var commandPaletteService = new CommandPaletteService(
                    panelRegistry,
                    new ThemeManager()
                );
                commandPaletteService.Show();
            }
            catch
            {
                // Fallback: Show simple message if CommandPaletteService fails
                // In production, this would show a proper command palette
            }
        }

        private async Task ShowToolCatalogAsync()
        {
            try
            {
                var dialog = new Views.Dialogs.ToolCatalogDialog(this.Content.XamlRoot);
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary && dialog.SelectedDescriptor != null)
                {
                    var desc = dialog.SelectedDescriptor;
                    var region = dialog.SelectedRegion ?? desc.DefaultRegion;
                    var opened = await OpenPanelByIdAsync(desc.PanelId, region);
                    if (opened)
                    {
                        var host = region switch
                        {
                            PanelRegion.Left => FindNameOnContent("LeftPanelHost") as Controls.PanelHost,
                            PanelRegion.Center => FindNameOnContent("CenterPanelHost") as Controls.PanelHost,
                            PanelRegion.Right => FindNameOnContent("RightPanelHost") as Controls.PanelHost,
                            PanelRegion.Bottom => FindNameOnContent("BottomPanelHost") as Controls.PanelHost,
                            _ => null
                        };
                        if (host != null)
                        {
                            host.PanelTitle = desc.DisplayName;
                            if (!string.IsNullOrEmpty(desc.Icon))
                                host.PanelIcon = desc.Icon;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] ToolCatalog failed: {ex.Message}");
                var toast = ServiceProvider.TryGetToastNotificationService();
                toast?.ShowError("Tool Catalog", ex.Message);
            }
        }

        private void ShowGlobalSearch() => _searchOverlayCoordinator?.Show();

        private void GlobalSearchOverlay_Tapped(object _, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var globalSearchOverlay = FindNameOnContent("GlobalSearchOverlay") as FrameworkElement;
            if (globalSearchOverlay != null && ReferenceEquals(e.OriginalSource, globalSearchOverlay))
                _searchOverlayCoordinator?.Hide();
        }

        // Collaboration Panel Toggle (IDEA 25)
        private void CollaboratorsToggleButton_Click(object sender, RoutedEventArgs e)
        {
            var collaborationPanel = FindNameOnContent("CollaborationPanel") as FrameworkElement;
            if (collaborationPanel != null)
            {
                collaborationPanel.Visibility = collaborationPanel.Visibility == Visibility.Visible
                  ? Visibility.Collapsed
                  : Visibility.Visible;
            }
        }

        private void CollaborationIndicator_CloseRequested(object? sender, EventArgs e)
        {
            var collaborationPanel = FindNameOnContent("CollaborationPanel") as FrameworkElement;
            if (collaborationPanel != null)
            {
                collaborationPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void SaveProject()
        {
            if (_projectWorkflowCoordinator != null)
                await _projectWorkflowCoordinator.SaveProjectAsync();
        }

        private async void CreateNewProject()
        {
            if (_projectWorkflowCoordinator != null)
                await _projectWorkflowCoordinator.CreateNewProjectAsync();
        }

        private async void OpenProject()
        {
            if (_projectWorkflowCoordinator != null)
                await _projectWorkflowCoordinator.OpenProjectAsync();
        }

        /// <summary>
        /// Thin wrapper for import workflow. Delegates to IImportWorkflowService (Transport Coherence Wave 4 Phase 2).
        /// Gated: blocks until backend ready (Round 5 Task 2 — audit non-registry paths).
        /// </summary>
        public void ImportAudioFile()
        {
            var startupState = ServiceProvider.GetStartupStateService();
            if (!startupState.IsReady)
            {
                AppServices.TryGetToastNotificationService()?.ShowInfo("Starting VoiceStudio services…", "Please wait");
                return;
            }
            var service = AppServices.GetService<IImportWorkflowService>();
            if (service == null) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _ = service.ImportAudioFileAsync(hwnd);
        }

        private async void OpenRecentProject(string projectId, string projectName)
        {
            if (_projectWorkflowCoordinator != null)
                await _projectWorkflowCoordinator.OpenRecentProjectAsync(projectId, projectName);
        }

        private async void PinRecentProject(string projectId)
        {
            try
            {
                if (_recentProjectsService != null)
                {
                    await _recentProjectsService.PinProjectAsync(projectId);
                    var toastService = ServiceProvider.GetToastNotificationService();
                    toastService?.ShowToast(
                        Services.ToastType.Success,
                        "Project Pinned",
                        "Project pinned to Recent Projects menu");
                }
            }
            catch (Exception ex)
            {
                var toastService = ServiceProvider.GetToastNotificationService();
                toastService?.ShowToast(
                    Services.ToastType.Error,
                    "Failed to Pin Project",
                    ex.Message);
            }
        }

        private async void UnpinRecentProject(string projectId)
        {
            try
            {
                if (_recentProjectsService != null)
                {
                    await _recentProjectsService.UnpinProjectAsync(projectId);
                    var toastService = ServiceProvider.GetToastNotificationService();
                    toastService?.ShowToast(
                        Services.ToastType.Success,
                        "Project Unpinned",
                        "Project removed from pinned list");
                }
            }
            catch (Exception ex)
            {
                var toastService = ServiceProvider.GetToastNotificationService();
                toastService?.ShowToast(
                    Services.ToastType.Error,
                    "Failed to Unpin Project",
                    ex.Message);
            }
        }

        private async void ClearRecentProjects()
        {
            try
            {
                if (_recentProjectsService != null)
                {
                    await _recentProjectsService.ClearRecentProjectsAsync();
                    var toastService = ServiceProvider.GetToastNotificationService();
                    toastService?.ShowToast(
                        Services.ToastType.Success,
                        "Recent Projects Cleared",
                        "All recent projects have been cleared");
                }
            }
            catch (Exception ex)
            {
                var toastService = ServiceProvider.GetToastNotificationService();
                toastService?.ShowToast(
                    Services.ToastType.Error,
                    "Failed to Clear Recent Projects",
                    ex.Message);
            }
        }

        private void ExecuteUndo()
        {
            try
            {
                var undoService = ServiceProvider.GetUndoRedoService();
                if (undoService.CanUndo)
                {
                    undoService.Undo();
                }
            }
            catch (Exception ex)
            {
                ServiceProvider.TryGetErrorLoggingService()?.LogError(ex, "ExecuteUndo");
            }
        }

        private void ExecuteRedo()
        {
            try
            {
                var undoService = ServiceProvider.GetUndoRedoService();
                if (undoService.CanRedo)
                {
                    undoService.Redo();
                }
            }
            catch (Exception ex)
            {
                ServiceProvider.TryGetErrorLoggingService()?.LogError(ex, "ExecuteRedo");
            }
        }

        private void OpenDocumentationFolder()
        {
            var repoRoot = Environment.GetEnvironmentVariable("VOICESTUDIO_REPO_ROOT");
            var docsPath = repoRoot != null
                ? Path.Combine(repoRoot, "docs")
                : Path.Combine(AppContext.BaseDirectory, "docs");
            try
            {
                if (!Directory.Exists(docsPath))
                {
                    ServiceProvider.TryGetToastNotificationService()?.ShowWarning(
                        $"Docs folder not found: {docsPath}",
                        "Documentation");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{docsPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ServiceProvider.TryGetErrorLoggingService()?.LogError(ex, "OpenDocumentationFolder");
                ServiceProvider.TryGetToastNotificationService()?.ShowError(
                    "Unable to open documentation folder.",
                    "Documentation");
            }
        }

        private async void ShowAboutDialog()
        {
            try
            {
                var version = Package.Current.Id.Version;
                var versionText = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
                var aboutPanel = new StackPanel { Spacing = 8 };
                aboutPanel.Children.Add(new TextBlock { Text = $"Version {versionText}" });
                aboutPanel.Children.Add(new TextBlock { Text = "Local-first voice production studio", Opacity = 0.7 });

                var licenseLink = new HyperlinkButton
                {
                    Content = "View Third-Party Licenses",
                    NavigateUri = new System.Uri("https://github.com/wtsteward11/VoiceStudio/blob/main/THIRD_PARTY_LICENSES.md")
                };
                aboutPanel.Children.Add(licenseLink);

                aboutPanel.Children.Add(new TextBlock
                {
                    Text = "License file: THIRD_PARTY_LICENSES.md (repo root)",
                    Opacity = 0.5,
                    FontSize = 11
                });

                var dialog = new ContentDialog
                {
                    Title = "VoiceStudio Quantum+",
                    Content = aboutPanel,
                    CloseButtonText = "Close",
                    XamlRoot = (Content as FrameworkElement)?.XamlRoot
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                ServiceProvider.TryGetErrorLoggingService()?.LogError(ex, "ShowAboutDialog");
                ServiceProvider.TryGetToastNotificationService()?.ShowError(
                    "Unable to show About dialog.",
                    "About");
            }
        }

        private void PopulateRecentProjectsMenu()
        {
            if (_recentProjectsSubMenu == null || _recentProjectsService == null)
                return;

            _recentProjectsSubMenu.Items.Clear();

            var allProjects = _recentProjectsService.AllProjects;

            if (allProjects.Count == 0)
            {
                var emptyItem = new MenuFlyoutItem
                {
                    Text = "No recent projects",
                    IsEnabled = false
                };
                _recentProjectsSubMenu.Items.Add(emptyItem);
                return;
            }

            // Add pinned projects first
            var pinnedProjects = _recentProjectsService.PinnedProjects;
            if (pinnedProjects.Count > 0)
            {
                foreach (var project in pinnedProjects)
                {
                    var subMenu = new MenuFlyoutSubItem
                    {
                        Text = $"📌 {project.Name}"
                    };
                    var openItem = new MenuFlyoutItem
                    {
                        Text = "Open",
                        Tag = project.Path
                    };
                    openItem.Click += (_, _) => OpenRecentProject(project.Path, project.Name);
                    subMenu.Items.Add(openItem);
                    subMenu.Items.Add(new MenuFlyoutSeparator());

                    var unpinItem = new MenuFlyoutItem
                    {
                        Text = "Unpin",
                        Tag = project.Path
                    };
                    unpinItem.Click += (_, _) => UnpinRecentProject(project.Path);
                    subMenu.Items.Add(unpinItem);

                    _recentProjectsSubMenu!.Items.Add(subMenu);
                }

                if (_recentProjectsService.RecentProjects.Count > 0)
                {
                    _recentProjectsSubMenu!.Items.Add(new MenuFlyoutSeparator());
                }
            }

            // Add recent projects
            foreach (var project in _recentProjectsService.RecentProjects)
            {
                var subMenu = new MenuFlyoutSubItem
                {
                    Text = project.Name
                };
                var openItem2 = new MenuFlyoutItem
                {
                    Text = "Open",
                    Tag = project.Path
                };
                openItem2.Click += (_, _) => OpenRecentProject(project.Path, project.Name);
                subMenu.Items.Add(openItem2);
                subMenu.Items.Add(new MenuFlyoutSeparator());

                var pinItem = new MenuFlyoutItem
                {
                    Text = "Pin",
                    Tag = project.Path
                };
                pinItem.Click += (_, _) => PinRecentProject(project.Path);
                subMenu.Items.Add(pinItem);

                var removeItem = new MenuFlyoutItem
                {
                    Text = "Remove from list",
                    Tag = project.Path
                };
                removeItem.Click += async (_, _) =>
                {
                    if (_recentProjectsService != null)
                    {
                        await _recentProjectsService.RemoveRecentProjectAsync(project.Path);
                    }
                };
                subMenu.Items.Add(removeItem);

                _recentProjectsSubMenu!.Items.Add(subMenu);
            }

            // Add separator and clear option
            if (allProjects.Count > 0)
            {
                _recentProjectsSubMenu!.Items.Add(new MenuFlyoutSeparator());
                var clearItem = new MenuFlyoutItem
                {
                    Text = "Clear Recent Projects"
                };
                clearItem.Click += (_, _) => ClearRecentProjects();
                _recentProjectsSubMenu!.Items.Add(clearItem);
            }
        }

        private void OnPlayRequested(object? sender, EventArgs e) => TogglePlayback();
        private void OnStopRequested(object? sender, EventArgs e) => StopPlayback();

        private async void TogglePlayback()
        {
            // Task 4 (Round 4): Gate transport until backend ready
            if (StartupGatingHelper.ShouldBlockTransportPlayback(ServiceProvider.GetStartupStateService()))
            {
                ServiceProvider.TryGetToastNotificationService()?.ShowInfo("Starting VoiceStudio services…", "Please wait");
                return;
            }
            var orchestrator = AppServices.GetService<IGlobalTransportOrchestrator>();
            if (orchestrator != null)
            {
                await orchestrator.TogglePlaybackAsync();
            }
            else
            {
                var toast = ServiceProvider.TryGetToastNotificationService();
                toast?.ShowToast(ToastType.Info, "No media selected", "Select an audio asset in Library or Timeline, then press Play.");
            }
        }

        private void StopPlayback()
        {
            // Task 4 (Round 4): Gate transport until backend ready
            if (StartupGatingHelper.ShouldBlockTransportPlayback(ServiceProvider.GetStartupStateService()))
            {
                ServiceProvider.TryGetToastNotificationService()?.ShowInfo("Starting VoiceStudio services…", "Please wait");
                return;
            }
            var orchestrator = AppServices.GetService<IGlobalTransportOrchestrator>();
            orchestrator?.StopPlayback();
        }

        /// <summary>
        /// Ctrl+R / transport shortcut: same policy as timeline Record — navigate to Recording panel (Transport Authority Slice 2).
        /// </summary>
        private void OpenRecordingPanelFromTransportShortcut()
        {
            if (!ServiceProvider.GetStartupStateService().IsReady)
            {
                AppServices.TryGetToastNotificationService()?.ShowInfo("Starting VoiceStudio services…", "Please wait");
                return;
            }

            var aggregator = AppServices.TryGetEventAggregator();
            if (aggregator == null)
                return;

            aggregator.Publish(new NavigateToEvent(PanelIds.Timeline, PanelIds.Recording, null));
        }

        private async void ToggleRecording()
        {
            if (!ServiceProvider.GetStartupStateService().IsReady)
            {
                AppServices.TryGetToastNotificationService()?.ShowInfo("Starting VoiceStudio services…", "Please wait");
                return;
            }
            try
            {
                if (!(FindNameOnContent("RightPanelHost") is Controls.PanelHost rightPanelHost))
                {
                    return;
                }

                var recordingView = rightPanelHost.Content as RecordingView;
                if (recordingView == null)
                {
                    await OpenPanelByIdAsync("Recording", PanelRegion.Right);
                    recordingView = rightPanelHost.Content as RecordingView;
                }

                if (recordingView == null)
                    return;

                var viewModel = recordingView.ViewModel;
                if (viewModel.IsRecording)
                {
                    if (viewModel.StopRecordingCommand.CanExecute(null))
                    {
                        viewModel.StopRecordingCommand.Execute(null);
                    }
                }
                else
                {
                    if (viewModel.StartRecordingCommand.CanExecute(null))
                    {
                        viewModel.StartRecordingCommand.Execute(null);
                    }
                }
            }
            catch (Exception ex)
            {
                ServiceProvider.TryGetErrorLoggingService()?.LogError(ex, "ToggleRecording");
                ServiceProvider.TryGetToastNotificationService()?.ShowError(
                    "Recording toggle failed.",
                    "Recording");
            }
        }

        private void ZoomIn()
        {
            var centerPanelHost = FindNameOnContent("CenterPanelHost") as Controls.PanelHost;
            if (centerPanelHost?.Content is TimelineView timelineView && timelineView.ViewModel != null)
            {
                var viewModel = timelineView.ViewModel;
                if (viewModel.ZoomInCommand.CanExecute(null))
                {
                    viewModel.ZoomInCommand.Execute(null);
                }
            }
        }

        private void ZoomOut()
        {
            var centerPanelHost = FindNameOnContent("CenterPanelHost") as Controls.PanelHost;
            if (centerPanelHost?.Content is TimelineView timelineView && timelineView.ViewModel != null)
            {
                var viewModel = timelineView.ViewModel;
                if (viewModel.ZoomOutCommand.CanExecute(null))
                {
                    viewModel.ZoomOutCommand.Execute(null);
                }
            }
        }

        private void ResetZoom()
        {
            var centerPanelHost = FindNameOnContent("CenterPanelHost") as Controls.PanelHost;
            if (centerPanelHost?.Content is TimelineView timelineView && timelineView.ViewModel != null)
            {
                var viewModel = timelineView.ViewModel;
                viewModel.TimelineZoom = 1.0;
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs e)
        {
            _statusBarTimer?.Stop();
            _layoutSaveDebouncer?.Cancel();
            SaveWorkspaceLayout();
            _sessionLifecycle.TryMarkCleanShutdown();
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

        private void Cleanup()
        {
            if (_disposed)
                return;

            // Clean up temporary audio files (Audit L-2)
            CleanupTempAudioFiles();

            // Dispose clock timer
            _clockTimer?.Dispose();
            _clockTimer = null;

            // Dispose preview timer
            _previewHideTimer?.Dispose();
            _previewHideTimer = null;

            _layoutSaveDebouncer?.Cancel();
            SaveWorkspaceLayout();

            // Unsubscribe from events
            if (this.Content is UIElement root)
            {
                root.KeyDown -= MainWindow_KeyDown;
            }
            this.Activated -= MainWindow_Activated;
            this.Closed -= MainWindow_Closed;

            if (_panelStateService != null)
            {
                _panelStateService.WorkspaceProfileChanged -= OnWorkspaceProfileChanged;
            }

            // Startup overlay cleanup
            if (_startupStateService != null)
            {
                _startupStateService.StateChanged -= StartupState_StateChanged;
                _startupStateService = null;
            }

            // Transport/status event cleanup (Transport Coherence Wave 3 Task 1, 5; Wave 4 Phase 1)
            _sessionLifecycle.Dispose();

            _transportShortcutCoordinator?.Detach();
            _transportShortcutCoordinator = null;
            _statusBarCoordinator?.Unsubscribe();
            _statusBarCoordinator = null;
            if (_globalTransport != null)
            {
                _globalTransport.PlayRequested -= OnPlayRequested;
                _globalTransport.StopRequested -= OnStopRequested;
                _globalTransport = null;
            }

            _disposed = true;
        }

        /// <summary>
        /// Clean up temporary audio files created during synthesis and recording.
        /// Audit remediation L-2: Temp files not cleaned up on exit.
        /// Removes %TEMP%\voicestudio_*.wav and %TEMP%\voicestudio_recording_*.wav files.
        /// </summary>
        private static void CleanupTempAudioFiles()
        {
            try
            {
                var tempDir = System.IO.Path.GetTempPath();
                var patterns = new[] { "voicestudio_*.wav", "voicestudio_recording_*.wav" };

                int cleaned = 0;
                foreach (var pattern in patterns)
                {
                    foreach (var file in System.IO.Directory.GetFiles(tempDir, pattern))
                    {
                        try
                        {
                            System.IO.File.Delete(file);
                            cleaned++;
                        }
                        catch (System.IO.IOException)
                        {
                            // File in use -- skip, will be cleaned next exit
                            Debug.WriteLine("[MainWindow] Temp file in use, skipped: " + file);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Permission denied -- skip
                            Debug.WriteLine("[MainWindow] Temp file access denied, skipped: " + file);
                        }
                    }
                }

                if (cleaned > 0)
                {
                    Debug.WriteLine($"[MainWindow] Cleaned up {cleaned} temporary audio file(s)");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Temp cleanup failed (non-critical): {ex.Message}");
            }
        }

        ~MainWindow()
        {
            Cleanup();
        }
    }
}