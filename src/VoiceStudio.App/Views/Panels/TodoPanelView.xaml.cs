using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// TodoPanelView panel for todo/task management.
  /// </summary>
  public sealed partial class TodoPanelView : UserControl
  {
    public TodoPanelViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public TodoPanelView()
    {
      this.InitializeComponent();
      ViewModel = new TodoPanelViewModel(
          AppServices.GetViewModelContext(),
          AppServices.GetTodoPanelClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(TodoPanelViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Todo Panel Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(TodoPanelViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Todo Panel", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += TodoPanelView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void TodoPanelView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Todo Panel Help";
      HelpOverlay.HelpText = "The Todo Panel allows you to manage tasks and todos for your voice cloning projects. Create todos with titles, descriptions, priorities (low, medium, high, urgent), categories, tags, and due dates. Filter todos by status, priority, category, or tag to find what you need. Update todo status as you work on tasks, and mark them as completed when done. The summary cards show statistics about your todos including total count, status breakdown, and priority distribution. Use categories and tags to organize your todos for better project management.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Create new todo" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+U", Description = "Update selected todo" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected todo" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh todos" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Use priorities to organize important tasks (urgent > high > medium > low)");
      HelpOverlay.Tips.Add("Categories help group related todos together");
      HelpOverlay.Tips.Add("Tags allow multiple labels per todo for flexible organization");
      HelpOverlay.Tips.Add("Set due dates to track deadlines");
      HelpOverlay.Tips.Add("Update status as you progress: pending → in_progress → completed");
      HelpOverlay.Tips.Add("Use filters to focus on specific subsets of todos");
      HelpOverlay.Tips.Add("Summary cards provide quick overview of your todo statistics");
      HelpOverlay.Tips.Add("Completed todos are automatically tracked with completion timestamps");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Todo_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var todo = element.DataContext ?? listView.SelectedItem;
        if (todo != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleTodoMenuClick("Edit", todo);
            menu.Items.Add(editItem);

            var completeItem = new MenuFlyoutItem { Text = "Mark Complete" };
            completeItem.Click += async (_, _) => await HandleTodoMenuClick("Complete", todo);
            menu.Items.Add(completeItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleTodoMenuClick("Duplicate", todo);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleTodoMenuClick("Delete", todo);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleTodoMenuClick(string action, object todo)
    {
      try
      {
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedTodo = (TodoItem)todo;
            _toastService?.ShowToast(ToastType.Info, "Edit Todo", "Todo selected for editing");
            break;
          case "complete":
            _toastService?.ShowToast(ToastType.Info, "Complete", "Marking todo as complete");
            break;
          case "duplicate":
            DuplicateTodo(todo);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Todo",
              Content = "Are you sure you want to delete this todo? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var todoToDelete = (TodoItem)todo;
              var todoIndex = ViewModel.Todos.IndexOf(todoToDelete);

              ViewModel.Todos.Remove(todoToDelete);

              // Register undo action
              if (_undoRedoService != null && todoIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Todo",
                    () => ViewModel.Todos.Insert(todoIndex, todoToDelete),
                    () => ViewModel.Todos.Remove(todoToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Todo deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private void DuplicateTodo(object todo)
    {
      try
      {
        var todoType = todo.GetType();
        var duplicatedTodo = Activator.CreateInstance(todoType);
        if (duplicatedTodo != null)
        {
          foreach (var prop in todoType.GetProperties())
          {
            if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
            {
              var value = prop.GetValue(todo);
              if (prop.Name == "Title")
              {
                prop.SetValue(duplicatedTodo, $"{value} (Copy)");
              }
              else
              {
                prop.SetValue(duplicatedTodo, value);
              }
            }
          }

          var index = ViewModel.Todos.IndexOf((TodoItem)todo);
          ViewModel.Todos.Insert(index + 1, (TodoItem)duplicatedTodo);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "Todo duplicated");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }
  }
}