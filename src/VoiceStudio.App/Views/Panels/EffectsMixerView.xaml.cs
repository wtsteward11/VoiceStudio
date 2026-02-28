using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.Core.Models;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using SelectionChangedEventArgsAlias = Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class EffectsMixerView : UserControl
  {
    public EffectsMixerViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private DragDropVisualFeedbackService? _dragDropService;
    private Effect? _draggedEffect;

    public EffectsMixerView()
    {
      this.InitializeComponent();
      // Wire DataContext with BackendClient
      ViewModel = new EffectsMixerViewModel(
          ServiceProvider.GetBackendClient()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();
      _dragDropService = ServiceProvider.GetDragDropVisualFeedbackService();

      // Subscribe to selection changes to update UI (IDEA 12)
      var multiSelectService = ServiceProvider.GetMultiSelectService();
      multiSelectService.SelectionChanged += (s, e) =>
      {
        if (e.PanelId == ViewModel.PanelId)
        {
          UpdateChannelSelectionVisuals();
        }
      };

      // Update visuals when channels change
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(EffectsMixerViewModel.Channels) ||
                  e.PropertyName == nameof(EffectsMixerViewModel.SelectedChannelCount))
        {
          UpdateChannelSelectionVisuals();
        }
      };

      // Add keyboard handler for multi-select
      this.KeyDown += EffectsMixerView_KeyDown;

      // Setup keyboard navigation
      this.Loaded += EffectsMixerView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        // Close any open dialogs or overlays
      });
    }

    private void EffectsMixerView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
    }

    private void EffectsMixerView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

      if (isCtrlPressed && e.Key == VirtualKey.A)
      {
        // Ctrl+A - Select all channels
        ViewModel.SelectAllChannelsCommand.Execute(null);
        UpdateChannelSelectionVisuals();
        e.Handled = true;
      }
      else if (e.Key == VirtualKey.Escape)
      {
        // Escape - Clear channel selection
        ViewModel.ClearChannelSelectionCommand.Execute(null);
        UpdateChannelSelectionVisuals();
        e.Handled = true;
      }
    }

    private void Channel_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      if (sender is Border border && border.DataContext is MixerChannel channel)
      {
        var isCtrlPressed = InputHelper.IsControlPressed();
        var isShiftPressed = InputHelper.IsShiftPressed();

        ViewModel.ToggleChannelSelection(channel.Id, isCtrlPressed, isShiftPressed);

        UpdateChannelSelectionVisuals();
        e.Handled = true;
      }
    }

    private void UpdateChannelSelectionVisuals()
    {
      // Update visual indicators for all channel borders
      UpdateChannelSelectionVisualsRecursive(this);
    }

    private void UpdateChannelSelectionVisualsRecursive(DependencyObject element)
    {
      if (element == null || ViewModel == null)
        return;

      // Check if this is a channel border with a Tag (channel ID)
      if (element is Border border && border.Tag is string channelId && border.DataContext is MixerChannel channel)
      {
        var isSelected = ViewModel.IsChannelSelected(channelId);

        // Find the selection indicator child border
        var selectionIndicator = FindChild<Border>(border, "ChannelSelectionIndicator");
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
        UpdateChannelSelectionVisualsRecursive(child);
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

        var childOfChild = FindChild<T>(child, childName);
        if (childOfChild != null)
        {
          return childOfChild;
        }
      }

      return null;
    }

    // Drag-and-drop handlers for effect reordering
    private void Effect_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
      if (sender is Border border && border.DataContext is Effect effect)
      {
        _draggedEffect = effect;

        // Set drag data
        e.Data.SetText(effect.Id);
        e.Data.Properties.Add("EffectId", effect.Id);
        e.Data.Properties.Add("EffectName", effect.Name ?? "Unnamed Effect");

        // Reduce opacity of source element
        border.Opacity = 0.5;
      }
    }

    private void Effect_DragItemsCompleted(UIElement sender, DragItemsCompletedEventArgs _)
    {
      // Clean up drag state
      if (sender is Border border)
      {
        border.Opacity = 1.0;
      }

      _dragDropService?.Cleanup();

      _draggedEffect = null;
    }

    private void Effect_DragOver(object sender, DragEventArgs e)
    {
      if (sender is Border border && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator
        var position = e.GetPosition(border);
        var dropPosition = DetermineEffectDropPosition(border, position);
        _dragDropService.ShowDropTargetIndicator(border, dropPosition);
      }
    }

    private async void Effect_Drop(object sender, DragEventArgs e)
    {
      try
      {
        if (sender is Border border && _draggedEffect != null && _dragDropService != null)
        {
          e.AcceptedOperation = DataPackageOperation.Move;

          // Hide drop indicator
          _dragDropService.HideDropTargetIndicator();
          _dragDropService.Cleanup();

          // Get target effect
          if (border.DataContext is Effect targetEffect && ViewModel.SelectedEffectChain != null)
          {
            var draggedEffect = _draggedEffect;
            var targetEffectOrder = targetEffect.Order;
            var draggedEffectOrder = draggedEffect.Order;

            // Determine drop position
            var position = e.GetPosition(border);
            var dropPosition = DetermineEffectDropPosition(border, position);

            // Reorder effects
            if (dropPosition == DropPosition.Before && draggedEffectOrder > targetEffectOrder)
            {
              // Move dragged effect before target
              if (ViewModel.MoveEffectUpCommand.CanExecute(draggedEffect.Id))
              {
                // Move up until we reach the target position
                while (draggedEffect.Order > targetEffectOrder)
                {
                  await ViewModel.MoveEffectUpCommand.ExecuteAsync(draggedEffect.Id);
                }
              }
            }
            else if (dropPosition == DropPosition.After && draggedEffectOrder < targetEffectOrder)
            {
              // Move dragged effect after target
              if (ViewModel.MoveEffectDownCommand.CanExecute(draggedEffect.Id))
              {
                // Move down until we reach the target position
                while (draggedEffect.Order < targetEffectOrder)
                {
                  await ViewModel.MoveEffectDownCommand.ExecuteAsync(draggedEffect.Id);
                }
              }
            }
            else if (dropPosition == DropPosition.Before && draggedEffectOrder < targetEffectOrder)
            {
              // Move dragged effect before target (moving down)
              if (ViewModel.MoveEffectDownCommand.CanExecute(draggedEffect.Id))
              {
                // Move down until we're just before target
                while (draggedEffect.Order < targetEffectOrder - 1)
                {
                  await ViewModel.MoveEffectDownCommand.ExecuteAsync(draggedEffect.Id);
                }
              }
            }
            else if (dropPosition == DropPosition.After && draggedEffectOrder > targetEffectOrder)
            {
              // Move dragged effect after target (moving up)
              if (ViewModel.MoveEffectUpCommand.CanExecute(draggedEffect.Id))
              {
                // Move up until we're just after target
                while (draggedEffect.Order > targetEffectOrder + 1)
                {
                  await ViewModel.MoveEffectUpCommand.ExecuteAsync(draggedEffect.Id);
                }
              }
            }

            _toastService?.ShowToast(ToastType.Success, "Reordered", $"Moved {draggedEffect.Name} in effect chain");
          }

          // Clean up drag state
          _draggedEffect = null;

          // Restore source element opacity
          if (e.OriginalSource is Border sourceBorder)
          {
            sourceBorder.Opacity = 1.0;
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private void Effect_DragLeave(object _, DragEventArgs __)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    private DropPosition DetermineEffectDropPosition(Border target, Point position)
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

    private void RoutingComboBox_Loaded(object sender, RoutedEventArgs _)
    {
      if (sender is ComboBox comboBox && comboBox.DataContext is MixerChannel channel)
      {
        // Set initial selection based on channel's MainDestination
        if (channel.MainDestination == RoutingDestination.Master)
        {
          comboBox.SelectedIndex = 0; // Master
        }
        else if (channel.MainDestination == RoutingDestination.SubGroup)
        {
          comboBox.SelectedIndex = 1; // Sub-Group
        }
      }
    }

    private async void RoutingComboBox_SelectionChanged(object sender, SelectionChangedEventArgsAlias _)
    {
      try
      {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is string selectedTag)
        {
          // Find the channel in the visual tree
          var channel = GetChannelFromContext(comboBox);
          if (channel != null)
          {
            // Update channel routing destination
            if (selectedTag == "Master")
            {
              channel.MainDestination = RoutingDestination.Master;
              channel.SubGroupId = null;
            }
            else if (selectedTag == "SubGroup")
            {
              channel.MainDestination = RoutingDestination.SubGroup;
              // SubGroupId will be set by the sub-group selection combo
            }

            // Update sub-group combo visibility
            var subGroupCombo = FindChild<ComboBox>(comboBox.Parent as DependencyObject, "SubGroupComboBox");
            if (subGroupCombo != null)
            {
              subGroupCombo.Visibility = channel.MainDestination == RoutingDestination.SubGroup
                  ? Visibility.Visible
                  : Visibility.Collapsed;
            }

            // Auto-save if project is selected
            if (!string.IsNullOrWhiteSpace(ViewModel.SelectedProjectId))
            {
              await ViewModel.SaveMixerStateCommand.ExecuteAsync(null);
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private void SubGroupComboBox_Loaded(object sender, RoutedEventArgs e)
    {
      if (sender is ComboBox comboBox && comboBox.DataContext is MixerChannel channel)
      {
        // Show/hide based on routing destination
        comboBox.Visibility = channel.MainDestination == RoutingDestination.SubGroup
            ? Visibility.Visible
            : Visibility.Collapsed;
      }
    }

    private async void SubGroupComboBox_SelectionChanged(object sender, SelectionChangedEventArgsAlias e)
    {
      try
      {
        if (sender is ComboBox comboBox)
        {
          var channel = GetChannelFromContext(comboBox);
          if (channel != null)
          {
            // Update SubGroupId
            channel.SubGroupId = comboBox.SelectedValue as string;

            // Auto-save if project is selected
            if (!string.IsNullOrWhiteSpace(ViewModel.SelectedProjectId))
            {
              await ViewModel.SaveMixerStateCommand.ExecuteAsync(null);
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private void SendSlider_Loaded(object sender, RoutedEventArgs e)
    {
      if (sender is Slider slider && slider.Tag is string sendId)
      {
        var channel = GetChannelFromContext(slider);
        if (channel != null)
        {
          if (channel.SendLevels != null && channel.SendLevels.TryGetValue(sendId, out var level))
          {
            slider.Value = level;
          }
          else
          {
            slider.Value = 0.0;
          }
        }
      }
    }

    private void SendToggle_Loaded(object sender, RoutedEventArgs e)
    {
      if (sender is ToggleButton toggle && toggle.Tag is string sendId)
      {
        var channel = GetChannelFromContext(toggle);
        if (channel != null)
        {
          if (channel.SendEnabled != null && channel.SendEnabled.TryGetValue(sendId, out var enabled))
          {
            toggle.IsChecked = enabled;
          }
          else
          {
            toggle.IsChecked = false;
          }
        }
      }
    }

    private async void SendLevelSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
      try
      {
        if (sender is Slider slider && slider.Tag is string sendId)
        {
          var channel = GetChannelFromContext(slider);
          if (channel != null)
          {
            if (channel.SendLevels == null)
              channel.SendLevels = new System.Collections.Generic.Dictionary<string, double>();

            channel.SendLevels[sendId] = e.NewValue;

            // Auto-save if project is selected
            if (!string.IsNullOrWhiteSpace(ViewModel.SelectedProjectId))
            {
              await ViewModel.SaveMixerStateCommand.ExecuteAsync(null);
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void SendToggleButton_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        if (sender is ToggleButton toggle && toggle.Tag is string sendId)
        {
          var channel = GetChannelFromContext(toggle);
          if (channel != null)
          {
            if (channel.SendEnabled == null)
              channel.SendEnabled = new System.Collections.Generic.Dictionary<string, bool>();

            channel.SendEnabled[sendId] = toggle.IsChecked == true;

            // Auto-save if project is selected
            if (!string.IsNullOrWhiteSpace(ViewModel.SelectedProjectId))
            {
              await ViewModel.SaveMixerStateCommand.ExecuteAsync(null);
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void SendVolume_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
      try
      {
        if (sender is Slider slider && slider.DataContext is MixerSend send)
        {
          await ViewModel.UpdateSendCommand.ExecuteAsync(send);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void SendEnabled_Checked(object sender, RoutedEventArgs e)
    {
      try
      {
        if (sender is CheckBox checkBox && checkBox.DataContext is MixerSend send)
        {
          await ViewModel.UpdateSendCommand.ExecuteAsync(send);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void SendEnabled_Unchecked(object sender, RoutedEventArgs _)
    {
      try
      {
        if (sender is CheckBox checkBox && checkBox.DataContext is MixerSend send)
        {
          await ViewModel.UpdateSendCommand.ExecuteAsync(send);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void ReturnVolume_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
      try
      {
        if (sender is Slider slider && slider.DataContext is MixerReturn returnBus)
        {
          await ViewModel.UpdateReturnCommand.ExecuteAsync(returnBus);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void ReturnPan_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
      try
      {
        if (sender is Slider slider && slider.DataContext is MixerReturn returnBus)
        {
          await ViewModel.UpdateReturnCommand.ExecuteAsync(returnBus);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void ReturnEnabled_Checked(object sender, RoutedEventArgs e)
    {
      try
      {
        if (sender is CheckBox checkBox && checkBox.DataContext is MixerReturn returnBus)
        {
          await ViewModel.UpdateReturnCommand.ExecuteAsync(returnBus);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void ReturnEnabled_Unchecked(object sender, RoutedEventArgs e)
    {
      try
      {
        if (sender is CheckBox checkBox && checkBox.DataContext is MixerReturn returnBus)
        {
          await ViewModel.UpdateReturnCommand.ExecuteAsync(returnBus);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void SubGroupVolume_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
      try
      {
        if (sender is Slider slider && slider.DataContext is MixerSubGroup subGroup)
        {
          await ViewModel.UpdateSubGroupCommand.ExecuteAsync(subGroup);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void SubGroupPan_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
      try
      {
        if (sender is Slider slider && slider.DataContext is MixerSubGroup subGroup)
        {
          await ViewModel.UpdateSubGroupCommand.ExecuteAsync(subGroup);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void SubGroupMuted_Checked(object sender, RoutedEventArgs e)
    {
      try
      {
        if (sender is CheckBox checkBox && checkBox.DataContext is MixerSubGroup subGroup)
        {
          await ViewModel.UpdateSubGroupCommand.ExecuteAsync(subGroup);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void SubGroupMuted_Unchecked(object sender, RoutedEventArgs e)
    {
      try
      {
        if (sender is CheckBox checkBox && checkBox.DataContext is MixerSubGroup subGroup)
        {
          await ViewModel.UpdateSubGroupCommand.ExecuteAsync(subGroup);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void SubGroupSoloed_Checked(object sender, RoutedEventArgs e)
    {
      try
      {
        if (sender is CheckBox checkBox && checkBox.DataContext is MixerSubGroup subGroup)
        {
          await ViewModel.UpdateSubGroupCommand.ExecuteAsync(subGroup);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void SubGroupSoloed_Unchecked(object sender, RoutedEventArgs e)
    {
      try
      {
        if (sender is CheckBox checkBox && checkBox.DataContext is MixerSubGroup subGroup)
        {
          await ViewModel.UpdateSubGroupCommand.ExecuteAsync(subGroup);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private MixerChannel? GetChannelFromContext(DependencyObject element)
    {
      var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
      while (parent != null)
      {
        if (parent is FrameworkElement fe && fe.DataContext is MixerChannel channel)
        {
          return channel;
        }
        parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
      }
      return null;
    }

    private async void AddEffectComboBox_SelectionChanged(object sender, SelectionChangedEventArgsAlias e)
    {
      try
      {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is string effectType)
        {
          if (ViewModel.AddEffectCommand.CanExecute(effectType))
          {
            await ViewModel.AddEffectCommand.ExecuteAsync(effectType);
          }
          // Reset selection to allow re-adding the same effect type
          comboBox.SelectedIndex = -1;
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      // Set up help content
      HelpOverlay.Title = "Effects Mixer Help";
      HelpOverlay.HelpText = "The Effects Mixer allows you to manage audio channels, apply effects, and route audio through sends, returns, and sub-groups. Each channel has volume, pan, mute, and solo controls. You can create effect chains and apply them to channels. Mixer routing lets you organize channels into sub-groups and route audio through send/return buses for parallel processing.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+M", Description = "Mute master bus" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "M", Description = "Mute selected channel" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "S", Description = "Solo selected channel" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Up", Description = "Move effect up in chain" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Down", Description = "Move effect down in chain" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Remove effect from chain" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Space", Description = "Toggle effect enable/disable" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Use sends to route audio to effects processors without affecting the dry signal");
      HelpOverlay.Tips.Add("Returns are used to bring processed audio back into the mix");
      HelpOverlay.Tips.Add("Sub-groups help organize multiple channels together for easier control");
      HelpOverlay.Tips.Add("Effect chains can be saved as presets and reused across channels");
      HelpOverlay.Tips.Add("VU meters show real-time audio levels - keep them in the green/yellow range");

      HelpOverlay.Visibility = Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Channel_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is Border border && border.DataContext is MixerChannel channel)
      {
        e.Handled = true;
        if (_contextMenuService != null)
        {
          var menu = _contextMenuService.CreateContextMenu("track", channel);
          WireUpChannelMenuCommands(menu, channel);
          var position = e.GetPosition(border);
          _contextMenuService.ShowContextMenu(menu, border, position);
        }
      }
    }

    private void Effect_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is Border border && border.DataContext is Effect effect)
      {
        e.Handled = true;
        if (_contextMenuService != null)
        {
          var menu = _contextMenuService.CreateContextMenu("effect", effect);
          WireUpEffectMenuCommands(menu, effect);
          var position = e.GetPosition(border);
          _contextMenuService.ShowContextMenu(menu, border, position);
        }
      }
    }

    private void EffectChain_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var chain = element.DataContext as EffectChain ?? listView.SelectedItem as EffectChain;
        if (chain != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = _contextMenuService.CreateContextMenu("default", chain);
            WireUpEffectChainMenuCommands(menu, chain);
            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private void WireUpChannelMenuCommands(MenuFlyout menu, MixerChannel channel)
    {
      foreach (var item in menu.Items)
      {
        if (item is MenuFlyoutItem menuItem)
        {
          menuItem.Click += (s, e) => HandleChannelMenuClick(menuItem.Text, channel);
        }
      }
    }

    private void WireUpEffectMenuCommands(MenuFlyout menu, Effect effect)
    {
      foreach (var item in menu.Items)
      {
        if (item is MenuFlyoutItem menuItem)
        {
          menuItem.Click += (s, e) => HandleEffectMenuClick(menuItem.Text, effect);
        }
      }
    }

    private void WireUpEffectChainMenuCommands(MenuFlyout menu, EffectChain chain)
    {
      menu.Items.Clear();

      var applyItem = new MenuFlyoutItem { Text = "Apply to Channels" };
      applyItem.Click += async (s, e) => await HandleEffectChainMenuClick("Apply", chain);
      menu.Items.Add(applyItem);

      menu.Items.Add(new MenuFlyoutSeparator());

      var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
      duplicateItem.Click += async (s, e) => await HandleEffectChainMenuClick("Duplicate", chain);
      menu.Items.Add(duplicateItem);

      var renameItem = new MenuFlyoutItem { Text = "Rename" };
      renameItem.Click += async (s, e) => await HandleEffectChainMenuClick("Rename", chain);
      menu.Items.Add(renameItem);

      menu.Items.Add(new MenuFlyoutSeparator());

      var deleteItem = new MenuFlyoutItem { Text = "Delete" };
      deleteItem.Click += async (s, e) => await HandleEffectChainMenuClick("Delete", chain);
      menu.Items.Add(deleteItem);
    }

    private async void HandleChannelMenuClick(string action, MixerChannel channel)
    {
      try
      {
        switch (action.ToLower())
        {
          case "rename":
            await RenameChannelAsync(channel);
            break;
          case "duplicate":
            await DuplicateChannelAsync(channel);
            break;
          case "delete":
            await DeleteChannelAsync(channel);
            break;
          case "add effect":
            // Note: Effect picker dialog will be implemented in a future update
            _toastService?.ShowToast(ToastType.Info, "Add Effect", "Effect picker is planned for a future release. Add effects manually using the effect chain editor.");
            break;
          case "reset":
            await ResetChannelAsync(channel);
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async void HandleEffectMenuClick(string action, Effect effect)
    {
      try
      {
        switch (action.ToLower())
        {
          case "move up":
            if (ViewModel.MoveEffectUpCommand.CanExecute(effect.Id))
            {
              await ViewModel.MoveEffectUpCommand.ExecuteAsync(effect.Id);
              _toastService?.ShowToast(ToastType.Success, "Moved", $"Moved {effect.Name} up");
            }
            break;
          case "move down":
            if (ViewModel.MoveEffectDownCommand.CanExecute(effect.Id))
            {
              await ViewModel.MoveEffectDownCommand.ExecuteAsync(effect.Id);
              _toastService?.ShowToast(ToastType.Success, "Moved", $"Moved {effect.Name} down");
            }
            break;
          case "remove":
          case "delete":
            if (ViewModel.RemoveEffectCommand.CanExecute(effect.Id))
            {
              // Store for undo
              var chain = ViewModel.SelectedEffectChain;
              var effectIndex = chain?.Effects.IndexOf(effect) ?? -1;

              await ViewModel.RemoveEffectCommand.ExecuteAsync(effect.Id);

              // Register undo action
              if (_undoRedoService != null && chain != null && effectIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    $"Remove Effect: {effect.Name}",
                    () =>
                    {
                      // Undo: Re-add effect at original position
                      chain.Effects.Insert(effectIndex, effect);
                      _toastService?.ShowToast(ToastType.Info, "Undo", $"Restored {effect.Name}");
                    },
                    () =>
                    {
                      // Redo: Remove effect again
                      chain.Effects.Remove(effect);
                      _toastService?.ShowToast(ToastType.Info, "Redo", $"Removed {effect.Name}");
                    });
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Removed", $"Removed {effect.Name}");
            }
            break;
          case "duplicate":
            await DuplicateEffectAsync(effect);
            break;
          case "properties":
            ShowEffectProperties(effect);
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async Task HandleEffectChainMenuClick(string action, EffectChain chain)
    {
      try
      {
        switch (action.ToLower())
        {
          case "apply":
            if (ViewModel.ApplyEffectChainCommand.CanExecute(chain.Id))
            {
              await ViewModel.ApplyEffectChainCommand.ExecuteAsync(chain.Id);
              _toastService?.ShowToast(ToastType.Success, "Applied", $"Applied effect chain '{chain.Name}' to channels");
            }
            break;
          case "duplicate":
            await DuplicateEffectChainAsync(chain);
            break;
          case "rename":
            await RenameEffectChainAsync(chain);
            break;
          case "delete":
            if (ViewModel.DeleteEffectChainCommand.CanExecute(chain.Id))
            {
              // Store for undo
              var chainToDelete = chain;
              var chainIndex = ViewModel.EffectChains.IndexOf(chain);

              await ViewModel.DeleteEffectChainCommand.ExecuteAsync(chain.Id);

              // Register undo action
              if (_undoRedoService != null && chainIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    $"Delete Effect Chain: {chain.Name}",
                    () =>
                    {
                      // Undo: Re-add chain at original position
                      ViewModel.EffectChains.Insert(chainIndex, chainToDelete);
                      ViewModel.SelectedEffectChain = chainToDelete;
                      _toastService?.ShowToast(ToastType.Info, "Undo", $"Restored effect chain '{chain.Name}'");
                    },
                    () =>
                    {
                      // Redo: Remove chain again
                      ViewModel.EffectChains.Remove(chainToDelete);
                      if (ViewModel.SelectedEffectChain?.Id == chainToDelete.Id)
                      {
                        ViewModel.SelectedEffectChain = null;
                      }
                      _toastService?.ShowToast(ToastType.Info, "Redo", $"Deleted effect chain '{chain.Name}'");
                    });
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", $"Deleted effect chain '{chain.Name}'");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async Task RenameChannelAsync(MixerChannel channel)
    {
      var textBox = new TextBox
      {
        Text = channel.Name,
        PlaceholderText = "Enter channel name",
        Margin = new Microsoft.UI.Xaml.Thickness(0, 12, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Stretch
      };

      var dialog = new ContentDialog
      {
        Title = "Rename Channel",
        Content = textBox,
        PrimaryButtonText = "Rename",
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = this.XamlRoot
      };

      textBox.Loaded += (s, e) =>
      {
        textBox.SelectAll();
        textBox.Focus(FocusState.Programmatic);
      };

      var result = await dialog.ShowAsync();
      if (result == ContentDialogResult.Primary)
      {
        var newName = textBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(newName) && newName != channel.Name)
        {
          var oldName = channel.Name;
          channel.Name = newName;

          // Register undo action
          if (_undoRedoService != null)
          {
            var actionObj = new SimpleAction(
                $"Rename Channel: {oldName} → {newName}",
                () => channel.Name = oldName,
                () => channel.Name = newName);
            _undoRedoService.RegisterAction(actionObj);
          }

          _toastService?.ShowToast(ToastType.Success, "Renamed", $"Channel renamed to '{newName}'");
        }
      }
    }

    private Task DuplicateChannelAsync(MixerChannel channel)
    {
      try
      {
        // Create a deep copy of the channel with a new ID
        var duplicatedChannel = new MixerChannel
        {
          Id = Guid.NewGuid().ToString(),
          Name = $"{channel.Name} Copy",
          ChannelNumber = ViewModel.Channels.Count + 1,
          Volume = channel.Volume,
          Pan = channel.Pan,
          IsMuted = channel.IsMuted,
          IsSoloed = false, // Don't duplicate solo state
          MainDestination = channel.MainDestination,
          SubGroupId = channel.SubGroupId,
          SendLevels = new Dictionary<string, double>(channel.SendLevels ?? new Dictionary<string, double>()),
          SendEnabled = new Dictionary<string, bool>(channel.SendEnabled ?? new Dictionary<string, bool>())
        };

        // Find the index to insert after the original channel
        var insertIndex = ViewModel.Channels.IndexOf(channel) + 1;
        if (insertIndex < ViewModel.Channels.Count)
        {
          ViewModel.Channels.Insert(insertIndex, duplicatedChannel);
        }
        else
        {
          ViewModel.Channels.Add(duplicatedChannel);
        }

        // Register undo action
        if (_undoRedoService != null)
        {
          var actionObj = new SimpleAction(
              $"Duplicate Channel: {channel.Name}",
              () =>
              {
                ViewModel.Channels.Remove(duplicatedChannel);
                _toastService?.ShowToast(ToastType.Info, "Undo", $"Removed duplicated channel '{duplicatedChannel.Name}'");
              },
              () =>
              {
                if (insertIndex < ViewModel.Channels.Count)
                {
                  ViewModel.Channels.Insert(insertIndex, duplicatedChannel);
                }
                else
                {
                  ViewModel.Channels.Add(duplicatedChannel);
                }
                _toastService?.ShowToast(ToastType.Info, "Redo", $"Restored duplicated channel '{duplicatedChannel.Name}'");
              });
          _undoRedoService.RegisterAction(actionObj);
        }

        _toastService?.ShowToast(ToastType.Success, "Duplicated", $"Duplicated channel '{channel.Name}'");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to duplicate channel: {ex.Message}");
      }

      return Task.CompletedTask;
    }

    private async Task DeleteChannelAsync(MixerChannel channel)
    {
      var dialog = new ContentDialog
      {
        Title = "Delete Channel",
        Content = $"Are you sure you want to delete channel '{channel.Name}'? This action cannot be undone.",
        PrimaryButtonText = "Delete",
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Close,
        XamlRoot = this.XamlRoot
      };

      var result = await dialog.ShowAsync();
      if (result == ContentDialogResult.Primary)
      {
        var channelToDelete = channel;
        var channelIndex = ViewModel.Channels.IndexOf(channel);

        ViewModel.Channels.Remove(channel);

        // Register undo action
        if (_undoRedoService != null && channelIndex >= 0)
        {
          var actionObj = new SimpleAction(
              $"Delete Channel: {channel.Name}",
              () => ViewModel.Channels.Insert(channelIndex, channelToDelete),
              () => ViewModel.Channels.Remove(channelToDelete));
          _undoRedoService.RegisterAction(actionObj);
        }

        _toastService?.ShowToast(ToastType.Success, "Deleted", $"Deleted channel '{channel.Name}'");
      }
    }

    private Task ResetChannelAsync(MixerChannel channel)
    {
      try
      {
        var oldVolume = channel.Volume;
        var oldPan = channel.Pan;
        var oldMuted = channel.IsMuted;
        var oldSoloed = channel.IsSoloed;

        channel.Volume = 0.0;
        channel.Pan = 0.0;
        channel.IsMuted = false;
        channel.IsSoloed = false;

        // Register undo action
        if (_undoRedoService != null)
        {
          var actionObj = new SimpleAction(
              $"Reset Channel: {channel.Name}",
              () =>
              {
                channel.Volume = oldVolume;
                channel.Pan = oldPan;
                channel.IsMuted = oldMuted;
                channel.IsSoloed = oldSoloed;
              },
              () =>
              {
                channel.Volume = 0.0;
                channel.Pan = 0.0;
                channel.IsMuted = false;
                channel.IsSoloed = false;
              });
          _undoRedoService.RegisterAction(actionObj);
        }

        _toastService?.ShowToast(ToastType.Success, "Reset", $"Reset channel '{channel.Name}' to defaults");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to reset channel: {ex.Message}");
      }

      return Task.CompletedTask;
    }

    private Task DuplicateEffectAsync(Effect effect)
    {
      try
      {
        if (ViewModel.SelectedEffectChain == null)
        {
          _toastService?.ShowToast(ToastType.Warning, "No Chain", "Select an effect chain first");
          return Task.CompletedTask;
        }

        var duplicatedEffect = new Effect
        {
          Id = Guid.NewGuid().ToString(),
          Name = $"{effect.Name} Copy",
          Type = effect.Type,
          Enabled = effect.Enabled,
          Order = ViewModel.SelectedEffectChain.Effects.Count,
          Parameters = new List<EffectParameter>(effect.Parameters.Select(p => new EffectParameter
          {
            Name = p.Name,
            Value = p.Value,
            MinValue = p.MinValue,
            MaxValue = p.MaxValue,
            Unit = p.Unit
          }))
        };

        ViewModel.SelectedEffectChain.Effects.Add(duplicatedEffect);

        // Register undo action
        if (_undoRedoService != null)
        {
          var actionObj = new SimpleAction(
              $"Duplicate Effect: {effect.Name}",
              () => ViewModel.SelectedEffectChain.Effects.Remove(duplicatedEffect),
              () => ViewModel.SelectedEffectChain.Effects.Add(duplicatedEffect));
          _undoRedoService.RegisterAction(actionObj);
        }

        _toastService?.ShowToast(ToastType.Success, "Duplicated", $"Duplicated effect '{effect.Name}'");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to duplicate effect: {ex.Message}");
      }

      return Task.CompletedTask;
    }

    private void ShowEffectProperties(Effect effect)
    {
      var dialog = new ContentDialog
      {
        Title = $"Effect Properties: {effect.Name}",
        Content = CreateEffectPropertiesContent(effect),
        CloseButtonText = "Close",
        XamlRoot = this.XamlRoot
      };

      _ = dialog.ShowAsync();
    }

    private UIElement CreateEffectPropertiesContent(Effect effect)
    {
      var stackPanel = new StackPanel { Spacing = 8 };

      var nameText = new TextBlock { Text = $"Name: {effect.Name}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
      stackPanel.Children.Add(nameText);

      var typeText = new TextBlock { Text = $"Type: {effect.Type}" };
      stackPanel.Children.Add(typeText);

      var enabledText = new TextBlock { Text = $"Enabled: {effect.Enabled}" };
      stackPanel.Children.Add(enabledText);

      var orderText = new TextBlock { Text = $"Order: {effect.Order}" };
      stackPanel.Children.Add(orderText);

      if (effect.Parameters?.Count > 0)
      {
        stackPanel.Children.Add(new TextBlock { Text = "Parameters:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0) });
        foreach (var param in effect.Parameters)
        {
          var unitText = !string.IsNullOrEmpty(param.Unit) ? $" {param.Unit}" : "";
          var paramText = new TextBlock { Text = $"  {param.Name}: {param.Value}{unitText} ({param.MinValue}-{param.MaxValue})", Margin = new Microsoft.UI.Xaml.Thickness(8, 0, 0, 0) };
          stackPanel.Children.Add(paramText);
        }
      }

      return new ScrollViewer
      {
        Content = stackPanel,
        MaxHeight = 400
      };
    }

    private Task DuplicateEffectChainAsync(EffectChain chain)
    {
      try
      {
        var duplicatedChain = new EffectChain
        {
          Id = Guid.NewGuid().ToString(),
          Name = $"{chain.Name} Copy",
          Description = chain.Description,
          ProjectId = chain.ProjectId,
          Effects = new List<Effect>(
                chain.Effects.Select(e => new Effect
                {
                  Id = Guid.NewGuid().ToString(),
                  Name = e.Name,
                  Type = e.Type,
                  Enabled = e.Enabled,
                  Order = e.Order,
                  Parameters = new List<EffectParameter>(e.Parameters.Select(p => new EffectParameter
                  {
                    Name = p.Name,
                    Value = p.Value,
                    MinValue = p.MinValue,
                    MaxValue = p.MaxValue,
                    Unit = p.Unit
                  }))
                }))
        };

        ViewModel.EffectChains.Add(duplicatedChain);

        // Register undo action
        if (_undoRedoService != null)
        {
          var actionObj = new SimpleAction(
              $"Duplicate Effect Chain: {chain.Name}",
              () => ViewModel.EffectChains.Remove(duplicatedChain),
              () => ViewModel.EffectChains.Add(duplicatedChain));
          _undoRedoService.RegisterAction(actionObj);
        }

        _toastService?.ShowToast(ToastType.Success, "Duplicated", $"Duplicated effect chain '{chain.Name}'");
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to duplicate effect chain: {ex.Message}");
      }

      return Task.CompletedTask;
    }

    private async Task RenameEffectChainAsync(EffectChain chain)
    {
      var textBox = new TextBox
      {
        Text = chain.Name,
        PlaceholderText = "Enter chain name",
        Margin = new Microsoft.UI.Xaml.Thickness(0, 12, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Stretch
      };

      var dialog = new ContentDialog
      {
        Title = "Rename Effect Chain",
        Content = textBox,
        PrimaryButtonText = "Rename",
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = this.XamlRoot
      };

      textBox.Loaded += (s, e) =>
      {
        textBox.SelectAll();
        textBox.Focus(FocusState.Programmatic);
      };

      var result = await dialog.ShowAsync();
      if (result == ContentDialogResult.Primary)
      {
        var newName = textBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(newName) && newName != chain.Name)
        {
          var oldName = chain.Name;
          chain.Name = newName;

          // Register undo action
          if (_undoRedoService != null)
          {
            var actionObj = new SimpleAction(
                $"Rename Effect Chain: {oldName} → {newName}",
                () => chain.Name = oldName,
                () => chain.Name = newName);
            _undoRedoService.RegisterAction(actionObj);
          }

          _toastService?.ShowToast(ToastType.Success, "Renamed", $"Effect chain renamed to '{newName}'");
        }
      }
    }
  }
}