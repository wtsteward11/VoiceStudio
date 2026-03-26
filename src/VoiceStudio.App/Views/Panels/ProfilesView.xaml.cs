using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Services;
using VoiceStudio.App.Controls;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using Windows.Foundation;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using SelectionChangedEventArgsAlias = Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs;
using MultiSelectSelectionChangedEventArgs = VoiceStudio.App.Services.SelectionChangedEventArgs;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class ProfilesView : UserControl, INavigatablePanel
  {
    /// <summary>
    /// ViewModel is set by PanelRegistry via DataContext (DI path). Do not construct in View.
    /// </summary>
    public ProfilesViewModel ViewModel => (ProfilesViewModel)(DataContext ?? throw new InvalidOperationException("ProfilesView DataContext must be set by PanelRegistry."));

    private VoiceProfile? _lastSelectedProfile;
    private VoiceProfile? _draggedProfile;
    private DragDropVisualFeedbackService? _dragDropService;
    private VoiceStudio.Core.Services.IDragDropService? _panelDragDropService;
    private ToastNotificationService? _toastService;
    private IErrorLoggingService? _errorLoggingService;
    private MultiSelectService? _multiSelectService;

    public ProfilesView()
    {
      this.InitializeComponent();

      // Initialize services (no ViewModel access in constructor)
      _dragDropService = AppServices.GetDragDropVisualFeedbackService();
      _panelDragDropService = AppServices.TryGetDragDropService();
      _toastService = AppServices.TryGetToastNotificationService();
      _errorLoggingService = AppServices.TryGetErrorLoggingService();
      _multiSelectService = AppServices.GetMultiSelectService();

      _multiSelectService.SelectionChanged += OnMultiSelectSelectionChanged;
      this.Unloaded += ProfilesView_Unloaded;
      this.KeyDown += ProfilesView_KeyDown;
      this.Loaded += ProfilesView_Loaded;

      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () => { });
    }

    private void ProfilesView_Loaded(object sender, RoutedEventArgs e)
    {
      // DataContext is set by PanelRegistry.CreatePanel; wire View-specific callback and subscriptions
      var vm = ViewModel;
      vm.GetProfileNameFromUser = ShowCreateProfileDialogAsync;
      vm.PropertyChanged += OnViewModelPropertyChanged;

      ProfilesView_KeyboardNavigation_Loaded(sender, e);
      EnsureEmptyStateControl();
      UpdateEmptyStateVisibility();
    }

    private Controls.EmptyState? _profilesEmptyState;

    private void OnMultiSelectSelectionChanged(object? sender, MultiSelectSelectionChangedEventArgs e)
    {
      if (e.PanelId == ViewModel.PanelId)
        UpdateSelectionVisuals();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
      if (e.PropertyName == nameof(ProfilesViewModel.Profiles) ||
          e.PropertyName == nameof(ProfilesViewModel.SelectedCount))
      {
        UpdateSelectionVisuals();
      }
      if (e.PropertyName is nameof(ProfilesViewModel.FilteredCount) or nameof(ProfilesViewModel.IsLoading) or nameof(ProfilesViewModel.ErrorMessage))
      {
        UpdateEmptyStateVisibility();
      }
    }

    private void ProfilesView_Unloaded(object sender, RoutedEventArgs e)
    {
      this.Unloaded -= ProfilesView_Unloaded;
      if (_multiSelectService != null)
      {
        _multiSelectService.SelectionChanged -= OnMultiSelectSelectionChanged;
        _multiSelectService = null;
      }
      if (ViewModel != null)
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void EnsureEmptyStateControl()
    {
      if (_profilesEmptyState != null || ProfilesEmptyStateOverlay == null)
        return;
      ProfilesEmptyStateOverlay.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["VSQ.Panel.Background"];
      _profilesEmptyState = new Controls.EmptyState
      {
        Icon = "\uE77B",
        Title = ViewModel.EmptyStateTitle,
        Message = ViewModel.EmptyStateMessage,
        ActionText = ViewModel.EmptyStateActionText,
        ActionCommand = ViewModel.CreateProfileFromEmptyStateCommand
      };
      ProfilesEmptyStateOverlay.Children.Clear();
      ProfilesEmptyStateOverlay.Children.Add(_profilesEmptyState);
    }

    private void UpdateEmptyStateVisibility()
    {
      if (ProfilesEmptyStateOverlay == null)
        return;
      EnsureEmptyStateControl();
      ProfilesEmptyStateOverlay.Visibility = ViewModel.ShowEmptyState ? Visibility.Visible : Visibility.Collapsed;
      if (_profilesEmptyState != null)
      {
        _profilesEmptyState.Title = ViewModel.EmptyStateTitle;
        _profilesEmptyState.Message = ViewModel.EmptyStateMessage;
        _profilesEmptyState.ActionText = ViewModel.EmptyStateActionText;
      }
    }

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      HelpOverlay.Title = "Voice Profiles Help";
      HelpOverlay.HelpText = "The Profiles panel displays all your voice profiles in a grid layout. Each profile card shows the voice name, quality score, language, emotion, and tags. Click a profile to select it and view details in the side panel. Use multi-select (Ctrl+Click or Shift+Click) to select multiple profiles for batch operations. Drag profiles to reorder them or move them to other panels. Right-click profiles for context menus with additional options.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+A", Description = "Select all profiles" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Clear selection" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+C", Description = "Copy selected profiles" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected profiles" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh profiles" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Click a profile card to view details in the side panel");
      HelpOverlay.Tips.Add("Use multi-select (Ctrl+Click or Shift+Click) to select multiple profiles");
      HelpOverlay.Tips.Add("Right-click profiles for context menus with more options");
      HelpOverlay.Tips.Add("Quality scores help you identify the best voice profiles");
      HelpOverlay.Tips.Add("Tags help organize and filter profiles");
      HelpOverlay.Tips.Add("Drag profiles to reorder them in the grid");
      HelpOverlay.Tips.Add("Use batch operations to delete or export multiple profiles at once");
      HelpOverlay.Tips.Add("The selection count badge shows how many profiles are selected");

      HelpOverlay.Visibility = Visibility.Visible;
      HelpOverlay.Show();
    }

    private async void CreateProfileButton_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        var name = await ShowCreateProfileDialogAsync();
        if (!string.IsNullOrWhiteSpace(name))
          await ViewModel.CreateProfileCommand.ExecuteAsync(name);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error creating profile: {ex.Message}");
        _errorLoggingService?.LogError(ex, "CreateProfileButton_Click");
      }
    }

    private async Task<string?> ShowCreateProfileDialogAsync()
    {
      var dialogService = AppServices.TryGetDialogService();
      if (dialogService == null)
        return null;

      var name = await dialogService.ShowInputAsync(
          "Create New Profile",
          "Profile name",
          defaultValue: null,
          placeholder: "Enter profile name...");
      return !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;
    }

    private void ProfileCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is Border border && border.DataContext is VoiceProfile profile)
      {
        e.Handled = true;
        var menuService = ServiceProvider.GetContextMenuService();
        var menu = menuService.CreateContextMenu("profile", profile);

        // Wire up menu item commands
        WireUpProfileMenuCommands(menu, profile);

        var position = e.GetPosition(border);
        menuService.ShowContextMenu(menu, border, position);
      }
    }

    private void ProfilesEmptyArea_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      e.Handled = true;
      var menuService = ServiceProvider.GetContextMenuService();
      var menu = menuService.CreateContextMenu("profile", null);

      // Wire up menu item commands for empty area
      WireUpProfileMenuCommands(menu, null);

      if (sender is ScrollViewer scrollViewer)
      {
        var position = e.GetPosition(scrollViewer);
        menuService.ShowContextMenu(menu, scrollViewer, position);
      }
    }

    private void WireUpProfileMenuCommands(MenuFlyout menu, VoiceProfile? profile)
    {
      foreach (var item in menu.Items)
      {
        if (item is MenuFlyoutItem menuItem)
        {
          menuItem.Click += (_, __) => HandleProfileMenuClick(menuItem.Text, profile);
        }
      }
    }

    private async void HandleProfileMenuClick(string action, VoiceProfile? profile)
    {
      try
      {
        switch (action.ToLower())
        {
          case "new profile":
            {
              var dialogService = AppServices.TryGetDialogService();
              if (dialogService == null) break;

              var name = await dialogService.ShowInputAsync(
                  "Create New Profile",
                  "Profile name",
                  placeholder: "Profile name");
              if (!string.IsNullOrWhiteSpace(name))
                await ViewModel.CreateProfileCommand.ExecuteAsync(name.Trim());
            }
            break;
          case "import profile":
            _errorLoggingService?.LogInfo("Profile import requested", "ProfilesView");
            await ViewModel.ImportProfilesAsync();
            break;
          case "edit":
            if (profile != null)
            {
              ViewModel.SelectedProfile = profile;
              _errorLoggingService?.LogInfo($"Profile edit requested: {profile.Name}", "ProfilesView");

              var dialogService = AppServices.TryGetDialogService();
              if (dialogService is DialogService ds)
              {
                var editResult = await ds.ShowProfileEditAsync(
                    profile.Name ?? string.Empty,
                    profile.Language ?? string.Empty,
                    profile.Emotion ?? string.Empty,
                    profile.Tags != null ? string.Join(", ", profile.Tags) : string.Empty);
                if (editResult.HasValue)
                {
                  await ViewModel.UpdateProfileAsync(
                      profile,
                      editResult.Value.Name,
                      editResult.Value.Language,
                      editResult.Value.Emotion,
                      editResult.Value.Tags);
                }
              }
            }
            break;
          case "duplicate":
            if (profile != null)
            {
              _errorLoggingService?.LogInfo($"Profile duplicate requested: {profile.Name}", "ProfilesView");
              await ViewModel.DuplicateProfileAsync(profile);
            }
            break;
          case "delete":
            if (profile != null && !string.IsNullOrWhiteSpace(profile.Id))
            {
              await ViewModel.DeleteProfileCommand.ExecuteAsync(profile.Id);
            }
            break;
          case "export profile":
            if (profile != null)
            {
              _errorLoggingService?.LogInfo($"Profile export requested: {profile.Name}", "ProfilesView");
              await ViewModel.ExportProfileAsync(profile);
            }
            break;
          case "test voice":
          case "preview":
            if (profile != null && !string.IsNullOrWhiteSpace(profile.Id))
            {
              ViewModel.SelectedProfile = profile;
              await ViewModel.PreviewProfileCommand.ExecuteAsync(profile.Id);
            }
            break;
          case "analyze quality":
            if (profile != null)
            {
              _errorLoggingService?.LogInfo($"Quality analysis requested for profile: {profile.Name}", "ProfilesView");
              await ViewModel.AnalyzeProfileQualityAsync(profile);
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, $"HandleProfileMenuClick_{action}");
        _toastService?.ShowError("Error", $"Failed to execute action '{action}': {ex.Message}");
      }
    }

    private void ProfileCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      try
      {
        if (sender is Border border && border.DataContext is VoiceProfile profile)
        {
          var isCtrlPressed = InputHelper.IsControlPressed();
          var isShiftPressed = InputHelper.IsShiftPressed();

          ViewModel.ToggleSelection(profile.Id, isCtrlPressed, isShiftPressed);
          _lastSelectedProfile = profile;

          UpdateSelectionVisuals();
          e.Handled = true;
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error in ProfileCard_PointerPressed: {ex.Message}");
        _errorLoggingService?.LogError(ex, "ProfileCard_PointerPressed");
      }
    }

    private void ProfilesView_KeyDown(object _, KeyRoutedEventArgs e)
    {
      var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

      if (isCtrlPressed && e.Key == VirtualKey.A)
      {
        // Ctrl+A - Select all
        ViewModel.SelectAllCommand.Execute(null);
        UpdateSelectionVisuals();
        e.Handled = true;
      }
      else if (e.Key == VirtualKey.Escape)
      {
        // Escape - Clear selection
        ViewModel.ClearSelectionCommand.Execute(null);
        UpdateSelectionVisuals();
        e.Handled = true;
      }
    }

    private void UpdateSelectionVisuals()
    {
      // Update visual indicators for all profile cards
      // Walk through the visual tree to find all profile card borders and update their selection state
      UpdateSelectionVisualsRecursive(this);
    }

    private void UpdateSelectionVisualsRecursive(DependencyObject element)
    {
      if (element == null || ViewModel == null)
        return;

      // Check if this is a profile card border with a Tag (profile ID)
      if (element is Border border && border.Tag is string profileId)
      {
        var isSelected = ViewModel.IsProfileSelected(profileId);

        // Find the selection indicator child border
        var selectionIndicator = FindChild<Border>(border, "SelectionIndicator");
        if (selectionIndicator != null)
        {
          selectionIndicator.Visibility = isSelected
              ? Microsoft.UI.Xaml.Visibility.Visible
              : Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        // Update border brush to show selection
        if (isSelected)
        {
          border.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 183, 194)); // VSQ.Accent.Cyan
          border.BorderThickness = new Microsoft.UI.Xaml.Thickness(2);
        }
        else
        {
          border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)this.Resources["VSQ.Panel.BorderBrush"];
          border.BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
        }
      }

      // Recursively check children
      var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
      for (int i = 0; i < childCount; i++)
      {
        var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i);
        UpdateSelectionVisualsRecursive(child);
      }
    }

    private static T? FindChild<T>(DependencyObject? parent, string childName) where T : DependencyObject
    {
      if (parent == null) return null;

      for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
      {
        var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);

        if (child is T t && (child as FrameworkElement)?.Name == childName)
        {
          return t;
        }

        var foundChild = FindChild<T>(child, childName);
        if (foundChild != null)
        {
          return foundChild;
        }
      }

      return null;
    }

    private void EditProfileButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
      // Navigate to profile edit mode
      if (ViewModel?.SelectedProfile != null)
      {
        System.Diagnostics.Debug.WriteLine($"Edit profile: {ViewModel.SelectedProfile.Name}");
      }
    }

    // GAP-B18: BatchExport_Click - Removed, now using Command binding in XAML
    // The export functionality is now handled by ViewModel.ExportSelectedCommand

    private void Profile_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
      if (sender is Border border && border.DataContext is VoiceProfile profile)
      {
        _draggedProfile = profile;

        // Set drag data
        e.Data.SetText(profile.Id);
        e.Data.Properties.Add("ProfileId", profile.Id);
        e.Data.Properties.Add("ProfileName", profile.Name ?? "Unnamed Profile");

        // Reduce opacity of source element
        border.Opacity = 0.5;

        // Notify cross-panel drag service (Panel Architecture Phase 4)
        var payload = DragPayload.FromProfile(
          ViewModel.PanelId,
          profile.Id,
          profile.Name ?? "Unnamed Profile",
          profile.Language);
        _panelDragDropService?.StartDrag(payload);
      }
    }

    private void Profile_DragItemsCompleted(UIElement sender, DragItemsCompletedEventArgs e)
    {
      // Clean up drag state
      if (sender is Border border)
      {
        border.Opacity = 1.0;
      }

      _dragDropService?.Cleanup();

      // Cancel cross-panel drag if it wasn't completed by a drop target (Panel Architecture Phase 4)
      if (_panelDragDropService?.IsDragging == true)
      {
        _panelDragDropService.CancelDrag();
      }

      _draggedProfile = null;
    }

    private void Profile_DragOver(object sender, DragEventArgs e)
    {
      if (sender is Border border && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move | DataPackageOperation.Copy;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator
        var position = e.GetPosition(border);
        var dropPosition = DetermineDropPosition(border, position);
        _dragDropService.ShowDropTargetIndicator(border, dropPosition);
      }
    }

    private void Profile_Drop(object sender, DragEventArgs e)
    {
      if (sender is Border border && _draggedProfile != null && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;

        // Hide drop indicator
        _dragDropService.HideDropTargetIndicator();
        _dragDropService.Cleanup();

        if (border.DataContext is VoiceProfile targetProfile)
        {
          // Determine drop position based on pointer location
          var position = e.GetPosition(border);
          var dropPosition = DetermineDropPosition(border, position);
          _errorLoggingService?.LogInfo($"Profile reorder requested: {_draggedProfile.Name}", "ProfilesView");
          ViewModel.ReorderProfiles(_draggedProfile, targetProfile, dropPosition);
        }

        // Clean up drag state
        _draggedProfile = null;

        // Restore source element opacity
        if (e.OriginalSource is Border sourceBorder)
        {
          sourceBorder.Opacity = 1.0;
        }
      }
    }

    private void Profile_DragLeave(object _, DragEventArgs __)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    private DropPosition DetermineDropPosition(Border target, Point position)
    {
      // Determine if drop is before, after, or on the target
      var targetHeight = target.ActualHeight;
      var relativeY = position.Y;

      if (relativeY < targetHeight * 0.33)
        return DropPosition.Before;
      else if (relativeY > targetHeight * 0.67)
        return DropPosition.After;
      else
        return DropPosition.On;
    }

    private void QualityBadge_Clicked(object sender, RoutedEventArgs __)
    {
      // Find the profile card that contains this badge
      if (sender is Controls.QualityBadgeControl badge)
      {
        // Traverse up the visual tree to find the DataContext (VoiceProfile)
        var element = badge as FrameworkElement;
        while (element != null)
        {
          if (element.DataContext is VoiceProfile profile)
          {
            // Select the profile to show quality details in the details panel
            ViewModel.SelectedProfile = profile;
            _toastService?.ShowInfo("Quality Details", $"Viewing quality metrics for '{profile.Name}'");
            break;
          }
          element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element) as FrameworkElement;
        }
      }
    }

    private void DegradationTimeWindow_SelectionChanged(object sender, SelectionChangedEventArgsAlias e)
    {
      if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
      {
        if (int.TryParse(tag, out int days))
        {
          ViewModel.DegradationTimeWindowDays = days;
          // Note: Quality degradation checking is handled internally by the ViewModel
        }
      }
    }

    private void SeverityBadge_Loaded(object sender, RoutedEventArgs e)
    {
      if (sender is Border border && border.DataContext is QualityDegradationAlert alert)
      {
        // Set background color based on severity
        border.Background = alert.Severity.ToLower() switch
        {
          "critical" => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["VSQ.Accent.RedBrush"],
          "warning" => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["VSQ.Accent.OrangeBrush"],
          _ => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["VSQ.Accent.YellowBrush"]
        };
      }
    }

    private void ProfilesView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
    }

    private void ConfidenceBar_Loaded(object sender, RoutedEventArgs e)
    {
      if (sender is ProgressBar progressBar && progressBar.DataContext is QualityDegradationAlert alert)
      {
        // Set foreground color based on severity
        progressBar.Foreground = alert.Severity.ToLower() switch
        {
          "critical" => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["VSQ.Accent.RedBrush"],
          "warning" => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["VSQ.Accent.OrangeBrush"],
          _ => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["VSQ.Accent.YellowBrush"]
        };
      }
    }

    /// <inheritdoc />
    public async Task<bool> NavigateToItemAsync(
        string itemId,
        string resultType,
        CancellationToken ct,
        IReadOnlyDictionary<string, object>? searchMetadata = null)
    {
      _ = searchMetadata;
      if (!string.Equals(resultType, "profile", StringComparison.OrdinalIgnoreCase))
        return false;
      return await ViewModel.NavigateToProfileAsync(itemId, ct);
    }
  }
}