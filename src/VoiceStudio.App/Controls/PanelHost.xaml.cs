using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Views.Panels;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Windows.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Controls
{
  public sealed partial class PanelHost : UserControl
  {
    private PanelStateService? _panelStateService;
    private IPanelRegistry? _panelRegistry;
    private readonly ConcurrentDictionary<string, UserControl> _loadedPanels = new();
    private readonly List<string> _lruOrder = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    /// <summary>GAP-013: serialize HostedPanel lifecycle (deactivate/save/restore/activate) across overlapping DP changes.</summary>
    private readonly SemaphoreSlim _hostedPanelTransitionLock = new(1, 1);
    private volatile bool _isUnloaded;
    private string? _previousPanelId;
    private PanelRegion _region = PanelRegion.Center;
    private DragDropVisualFeedbackService? _dragDropService;
    private PanelRegion? _currentDropZone;
    private bool _isDragging;
    private System.Threading.CancellationTokenSource? _loadingCts;

    private string? _lastRequestedPanelId;
    private Func<UserControl>? _lastRequestedLegacyFactory;
    private bool _subscribedToReachability;
    private Grid? _offlineOverlay;

    public static readonly DependencyProperty HostedPanelProperty =
        DependencyProperty.Register(nameof(HostedPanel), typeof(UIElement), typeof(PanelHost),
            new PropertyMetadata(null, OnContentChanged));

    public static readonly DependencyProperty PanelRegionProperty =
        DependencyProperty.Register(
            nameof(PanelRegion),
            typeof(PanelRegion),
            typeof(PanelHost),
            new PropertyMetadata(PanelRegion.Center, OnPanelRegionChanged));

    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register(
            nameof(IsLoading),
            typeof(bool),
            typeof(PanelHost),
            new PropertyMetadata(false));

    public static readonly DependencyProperty LoadingMessageProperty =
        DependencyProperty.Register(
            nameof(LoadingMessage),
            typeof(string),
            typeof(PanelHost),
            new PropertyMetadata("Loading..."));

    public static readonly DependencyProperty IsCollapsedProperty =
        DependencyProperty.Register(
            nameof(IsCollapsed),
            typeof(bool),
            typeof(PanelHost),
            new PropertyMetadata(false, OnIsCollapsedChanged));

    public static readonly DependencyProperty PanelTitleProperty =
        DependencyProperty.Register(
            nameof(PanelTitle),
            typeof(string),
            typeof(PanelHost),
            new PropertyMetadata("Panel", OnPanelTitleChanged));

    public static readonly DependencyProperty PanelIconProperty =
        DependencyProperty.Register(
            nameof(PanelIcon),
            typeof(string),
            typeof(PanelHost),
            new PropertyMetadata("📋", OnPanelIconChanged));

    public static readonly DependencyProperty QualityMetricsProperty =
        DependencyProperty.Register(
            nameof(QualityMetrics),
            typeof(QualityMetrics),
            typeof(PanelHost),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ShowQualityBadgeProperty =
        DependencyProperty.Register(
            nameof(ShowQualityBadge),
            typeof(bool),
            typeof(PanelHost),
            new PropertyMetadata(false));

    public static readonly DependencyProperty LoadErrorMessageProperty =
        DependencyProperty.Register(
            nameof(LoadErrorMessage),
            typeof(string),
            typeof(PanelHost),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LoadErrorTitleProperty =
        DependencyProperty.Register(
            nameof(LoadErrorTitle),
            typeof(string),
            typeof(PanelHost),
            new PropertyMetadata("Failed to load panel"));

    public UIElement? HostedPanel
    {
      get => (UIElement?)GetValue(HostedPanelProperty);
      set => SetValue(HostedPanelProperty, value);
    }

    public bool IsLoading
    {
      get => (bool)GetValue(IsLoadingProperty);
      set => SetValue(IsLoadingProperty, value);
    }

    public string LoadingMessage
    {
      get => (string)GetValue(LoadingMessageProperty);
      set => SetValue(LoadingMessageProperty, value);
    }

    public bool IsCollapsed
    {
      get => (bool)GetValue(IsCollapsedProperty);
      set => SetValue(IsCollapsedProperty, value);
    }

    public string PanelTitle
    {
      get => (string)GetValue(PanelTitleProperty);
      set => SetValue(PanelTitleProperty, value);
    }

    public string PanelIcon
    {
      get => (string)GetValue(PanelIconProperty);
      set => SetValue(PanelIconProperty, value);
    }

    public QualityMetrics? QualityMetrics
    {
      get => (QualityMetrics?)GetValue(QualityMetricsProperty);
      set => SetValue(QualityMetricsProperty, value);
    }

    public bool ShowQualityBadge
    {
      get => (bool)GetValue(ShowQualityBadgeProperty);
      set => SetValue(ShowQualityBadgeProperty, value);
    }

    public string LoadErrorMessage
    {
      get => (string)GetValue(LoadErrorMessageProperty);
      set => SetValue(LoadErrorMessageProperty, value);
    }

    public string LoadErrorTitle
    {
      get => (string)GetValue(LoadErrorTitleProperty);
      set => SetValue(LoadErrorTitleProperty, value);
    }

    // XAML compiler stability: avoid bool->Visibility x:Bind.
    public Visibility QualityBadgeVisibility => ShowQualityBadge ? Visibility.Visible : Visibility.Collapsed;

    public PanelRegion PanelRegion
    {
      get => (PanelRegion)GetValue(PanelRegionProperty);
      set => SetValue(PanelRegionProperty, value);
    }

    public PanelHost()
    {
      this.InitializeComponent();
      if (ErrorOverlay != null)
        ErrorOverlay.RetryRequested += ErrorOverlay_RetryRequested;
      _panelStateService = ServiceProvider.GetPanelStateService();
      _dragDropService = ServiceProvider.TryGetDragDropVisualFeedbackService();
      _panelRegistry = AppServices.GetPanelRegistry();

      // Wire up resize handles to resize this PanelHost (defensive null checks)
      var rightHandle = this.FindName("RightResizeHandle") as PanelResizeHandle;
      if (rightHandle != null)
      {
        rightHandle.TargetElement = this;
      }
      var bottomHandle = this.FindName("BottomResizeHandle") as PanelResizeHandle;
      if (bottomHandle != null)
      {
        bottomHandle.TargetElement = this;
      }

      // Enable drop on the entire PanelHost for docking
      this.AllowDrop = true;

      // Cleanup when unloaded: cancel loading, dispose cached panel ViewModels, clear cache.
      // Do NOT dispose _loadLock — other threads may still hold or wait on it; disposing causes ObjectDisposedException.
      this.Unloaded += (_, _) =>
      {
        _isUnloaded = true;
        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        if (_subscribedToReachability)
        {
          ErrorPresentationService.BackendReachabilityChanged -= OnBackendReachabilityChanged;
          _subscribedToReachability = false;
        }
        _ = CleanupCacheAsync();
      };
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is PanelHost host)
      {
        // Fire-and-forget async lifecycle handling
        _ = host.HandleContentChangeAsync(e.OldValue as UIElement, e.NewValue as UIElement);
      }
    }

    /// <summary>
    /// Async cleanup of cached panels on unload. Fire-and-forget from Unloaded handler to avoid blocking UI thread.
    /// Disposes ViewModels on UI thread when possible to avoid UI-thread affinity crashes.
    /// </summary>
    private async Task CleanupCacheAsync()
    {
      List<object?>? toTeardown = null;
      try
      {
        await _loadLock.WaitAsync().ConfigureAwait(false);
        try
        {
          toTeardown = new List<object?>();
          foreach (var cached in _loadedPanels.Values)
          {
            if (cached is UserControl uc && uc.DataContext != null)
              toTeardown.Add(uc.DataContext);
          }
          _loadedPanels.Clear();
          _lruOrder.Clear();
        }
        finally
        {
          _loadLock.Release();
        }
      }
      // ALLOWED: empty catch - lock disposed during shutdown, expected
      catch (ObjectDisposedException)
      {
        // Lock disposed during shutdown, ignore
        return;
      }

      if (toTeardown == null || toTeardown.Count == 0)
        return;

      var dq = DispatcherQueue;
      if (dq != null && !dq.HasThreadAccess)
      {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dq.TryEnqueue(() => { _ = CompleteCacheTeardownOnUiAsync(tcs, toTeardown!); });
        try
        {
          await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "PanelHost.CleanupCacheAsync");
        }
      }
      else if (dq == null)
      {
        System.Diagnostics.Debug.WriteLine("[PanelHost] CleanupCacheAsync: DispatcherQueue null, deactivating/disposing on current thread (may cause UI-thread affinity issues)");
        foreach (var vm in toTeardown)
        {
          await DeactivateViewModelThenDisposeAsync(vm, CancellationToken.None).ConfigureAwait(false);
        }
      }
      else
      {
        foreach (var vm in toTeardown)
        {
          await DeactivateViewModelThenDisposeAsync(vm, CancellationToken.None).ConfigureAwait(true);
        }
      }
    }

    private async Task CompleteCacheTeardownOnUiAsync(TaskCompletionSource tcs, List<object?> vms)
    {
      try
      {
        foreach (var vm in vms)
        {
          await DeactivateViewModelThenDisposeAsync(vm, CancellationToken.None).ConfigureAwait(true);
        }
        tcs.TrySetResult();
      }
      catch (Exception ex)
      {
        tcs.TrySetException(ex);
      }
    }

    /// <summary>
    /// Handles content change with proper lifecycle management.
    /// </summary>
    private async Task HandleContentChangeAsync(UIElement? oldContent, UIElement? newContent)
    {
      if (ReferenceEquals(oldContent, newContent))
        return;

      await _hostedPanelTransitionLock.WaitAsync().ConfigureAwait(true);
      try
      {
        var ct = _loadingCts?.Token ?? CancellationToken.None;

        // 1. Deactivate old content's ViewModel
        await DeactivateViewModelAsync(oldContent, ct).ConfigureAwait(true);

        // 2. Panels in cache must NOT be disposed on deactivation (removed DisposePreviousViewModel)

        // 3. Save outgoing panel state (use old content — HostedPanel already reflects new value in callback)
        SaveOutgoingPanelState(oldContent);

        // 4. Restore new panel state (await before activate)
        await RestorePanelStateAsync(newContent).ConfigureAwait(true);

        // 5. Activate new content's ViewModel
        await ActivateViewModelAsync(newContent, ct).ConfigureAwait(true);

        // 6. Update context-sensitive action bar (IDEA 2)
        UpdateActionBar(newContent);
      }
      catch (OperationCanceledException)
      {
        System.Diagnostics.Debug.WriteLine("PanelHost: HandleContentChangeAsync cancelled");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[PanelHost] Error during content change: {ex.Message}");
      }
      finally
      {
        _hostedPanelTransitionLock.Release();
      }
    }

    /// <summary>GAP-013: seam for behavioral tests — runs the same path as <see cref="HostedPanelProperty"/> change.</summary>
    internal Task RunHostedPanelLifecycleTransitionForTestsAsync(UIElement? oldContent, UIElement? newContent) =>
      HandleContentChangeAsync(oldContent, newContent);

    /// <summary>
    /// Activates the ViewModel if it implements IPanelLifecycle.
    /// </summary>
    private async Task ActivateViewModelAsync(UIElement? content, CancellationToken ct)
    {
      if (content == null) return;

      var viewModel = GetViewModelFromContent(content);
      if (viewModel == null) return;

      // Try typed interface first
      if (viewModel is IPanelLifecycle lifecycle)
      {
        try
        {
          await lifecycle.OnActivatedAsync(ct);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[PanelHost] Error activating panel: {ex.Message}");
        }
      }
      else
      {
        // Fall back to reflection-based activation
        await PanelLifecycleHelper.InvokeActivateAsync(viewModel, ct);
      }
    }

    /// <summary>
    /// Deactivates the ViewModel if it implements IPanelLifecycle.
    /// </summary>
    private async Task DeactivateViewModelAsync(UIElement? content, CancellationToken ct)
    {
      if (content == null) return;

      var viewModel = GetViewModelFromContent(content);
      if (viewModel == null) return;

      // Try typed interface first
      if (viewModel is IPanelLifecycle lifecycle)
      {
        try
        {
          await lifecycle.OnDeactivatedAsync(ct);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[PanelHost] Error deactivating panel: {ex.Message}");
        }
      }
      else
      {
        // Fall back to reflection-based deactivation
        await PanelLifecycleHelper.InvokeDeactivateAsync(viewModel, ct);
      }
    }

    /// <summary>
    /// GAP-013: deactivate lifecycle hooks before <see cref="IDisposable.Dispose"/> on eviction/unload/cache clear.
    /// </summary>
    internal static async Task DeactivateViewModelThenDisposeAsync(object? viewModel, CancellationToken cancellationToken)
    {
      if (viewModel == null)
        return;

      try
      {
        if (viewModel is IPanelLifecycle lifecycle)
        {
          await lifecycle.OnDeactivatedAsync(cancellationToken).ConfigureAwait(true);
        }
        else
        {
          await PanelLifecycleHelper.InvokeDeactivateAsync(viewModel, cancellationToken).ConfigureAwait(true);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[PanelHost] Deactivate before dispose failed: {ex.Message}");
      }

      if (viewModel is IDisposable d)
      {
        try
        {
          d.Dispose();
        }
        catch (Exception ex)
        {
          ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "PanelHost.DeactivateViewModelThenDisposeAsync");
        }
      }
    }

    /// <summary>
    /// Gets the ViewModel from a content element.
    /// </summary>
    private static object? GetViewModelFromContent(UIElement content)
    {
      if (content is UserControl userControl)
      {
        return userControl.DataContext;
      }

      if (content is FrameworkElement frameworkElement)
      {
        return frameworkElement.DataContext;
      }

      return null;
    }

    private static void OnPanelRegionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is PanelHost host)
      {
        host._region = (PanelRegion)e.NewValue;
      }
    }

    private static void OnIsCollapsedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is PanelHost host)
      {
        var panelBody = host.FindName("PanelBody") as FrameworkElement;
        if (panelBody != null)
        {
          panelBody.Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible;
        }
      }
    }

    private static void OnPanelTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is PanelHost host)
      {
        var titleTextBlock = host.FindName("PanelTitleTextBlock") as TextBlock;
        if (titleTextBlock != null)
        {
          titleTextBlock.Text = e.NewValue?.ToString() ?? "Panel";
        }
      }
    }

    private static void OnPanelIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is PanelHost host)
      {
        var iconTextBlock = host.FindName("PanelIconTextBlock") as TextBlock;
        if (iconTextBlock != null)
        {
          iconTextBlock.Text = e.NewValue?.ToString() ?? "📋";
        }
      }
    }

    private void CollapseButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
      IsCollapsed = !IsCollapsed;
    }

    private void OptionsFlyout_Opening(object sender, object e)
    {
      if (OptionsFlyout.Items.Count < 2)
        return;

      var viewModel = HostedPanel != null ? GetViewModelFromContent(HostedPanel) : null;
      var showRefresh = viewModel is IPanelLifecycle;

      if (OptionsFlyout.Items[0] is MenuFlyoutItem refreshItem)
        refreshItem.Visibility = showRefresh ? Visibility.Visible : Visibility.Collapsed;
      if (OptionsFlyout.Items[1] is MenuFlyoutSeparator separator)
        separator.Visibility = showRefresh ? Visibility.Visible : Visibility.Collapsed;

      PopulateSwitchPanelMenu();
    }

    private void PopulateSwitchPanelMenu()
    {
      if (_panelRegistry == null)
        return;

      SwitchPanelSubMenu.Items.Clear();

      var descriptors = _panelRegistry.GetAllDescriptors()
        .Where(d => d.IsVisible)
        .Where(d => d.Maturity != PanelMaturity.Deprecated)
        .Where(d => !string.IsNullOrEmpty(d.MenuCategory))
        .OrderBy(d => d.MenuCategory)
        .ThenBy(d => d.DisplayName);

      string? currentCategory = null;

      foreach (var desc in descriptors)
      {
        if (currentCategory != null && !string.Equals(currentCategory, desc.MenuCategory, StringComparison.Ordinal))
        {
          SwitchPanelSubMenu.Items.Add(new MenuFlyoutSeparator());
        }
        currentCategory = desc.MenuCategory;

        var item = new MenuFlyoutItem { Text = $"{desc.DisplayName}" };
        var panelId = desc.PanelId;

        item.Click += async (_, _) =>
        {
          var panel = await LoadPanelAsync(panelId);
          if (panel != null)
          {
            PanelTitle = desc.DisplayName;
            if (!string.IsNullOrEmpty(desc.Icon))
              PanelIcon = desc.Icon;
          }
        };

        SwitchPanelSubMenu.Items.Add(item);
      }

      if (SwitchPanelSubMenu.Items.Count == 0)
      {
        var empty = new MenuFlyoutItem { Text = "No panels available", IsEnabled = false };
        SwitchPanelSubMenu.Items.Add(empty);
      }
    }

    private async void OptionsRefresh_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
      if (HostedPanel == null)
        return;

      var viewModel = GetViewModelFromContent(HostedPanel);
      if (viewModel is not IPanelLifecycle lifecycle)
        return;

      try
      {
        await lifecycle.RefreshAsync(CancellationToken.None);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[PanelHost] Error refreshing panel: {ex.Message}");
      }
    }

    private void OptionsPopOut_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
      // Stub: future floating window support
      System.Diagnostics.Debug.WriteLine("[PanelHost] Pop out requested (stub)");
    }

    /// <summary>
    /// Saves outgoing panel state when <see cref="HostedPanel"/> changes (use explicit outgoing element, not ambient <see cref="HostedPanel"/>).
    /// </summary>
    private void SaveOutgoingPanelState(UIElement? outgoingContent)
    {
      if (_panelStateService == null || outgoingContent == null)
        return;

      try
      {
        if (!GetPanelIdFromContent(outgoingContent, out string? panelId) || string.IsNullOrEmpty(panelId))
          return;

        var panelState = new VoiceStudio.Core.Models.PanelState
        {
          PanelId = panelId
        };

        if (outgoingContent is UserControl userControl && userControl.DataContext is IPanelStatePersistable persistable)
          {
            var customState = persistable.GetCurrentState();
            if (customState != null)
            {
              // Map PanelStateData to PanelState
              panelState.ScrollPosition = customState.ScrollPosition;
              panelState.SelectedItemId = customState.SelectedItemId;
              
              // Store custom data in the CustomState dictionary
              panelState.CustomState = new Dictionary<string, object>();
              
              if (customState.SearchText != null)
                panelState.CustomState["SearchText"] = customState.SearchText;
              if (customState.SortColumn != null)
                panelState.CustomState["SortColumn"] = customState.SortColumn;
              if (customState.SortDescending.HasValue)
                panelState.CustomState["SortDescending"] = customState.SortDescending.Value;
              if (customState.ActiveTabIndex.HasValue)
                panelState.CustomState["ActiveTabIndex"] = customState.ActiveTabIndex.Value;
              if (customState.ZoomLevel.HasValue)
                panelState.CustomState["ZoomLevel"] = customState.ZoomLevel.Value;
              if (customState.HorizontalScrollPosition.HasValue)
                panelState.CustomState["HorizontalScrollPosition"] = customState.HorizontalScrollPosition.Value;
              if (customState.SelectedItemIds != null)
                panelState.CustomState["SelectedItemIds"] = customState.SelectedItemIds;
              if (customState.ExpandedSections != null)
                panelState.CustomState["ExpandedSections"] = customState.ExpandedSections;
              if (customState.CustomData != null)
              {
                foreach (var kvp in customState.CustomData)
                  panelState.CustomState[kvp.Key] = kvp.Value;
              }
              
              System.Diagnostics.Debug.WriteLine($"Saved custom state for panel: {panelId}");
            }
          }

        _panelStateService.SavePanelState(_region, panelId, panelState);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to save panel state: {ex.Message}");
      }
    }

    /// <summary>
    /// Restores panel state when a panel is shown; awaited before <see cref="IPanelLifecycle.OnActivatedAsync"/>.
    /// </summary>
    private async Task RestorePanelStateAsync(UIElement? newContent)
    {
      if (_panelStateService == null || newContent == null)
        return;

      try
      {
        if (!GetPanelIdFromContent(newContent, out string? panelId) || string.IsNullOrEmpty(panelId))
          return;

        _previousPanelId = panelId;

        var savedState = _panelStateService.GetPanelState(_region, panelId);
        if (savedState == null)
          return;

        if (newContent is UserControl userControl && userControl.DataContext is IPanelStatePersistable persistable)
        {
          var stateData = new PanelStateData
          {
            PanelId = savedState.PanelId,
            ScrollPosition = savedState.ScrollPosition,
            SelectedItemId = savedState.SelectedItemId
          };

          if (savedState.CustomState != null)
          {
            if (savedState.CustomState.TryGetValue("SearchText", out var searchText))
              stateData.SearchText = searchText as string;
            if (savedState.CustomState.TryGetValue("SortColumn", out var sortColumn))
              stateData.SortColumn = sortColumn as string;
            if (savedState.CustomState.TryGetValue("SortDescending", out var sortDesc) && sortDesc is bool sortDescBool)
              stateData.SortDescending = sortDescBool;
            if (savedState.CustomState.TryGetValue("ActiveTabIndex", out var tabIndex) && tabIndex is int tabIndexInt)
              stateData.ActiveTabIndex = tabIndexInt;
            if (savedState.CustomState.TryGetValue("ZoomLevel", out var zoom) && zoom is double zoomDouble)
              stateData.ZoomLevel = zoomDouble;
            if (savedState.CustomState.TryGetValue("HorizontalScrollPosition", out var hScroll) && hScroll is double hScrollDouble)
              stateData.HorizontalScrollPosition = hScrollDouble;
            if (savedState.CustomState.TryGetValue("SelectedItemIds", out var selectedIds) && selectedIds is string[] idsArray)
              stateData.SelectedItemIds = idsArray;
            if (savedState.CustomState.TryGetValue("ExpandedSections", out var expanded) && expanded is Dictionary<string, bool> expandedDict)
              stateData.ExpandedSections = expandedDict;

            var knownKeys = new HashSet<string> {
              "SearchText", "SortColumn", "SortDescending", "ActiveTabIndex",
              "ZoomLevel", "HorizontalScrollPosition", "SelectedItemIds", "ExpandedSections"
            };
            stateData.CustomData = new Dictionary<string, object>();
            foreach (var kvp in savedState.CustomState)
            {
              if (!knownKeys.Contains(kvp.Key))
                stateData.CustomData[kvp.Key] = kvp.Value;
            }
          }

          try
          {
            await persistable.RestoreStateAsync(stateData).ConfigureAwait(true);
            System.Diagnostics.Debug.WriteLine($"Successfully restored custom state for panel: {panelId}");
          }
          catch (Exception ex)
          {
            System.Diagnostics.Debug.WriteLine($"Failed to restore custom state for panel {panelId}: {ex.Message}");
          }
        }
        else
        {
          System.Diagnostics.Debug.WriteLine($"Panel {panelId} does not implement IPanelStatePersistable - skipping custom state restoration");
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to restore panel state: {ex.Message}");
      }
    }

    /// <summary>
    /// Gets panel ID from content by checking if it has a ViewModel implementing IPanelView.
    /// Public for use by MainWindow when resolving panel IDs during dock operations.
    /// </summary>
    public static bool TryGetPanelIdFromContent(UIElement? content, out string? panelId)
    {
      panelId = null;
      if (content == null) return false;
      if (content is UserControl userControl && userControl.DataContext is IPanelView pv1)
      {
        panelId = pv1.PanelId;
        return true;
      }
      if (content is FrameworkElement fe && fe.DataContext is IPanelView pv2)
      {
        panelId = pv2.PanelId;
        return true;
      }
      return false;
    }

    private bool GetPanelIdFromContent(UIElement content, out string? panelId) =>
      TryGetPanelIdFromContent(content, out panelId);

    private const int MaxCachedPanels = 8;

    private void TouchLru(string panelId)
    {
      _lruOrder.Remove(panelId);
      _lruOrder.Add(panelId);
    }

    private async Task EvictIfOverCapacityAsync(string currentPanelId, CancellationToken cancellationToken)
    {
      if (_loadedPanels.Count <= MaxCachedPanels) return;
      string? toEvict = null;
      for (int i = 0; i < _lruOrder.Count; i++)
      {
        if (_lruOrder[i] != currentPanelId)
        {
          toEvict = _lruOrder[i];
          _lruOrder.RemoveAt(i);
          break;
        }
      }
      if (toEvict != null && _loadedPanels.TryRemove(toEvict, out var evicted))
      {
        if (evicted is UserControl uc)
        {
          await DeactivateViewModelThenDisposeAsync(uc.DataContext, cancellationToken).ConfigureAwait(true);
        }
        else if (evicted is IDisposable ed)
        {
          try
          {
            ed.Dispose();
          }
          catch (Exception ex)
          {
            ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "PanelHost.EvictIfOverCapacityAsync");
          }
        }
        System.Diagnostics.Debug.WriteLine($"[PanelHost] LRU evicted: {toEvict}");
      }
    }

    /// <summary>
    /// Disposes the ViewModel from the previous content if it implements IDisposable.
    /// This ensures proper cleanup when switching panels.
    /// </summary>
    [Obsolete("Cached panels must not be disposed on navigation. Disposal happens in Unloaded handler only.", error: true)]
    private void DisposePreviousViewModel(UIElement? oldContent)
    {
      if (oldContent == null)
        return;

      try
      {
        // Try to get ViewModel from UserControl's DataContext
        if (oldContent is UserControl userControl)
        {
          var viewModel = userControl.DataContext;
          if (viewModel is IDisposable disposable)
          {
            disposable.Dispose();
            return;
          }
        }

        // Try to get from FrameworkElement's DataContext
        if (oldContent is FrameworkElement frameworkElement)
        {
          var viewModel = frameworkElement.DataContext;
          if (viewModel is IDisposable disposable)
          {
            disposable.Dispose();
          }
        }
      }
      catch (Exception ex)
      {
        // Don't break panel switching if disposal fails
        System.Diagnostics.Debug.WriteLine($"Failed to dispose previous ViewModel: {ex.Message}");
      }
    }

    /// <summary>
    /// Saves region state (active panel, opened panels).
    /// Called by MainWindow when saving workspace layout.
    /// </summary>
    public void SaveRegionState()
    {
      if (_panelStateService == null)
        return;

      try
      {
        string activePanelId = string.Empty;
        var openedPanels = new List<string>();

        // Get active panel ID
        if (HostedPanel != null && GetPanelIdFromContent(HostedPanel, out string? panelId))
        {
          activePanelId = panelId ?? string.Empty;
          openedPanels.Add(activePanelId);
        }

        _panelStateService.SaveRegionState(_region, activePanelId, openedPanels);
        _panelStateService.SaveRegionCollapsedState(_region, IsCollapsed);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to save region state: {ex.Message}");
      }
    }

    /// <summary>
    /// Loads a panel lazily using PanelRegistry.
    /// Shows loading indicator while panel is being loaded.
    /// </summary>
    /// <param name="panelId">The panel ID to load</param>
    /// <param name="legacyFactory">Optional factory for panels not in the unified registry (e.g. MiniTimeline).</param>
    /// <returns>The loaded panel, or null if loading failed</returns>
    public async System.Threading.Tasks.Task<UserControl?> LoadPanelAsync(string panelId, Func<UserControl>? legacyFactory = null)
    {
      _lastRequestedPanelId = panelId;
      _lastRequestedLegacyFactory = legacyFactory;
      EnsureReachabilitySubscription();

      if (_isUnloaded)
      {
        System.Diagnostics.Debug.WriteLine($"[PanelHost] Unloaded, skipping load of {panelId}");
        return null;
      }
      if (_panelRegistry == null)
      {
        System.Diagnostics.Debug.WriteLine($"[PanelHost] PanelRegistry not available, cannot lazy load {panelId}");
        return null;
      }

      // Return cached panel if already loaded — use WaitAsync to avoid blocking UI thread
      await _loadLock.WaitAsync(CancellationToken.None);
      try
      {
        if (_isUnloaded) return null;
        if (_loadedPanels.TryGetValue(panelId, out var cached))
        {
          TouchLru(panelId);
          HostedPanel = cached;
          return cached;
        }
      }
      finally
      {
        _loadLock.Release();
      }

      // Cancel any previous loading operation
      _loadingCts?.Cancel();
      _loadingCts?.Dispose();
      _loadingCts = new System.Threading.CancellationTokenSource();

      try
      {
        IsLoading = true;
        LoadingMessage = $"Loading {panelId}...";

        // Create panel OUTSIDE lock — expensive (DI + view + VM init). Avoids UI freeze.
        UserControl? panel = null;
        try
        {
          panel = _panelRegistry.CreatePanel(panelId) as UserControl;
          if (panel == null && legacyFactory != null)
            panel = legacyFactory();
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[PanelHost] Error creating panel {panelId}: {ex.Message}");
#if DEBUG
          try
          {
            var diagDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceStudio", "crashes");
            System.IO.Directory.CreateDirectory(diagDir);
            var path = System.IO.Path.Combine(diagDir, "startup_diag.txt");
            System.IO.File.AppendAllText(path, $"[{DateTime.UtcNow:O}] PanelHost.CreatePanel failed: panelId={panelId}, ex={ex}\n");
          }
          catch (Exception diagEx) { System.Diagnostics.Debug.WriteLine($"[PanelHost] Diagnostic write failed (non-fatal): {diagEx.Message}"); }
#endif
          if (ErrorPresentationService.IsBackendOffline)
            ShowOfflineOverlayIfApplicable();
          else
            ShowLoadErrorOverlay($"Failed to create panel: {ex.Message}");
          return null;
        }

        if (panel == null || _loadingCts.IsCancellationRequested)
          return null;

        var startTime = DateTime.UtcNow;

        // Re-acquire lock to commit or handle duplicate-load race
        await _loadLock.WaitAsync(_loadingCts.Token);
        try
        {
          if (_isUnloaded)
          {
            if (panel is UserControl ucUnloaded)
            {
              await DeactivateViewModelThenDisposeAsync(ucUnloaded.DataContext, CancellationToken.None).ConfigureAwait(true);
            }
            return null;
          }

          // Race guard: another thread may have loaded it while we were creating
          if (_loadedPanels.TryGetValue(panelId, out var existing))
          {
            System.Diagnostics.Debug.WriteLine($"[PanelHost] Duplicate load race for {panelId} — discarding newly created instance");
            if (panel is UserControl uc2)
            {
              await DeactivateViewModelThenDisposeAsync(uc2.DataContext, _loadingCts.Token).ConfigureAwait(true);
            }
            TouchLru(panelId);
            LoadErrorMessage = string.Empty;
            HostedPanel = existing;
            return existing;
          }

          _loadedPanels[panelId] = panel;
          _lruOrder.Add(panelId);
          await EvictIfOverCapacityAsync(panelId, _loadingCts.Token).ConfigureAwait(true);
          LoadErrorMessage = string.Empty;
          HostedPanel = panel;
          var loadTime = DateTime.UtcNow - startTime;
          System.Diagnostics.Debug.WriteLine($"[PanelHost] Loaded panel {panelId} in {loadTime.TotalMilliseconds:F1}ms");
          return panel;
        }
        finally
        {
          _loadLock.Release();
        }
      }
      catch (OperationCanceledException)
      {
        // Loading was cancelled, this is expected
        return null;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[PanelHost] Error loading panel {panelId}: {ex.Message}");
#if DEBUG
        try
        {
          var diagDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceStudio", "crashes");
          System.IO.Directory.CreateDirectory(diagDir);
          var path = System.IO.Path.Combine(diagDir, "startup_diag.txt");
          System.IO.File.AppendAllText(path, $"[{DateTime.UtcNow:O}] PanelHost.LoadPanelAsync failed: panelId={panelId}, ex={ex}\n");
        }
        catch (Exception diagEx) { System.Diagnostics.Debug.WriteLine($"[PanelHost] Diagnostic write failed (non-fatal): {diagEx.Message}"); }
#endif
        if (ErrorPresentationService.IsBackendOffline)
          ShowOfflineOverlayIfApplicable();
        else
          ShowLoadErrorOverlay($"Failed to load panel: {ex.Message}");
        return null;
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void ShowLoadErrorOverlay(string message)
    {
      LoadErrorMessage = message;
      LoadErrorTitle = "Failed to load panel";
      HostedPanel = null;
    }

    private async void ErrorOverlay_RetryRequested(object? sender, EventArgs e)
    {
      LoadErrorMessage = string.Empty;
      if (_lastRequestedPanelId != null)
        await LoadPanelAsync(_lastRequestedPanelId, _lastRequestedLegacyFactory);
    }

    private void ShowOfflineOverlayIfApplicable()
    {
      if (!ErrorPresentationService.IsBackendOffline)
        return;

      if (_offlineOverlay != null)
      {
        LoadErrorMessage = string.Empty;
        HostedPanel = _offlineOverlay;
        return;
      }

      var icon = new FontIcon
      {
        Glyph = "\uE783",
        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
        FontSize = 32,
        Opacity = 0.6
      };

      var heading = new TextBlock
      {
        Text = "Backend is offline",
        FontSize = 16,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center
      };

      var detail = new TextBlock
      {
        Text = "The backend server is not responding.\nIt will reconnect automatically, or you can retry manually.",
        FontSize = 13,
        Opacity = 0.7,
        TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
      };

      var retryButton = new Button
      {
        Content = "Retry",
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 8, 0, 0)
      };
      retryButton.Click += async (_, _) =>
      {
        var monitor = BackendConnectionMonitor.Current;
        if (monitor != null)
          await monitor.ForceReconnectAsync();

        if (_lastRequestedPanelId != null)
          await LoadPanelAsync(_lastRequestedPanelId, _lastRequestedLegacyFactory);
      };

      var stack = new StackPanel
      {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = 8
      };
      stack.Children.Add(icon);
      stack.Children.Add(heading);
      stack.Children.Add(detail);
      stack.Children.Add(retryButton);

      _offlineOverlay = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
      _offlineOverlay.Children.Add(stack);
      LoadErrorMessage = string.Empty;
      HostedPanel = _offlineOverlay;
    }

    private void EnsureReachabilitySubscription()
    {
      if (_subscribedToReachability)
        return;
      _subscribedToReachability = true;
      ErrorPresentationService.BackendReachabilityChanged += OnBackendReachabilityChanged;
    }

    private void OnBackendReachabilityChanged(object? sender, bool reachable)
    {
      if (!reachable || _lastRequestedPanelId == null)
        return;

      if (HostedPanel != _offlineOverlay)
        return;

      DispatcherQueue?.TryEnqueue(() =>
      {
        _ = LoadPanelAsync(_lastRequestedPanelId, _lastRequestedLegacyFactory);
      });
    }

    /// <summary>
    /// Checks if a panel is loaded in the cache.
    /// </summary>
    public bool IsPanelLoaded(string panelId)
    {
      return _loadedPanels.ContainsKey(panelId);
    }

    /// <summary>
    /// Unloads a panel from memory asynchronously.
    /// </summary>
    public async Task UnloadPanelAsync(string panelId)
    {
      UserControl? userControl = null;
      UIElement? removed = null;
      await _loadLock.WaitAsync().ConfigureAwait(false);
      try
      {
        if (_loadedPanels.TryRemove(panelId, out var panel))
        {
          _lruOrder.Remove(panelId);
          userControl = panel as UserControl;
          removed = panel;
        }
      }
      finally
      {
        _loadLock.Release();
      }

      if (removed == null)
        return;

      if (userControl != null)
      {
        await TeardownPanelUserControlOnDispatcherAsync(userControl, CancellationToken.None).ConfigureAwait(false);
      }
      else
      {
        var vm = GetViewModelFromContent(removed);
        await DeactivateViewModelThenDisposeAsync(vm, CancellationToken.None).ConfigureAwait(true);
      }

      if (removed is IDisposable disposable)
      {
        try
        {
          disposable.Dispose();
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"[PanelHost] Panel dispose failed for {panelId}: {ex.Message}");
        }
      }

      System.Diagnostics.Debug.WriteLine($"[PanelHost] Unloaded panel: {panelId}");
    }

    private async Task TeardownPanelUserControlOnDispatcherAsync(UserControl uc, CancellationToken ct)
    {
      var dq = DispatcherQueue;
      if (dq == null || dq.HasThreadAccess)
      {
        await DeactivateViewModelThenDisposeAsync(uc.DataContext, ct).ConfigureAwait(true);
        return;
      }

      var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(() => { _ = CompleteUnloadTeardownOnUiAsync(tcs, uc, ct); });
      try
      {
        await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "PanelHost.TeardownPanelUserControlOnDispatcherAsync");
      }
    }

    private async Task CompleteUnloadTeardownOnUiAsync(TaskCompletionSource tcs, UserControl uc, CancellationToken ct)
    {
      try
      {
        await DeactivateViewModelThenDisposeAsync(uc.DataContext, ct).ConfigureAwait(true);
        tcs.TrySetResult();
      }
      catch (Exception ex)
      {
        tcs.TrySetException(ex);
      }
    }

    /// <summary>
    /// Gets the PanelRegistry instance for external access.
    /// </summary>
    public IPanelRegistry? PanelRegistryInstance => _panelRegistry;

    /// <summary>
    /// Updates the context-sensitive action bar based on the current panel content.
    /// Implements IDEA 2: Context-Sensitive Action Bar in PanelHost Headers.
    /// </summary>
    private void UpdateActionBar(UIElement? content)
    {
      if (ActionBar == null)
        return;

      // Clear existing actions
      ActionBar.Children.Clear();

      if (content == null)
      {
        ActionBar.Visibility = Visibility.Collapsed;
        return;
      }

      // Try to get ViewModel from content
      IPanelActionable? actionable = null;

      if (content is UserControl userControl)
      {
        actionable = userControl.DataContext as IPanelActionable;
      }
      else if (content is FrameworkElement frameworkElement)
      {
        actionable = frameworkElement.DataContext as IPanelActionable;
      }

      if (actionable == null)
      {
        ActionBar.Visibility = Visibility.Collapsed;
        return;
      }

      // Get header actions from panel
      var actions = actionable.GetHeaderActions()?.ToList() ?? new List<PanelHeaderAction>();

      if (actions.Count == 0)
      {
        ActionBar.Visibility = Visibility.Collapsed;
        return;
      }

      // Limit to 4 actions to maintain compactness
      var actionsToShow = actions.Where(a => a.IsVisible).Take(4).ToList();

      if (actionsToShow.Count == 0)
      {
        ActionBar.Visibility = Visibility.Collapsed;
        return;
      }

      // Create AppBarButtons for each action
      foreach (var action in actionsToShow)
      {
        var button = new AppBarButton
        {
          Label = action.Name,
          IsEnabled = action.IsEnabled,
          Command = action.Command,
          Width = 32,
          Height = 32
        };
        if (!string.IsNullOrEmpty(action.Tooltip))
        {
          ToolTipService.SetToolTip(button, action.Tooltip);
        }

        // Set icon - try FontIcon first, fallback to SymbolIcon or TextBlock
        if (action.Icon.Length == 1 && char.IsSymbol(action.Icon[0]))
        {
          // Single character symbol - use FontIcon
          button.Icon = new FontIcon { Glyph = action.Icon };
        }
        else if (action.Icon.StartsWith("&#x") || action.Icon.StartsWith("\\u"))
        {
          // Unicode escape sequence
          button.Icon = new FontIcon { Glyph = action.Icon };
        }
        else
        {
          // Emoji or text - use TextBlock as icon
          button.Content = new TextBlock
          {
            Text = action.Icon,
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
          };
        }

        AutomationProperties.SetName(button, action.Name);
        if (!string.IsNullOrEmpty(action.KeyboardShortcut))
        {
          AutomationProperties.SetHelpText(button, $"Keyboard shortcut: {action.KeyboardShortcut}");
        }

        ActionBar.Children.Add(button);
      }

      ActionBar.Visibility = Visibility.Visible;
    }

    #region Panel Docking Visual Feedback (IDEA 14)

    /// <summary>
    /// Handles the start of a drag operation from the panel header.
    /// </summary>
    private void HeaderGrid_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
      _isDragging = true;

      // Set drag data
      args.Data.SetText(PanelTitleTextBlock?.Text ?? "Panel");
      args.Data.Properties.Add("PanelHost", this);
      args.Data.Properties.Add("PanelRegion", _region);
      string panelId = string.Empty;
      if (HostedPanel is UIElement content && GetPanelIdFromContent(content, out var id))
      {
        panelId = id ?? string.Empty;
      }
      args.Data.Properties.Add("PanelId", panelId);

      // Create drag preview
      if (_dragDropService != null)
      {
        var preview = _dragDropService.CreateDragPreview(HeaderBorder, PanelTitleTextBlock?.Text ?? "Panel");
        args.DragUI.SetContentFromDataPackage();
      }

      // Show drop zones in MainWindow (this will be handled by MainWindow)
      ShowDropZones();
    }

    /// <summary>
    /// Handles drag over events to show visual feedback.
    /// </summary>
    private void HeaderGrid_DragOver(object _, DragEventArgs e)
    {
      if (!_isDragging)
        return;

      e.AcceptedOperation = DataPackageOperation.Move;
      e.DragUIOverride.IsGlyphVisible = false;
      e.DragUIOverride.Caption = "Dock Panel";

      // Determine which drop zone the cursor is over
      var position = e.GetPosition(RootGrid);
      var dropZone = GetDropZoneFromPosition(position.X, position.Y);

      if (dropZone != _currentDropZone)
      {
        _currentDropZone = dropZone;
        UpdateDropZoneVisuals(dropZone);
      }
    }

    /// <summary>
    /// Handles drop events to dock the panel.
    /// </summary>
    private void HeaderGrid_Drop(object _, DragEventArgs e)
    {
      if (!_isDragging)
        return;

      var targetRegion = _currentDropZone;
      if (targetRegion.HasValue && targetRegion.Value != _region)
      {
        // Trigger panel docking event (MainWindow will handle the actual docking)
        OnPanelDockRequested?.Invoke(this, new PanelDockEventArgs
        {
          SourcePanelHost = this,
          SourceRegion = _region,
          TargetRegion = targetRegion.Value
        });
      }

      HideDropZones();
      _isDragging = false;
      _currentDropZone = null;
    }

    /// <summary>
    /// Handles drag leave events to clean up visual feedback.
    /// </summary>
    private void HeaderGrid_DragLeave(object _, DragEventArgs e)
    {
      HideDropZones();
      _currentDropZone = null;
    }

    /// <summary>
    /// Determines which drop zone the cursor position is over.
    /// </summary>
    private PanelRegion? GetDropZoneFromPosition(double x, double y)
    {
      if (!(this.FindName("RootGrid") is FrameworkElement rootGrid))
        return null;

      var width = rootGrid.ActualWidth;
      var height = rootGrid.ActualHeight;

      if (width == 0 || height == 0)
        return null;

      var leftThreshold = width * 0.2;  // Left 20%
      var rightThreshold = width * 0.8; // Right 20%
      var bottomThreshold = height * 0.85; // Bottom 15%

      // Check bottom first (smaller area)
      if (y > bottomThreshold)
      {
        return PanelRegion.Bottom;
      }
      // Check left
      else if (x < leftThreshold)
      {
        return PanelRegion.Left;
      }
      // Check right
      else if (x > rightThreshold)
      {
        return PanelRegion.Right;
      }
      // Default to center
      else
      {
        return PanelRegion.Center;
      }
    }

    /// <summary>
    /// Shows drop zone indicators.
    /// </summary>
    private void ShowDropZones()
    {
      if (DropZoneOverlay == null)
        return;

      DropZoneOverlay.Visibility = Visibility.Visible;

      // Show drag shadow on source panel
      if (DragShadow != null)
      {
        DragShadow.Visibility = Visibility.Visible;
        var shadowFade = new DoubleAnimation
        {
          To = 0.5,
          Duration = TimeSpan.FromMilliseconds(200)
        };
        Storyboard.SetTarget(shadowFade, DragShadow);
        Storyboard.SetTargetProperty(shadowFade, "Opacity");
        var shadowStoryboard = new Storyboard();
        shadowStoryboard.Children.Add(shadowFade);
        shadowStoryboard.Begin();
      }

      // Reduce opacity of source panel to show it's being dragged
      var sourceFade = new DoubleAnimation
      {
        To = 0.6,
        Duration = TimeSpan.FromMilliseconds(200)
      };
      Storyboard.SetTarget(sourceFade, RootGrid);
      Storyboard.SetTargetProperty(sourceFade, "Opacity");
      var sourceStoryboard = new Storyboard();
      sourceStoryboard.Children.Add(sourceFade);
      sourceStoryboard.Begin();

      // Animate drop zones in
      AnimateDropZone(LeftDropZone, 0);
      AnimateDropZone(CenterDropZone, 50);
      AnimateDropZone(RightDropZone, 100);
      AnimateDropZone(BottomDropZone, 150);
    }

    /// <summary>
    /// Hides drop zone indicators.
    /// </summary>
    private void HideDropZones()
    {
      if (DropZoneOverlay == null)
        return;

      // Animate drop zones out
      AnimateDropZoneOut(LeftDropZone);
      AnimateDropZoneOut(CenterDropZone);
      AnimateDropZoneOut(RightDropZone);
      AnimateDropZoneOut(BottomDropZone);

      // Hide drag shadow
      if (DragShadow != null)
      {
        var shadowFadeOut = new DoubleAnimation
        {
          To = 0,
          Duration = TimeSpan.FromMilliseconds(200)
        };
        Storyboard.SetTarget(shadowFadeOut, DragShadow);
        Storyboard.SetTargetProperty(shadowFadeOut, "Opacity");
        var shadowStoryboard = new Storyboard();
        shadowStoryboard.Children.Add(shadowFadeOut);
        shadowStoryboard.Completed += (_, _) =>
        {
          if (DragShadow != null)
            DragShadow.Visibility = Visibility.Collapsed;
        };
        shadowStoryboard.Begin();
      }

      // Restore source panel opacity
      var sourceFadeIn = new DoubleAnimation
      {
        To = 1.0,
        Duration = TimeSpan.FromMilliseconds(200)
      };
      Storyboard.SetTarget(sourceFadeIn, RootGrid);
      Storyboard.SetTargetProperty(sourceFadeIn, "Opacity");
      var sourceStoryboard = new Storyboard();
      sourceStoryboard.Children.Add(sourceFadeIn);
      sourceStoryboard.Begin();

      // Hide overlay after animation
      var hideStoryboard = new Storyboard();
      var fadeOut = new DoubleAnimation
      {
        To = 0,
        Duration = TimeSpan.FromMilliseconds(200)
      };
      Storyboard.SetTarget(fadeOut, DropZoneOverlay);
      Storyboard.SetTargetProperty(fadeOut, "Opacity");
      hideStoryboard.Children.Add(fadeOut);
      hideStoryboard.Completed += (_, _) =>
      {
        if (DropZoneOverlay != null)
          DropZoneOverlay.Visibility = Visibility.Collapsed;
      };
      hideStoryboard.Begin();
    }

    /// <summary>
    /// Updates visual feedback for the current drop zone.
    /// </summary>
    private void UpdateDropZoneVisuals(PanelRegion? dropZone)
    {
      // Reset all drop zones
      var leftDropZone = this.FindName("LeftDropZone") as Border;
      var centerDropZone = this.FindName("CenterDropZone") as Border;
      var rightDropZone = this.FindName("RightDropZone") as Border;
      var bottomDropZone = this.FindName("BottomDropZone") as Border;
      ResetDropZone(leftDropZone);
      ResetDropZone(centerDropZone);
      ResetDropZone(rightDropZone);
      ResetDropZone(bottomDropZone);

      // Highlight the active drop zone
      Border? activeZone = dropZone switch
      {
        PanelRegion.Left => leftDropZone,
        PanelRegion.Center => centerDropZone,
        PanelRegion.Right => rightDropZone,
        PanelRegion.Bottom => bottomDropZone,
        _ => null
      };

      if (activeZone != null)
      {
        HighlightDropZone(activeZone);
        if (dropZone.HasValue)
        {
          ShowDockPreview(dropZone.Value);
        }
      }
      else
      {
        HideDockPreview();
      }
    }

    /// <summary>
    /// Animates a drop zone in.
    /// </summary>
    private void AnimateDropZone(Border? zone, int delayMs)
    {
      if (zone == null)
        return;

      var storyboard = new Storyboard();

      // Fade in
      var fadeIn = new DoubleAnimation
      {
        From = 0,
        To = 0.8,
        Duration = TimeSpan.FromMilliseconds(300),
        BeginTime = TimeSpan.FromMilliseconds(delayMs)
      };
      Storyboard.SetTarget(fadeIn, zone);
      Storyboard.SetTargetProperty(fadeIn, "Opacity");
      storyboard.Children.Add(fadeIn);

      // Scale in
      var scaleTransform = new Microsoft.UI.Xaml.Media.ScaleTransform();
      zone.RenderTransform = scaleTransform;
      var scaleX = new DoubleAnimation
      {
        From = 0.8,
        To = 1.0,
        Duration = TimeSpan.FromMilliseconds(300),
        BeginTime = TimeSpan.FromMilliseconds(delayMs)
      };
      Storyboard.SetTarget(scaleX, scaleTransform);
      Storyboard.SetTargetProperty(scaleX, "ScaleX");
      storyboard.Children.Add(scaleX);

      var scaleY = new DoubleAnimation
      {
        From = 0.8,
        To = 1.0,
        Duration = TimeSpan.FromMilliseconds(300),
        BeginTime = TimeSpan.FromMilliseconds(delayMs)
      };
      Storyboard.SetTarget(scaleY, scaleTransform);
      Storyboard.SetTargetProperty(scaleY, "ScaleY");
      storyboard.Children.Add(scaleY);

      storyboard.Begin();
    }

    /// <summary>
    /// Animates a drop zone out.
    /// </summary>
    private void AnimateDropZoneOut(Border? zone)
    {
      if (zone == null)
        return;

      var storyboard = new Storyboard();
      var fadeOut = new DoubleAnimation
      {
        To = 0,
        Duration = TimeSpan.FromMilliseconds(200)
      };
      Storyboard.SetTarget(fadeOut, zone);
      Storyboard.SetTargetProperty(fadeOut, "Opacity");
      storyboard.Children.Add(fadeOut);
      storyboard.Begin();
    }

    /// <summary>
    /// Highlights a drop zone.
    /// </summary>
    private void HighlightDropZone(Border zone)
    {
      var storyboard = new Storyboard();

      // Increase opacity
      var fadeIn = new DoubleAnimation
      {
        To = 1.0,
        Duration = TimeSpan.FromMilliseconds(150)
      };
      Storyboard.SetTarget(fadeIn, zone);
      Storyboard.SetTargetProperty(fadeIn, "Opacity");
      storyboard.Children.Add(fadeIn);

      // Pulse border
      var borderAnimation = new DoubleAnimation
      {
        To = 5,
        Duration = TimeSpan.FromMilliseconds(150)
      };
      Storyboard.SetTarget(borderAnimation, zone);
      Storyboard.SetTargetProperty(borderAnimation, "(Border.BorderThickness)");
      storyboard.Children.Add(borderAnimation);

      storyboard.Begin();
    }

    /// <summary>
    /// Resets a drop zone to default state.
    /// </summary>
    private void ResetDropZone(Border? zone)
    {
      if (zone == null)
        return;

      zone.Opacity = 0.3;
      zone.BorderThickness = new Thickness(3);
    }

    /// <summary>
    /// Shows dock preview indicator.
    /// </summary>
    private void ShowDockPreview(PanelRegion region)
    {
      if (DockPreviewIndicator == null || DockPreviewText == null)
        return;

      var (regionName, icon) = region switch
      {
        PanelRegion.Left => ("Left", "◀"),
        PanelRegion.Center => ("Center", "⬌"),
        PanelRegion.Right => ("Right", "▶"),
        PanelRegion.Bottom => ("Bottom", "▼"),
        _ => ("Here", "⚓")
      };

      // Update icon if DockPreviewIcon exists (search within DockPreviewIndicator)
      var dockPreviewIcon = DockPreviewIndicator.FindName("DockPreviewIcon") as TextBlock;
      if (dockPreviewIcon == null && DockPreviewIndicator is FrameworkElement fe)
      {
        // Try finding it in the visual tree
        dockPreviewIcon = FindVisualChild<TextBlock>(fe, "DockPreviewIcon");
      }
      if (dockPreviewIcon != null)
      {
        dockPreviewIcon.Text = icon;
      }

      DockPreviewText.Text = $"Dock to {regionName}";
      DockPreviewIndicator.Visibility = Visibility.Visible;

      var storyboard = new Storyboard();
      var fadeIn = new DoubleAnimation
      {
        To = 0.9,
        Duration = TimeSpan.FromMilliseconds(200)
      };
      Storyboard.SetTarget(fadeIn, DockPreviewIndicator);
      Storyboard.SetTargetProperty(fadeIn, "Opacity");
      storyboard.Children.Add(fadeIn);
      storyboard.Begin();
    }

    /// <summary>
    /// Helper method to find a visual child by name.
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject parent, string childName) where T : DependencyObject
    {
      for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
      {
        var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
        if (child is T t && child is FrameworkElement fe && fe.Name == childName)
        {
          return t;
        }
        var childOfChild = FindVisualChild<T>(child, childName);
        if (childOfChild != null)
        {
          return childOfChild;
        }
      }
      return null;
    }

    /// <summary>
    /// Hides dock preview indicator.
    /// </summary>
    private void HideDockPreview()
    {
      if (DockPreviewIndicator == null)
        return;

      var storyboard = new Storyboard();
      var fadeOut = new DoubleAnimation
      {
        To = 0,
        Duration = TimeSpan.FromMilliseconds(150)
      };
      Storyboard.SetTarget(fadeOut, DockPreviewIndicator);
      Storyboard.SetTargetProperty(fadeOut, "Opacity");
      storyboard.Children.Add(fadeOut);
      storyboard.Completed += (_, _) =>
      {
        if (DockPreviewIndicator != null)
          DockPreviewIndicator.Visibility = Visibility.Collapsed;
      };
      storyboard.Begin();
    }

    /// <summary>
    /// Event raised when a panel dock is requested.
    /// </summary>
    public event EventHandler<PanelDockEventArgs>? OnPanelDockRequested;

    #endregion Panel Docking Visual Feedback (IDEA 14)
  }

  /// <summary>
  /// Event arguments for panel docking.
  /// </summary>
  public class PanelDockEventArgs : EventArgs
  {
    public PanelHost SourcePanelHost { get; set; } = null!;
    public PanelRegion SourceRegion { get; set; }
    public PanelRegion TargetRegion { get; set; }
  }
}