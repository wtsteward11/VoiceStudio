using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Models;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the TagManagerView panel - Tag management.
  /// </summary>
  public partial class TagManagerViewModel : BaseViewModel, IPanelView, ILifecyclePanelView
  {
    private readonly ITagManagerClient _tagManagerClient;
    private readonly IDialogService _dialogService;
    private readonly UndoRedoService? _undoRedoService;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly MultiSelectService _multiSelectService;
    private MultiSelectState? _multiSelectState;
    private CancellationTokenSource? _searchDebounceCts;
    private const int SearchDebounceMs = 300;

    public string PanelId => PanelIds.TagManager;
    public string DisplayName => ResourceHelper.GetString("Panel.TagManager.DisplayName", "Tag Manager");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<TagItem> tags = new();

    [ObservableProperty]
    private TagItem? selectedTag;

    // Multi-select support
    [ObservableProperty]
    private int selectedTagCount;

    [ObservableProperty]
    private bool hasMultipleTagSelection;

    public bool IsTagSelected(string tagId) => _multiSelectState?.SelectedIds.Contains(tagId) ?? false;

    [ObservableProperty]
    private string? searchQuery;

    [ObservableProperty]
    private string? selectedCategory;

    [ObservableProperty]
    private ObservableCollection<string> availableCategories = new();

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private string? editingName;

    [ObservableProperty]
    private string? editingCategory;

    [ObservableProperty]
    private string? editingColor;

    [ObservableProperty]
    private string? editingDescription;

    public TagManagerViewModel(IViewModelContext context, ITagManagerClient tagManagerClient, IDialogService dialogService)
        : base(context)
    {
      _tagManagerClient = tagManagerClient ?? throw new ArgumentNullException(nameof(tagManagerClient));
      _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

      // Get undo/redo service (may be null if not initialized)
      try
      {
        _undoRedoService = AppServices.TryGetUndoRedoService();
      }
      catch
      {
        // Service may not be initialized yet - that's okay
        _undoRedoService = null;
      }

      // Get toast notification service (may be null if not initialized)
      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
      }
      catch
      {
        // Service may not be initialized yet - that's okay
        _toastNotificationService = null;
      }

      // Get multi-select service
      var multiSelectService = AppServices.TryGetMultiSelectService();
      _multiSelectService = multiSelectService ?? throw new InvalidOperationException("MultiSelectService is required but not registered");
      _multiSelectState = _multiSelectService.GetState(PanelId);

      LoadTagsCommand = new AsyncRelayCommand(LoadTagsAsync);
      SearchTagsCommand = new AsyncRelayCommand(SearchTagsAsync);
      CreateTagCommand = new AsyncRelayCommand(CreateTagAsync);
      UpdateTagCommand = new AsyncRelayCommand<TagItem>(UpdateTagAsync);
      DeleteTagCommand = new AsyncRelayCommand<TagItem>(DeleteTagAsync);
      StartEditCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<TagItem>(StartEdit);
      CancelEditCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(CancelEdit);
      SaveEditCommand = new AsyncRelayCommand(SaveEditAsync);
      LoadCategoriesCommand = new AsyncRelayCommand(LoadCategoriesAsync);
      MergeTagsCommand = new AsyncRelayCommand<TagItem>(MergeTagsAsync);

      // Multi-select commands
      SelectAllTagsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(SelectAllTags, () => Tags?.Count > 0);
      ClearTagSelectionCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ClearTagSelection);
      DeleteSelectedTagsCommand = new AsyncRelayCommand(DeleteSelectedTagsAsync, () => SelectedTagCount > 0 && !IsLoading);

      // Subscribe to selection changes
      _multiSelectService.SelectionChanged += (_, e) =>
      {
        if (e.PanelId == PanelId)
        {
          UpdateTagSelectionProperties();
          OnPropertyChanged(nameof(SelectedTagCount));
          OnPropertyChanged(nameof(HasMultipleTagSelection));
        }
      };

      // No constructor fire-and-forget — load from View Loaded via OnActivatedAsync (RETAINED_ASYNC_RULE)
    }

    /// <inheritdoc />
    public async Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      await LoadCategoriesAsync(cancellationToken);
      await LoadTagsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default) => LoadTagsAsync(cancellationToken);

    public IAsyncRelayCommand LoadTagsCommand { get; }
    public IAsyncRelayCommand SearchTagsCommand { get; }
    public IAsyncRelayCommand CreateTagCommand { get; }
    public IAsyncRelayCommand<TagItem> UpdateTagCommand { get; }
    public IAsyncRelayCommand<TagItem> DeleteTagCommand { get; }
    public IRelayCommand<TagItem> StartEditCommand { get; }
    public IRelayCommand CancelEditCommand { get; }
    public IAsyncRelayCommand SaveEditCommand { get; }
    public IAsyncRelayCommand LoadCategoriesCommand { get; }
    public IAsyncRelayCommand<TagItem> MergeTagsCommand { get; }

    // Multi-select commands
    public IRelayCommand SelectAllTagsCommand { get; }
    public IRelayCommand ClearTagSelectionCommand { get; }
    public IAsyncRelayCommand DeleteSelectedTagsCommand { get; }

    private async Task LoadTagsAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var tags = await _tagManagerClient.GetTagsAsync(SelectedCategory, SearchQuery, cancellationToken);

        Tags.Clear();
        if (tags != null)
        {
          foreach (var tag in tags)
          {
            Tags.Add(new TagItem(tag));
          }
        }

        if (Tags.Count > 0)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("TagManager.TagsLoaded", Tags.Count),
              ResourceHelper.GetString("Toast.Title.TagsLoaded", "Tags Loaded"));
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TagManager.LoadTagsFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.LoadTagsFailed", "Failed to Load Tags"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task SearchTagsAsync(CancellationToken cancellationToken)
    {
      await LoadTagsAsync(cancellationToken);
    }

    private async Task CreateTagAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var name = ResourceHelper.GetString("TagManager.NewTag", "New Tag");
        var created = await _tagManagerClient.CreateTagAsync(name, null, null, null, cancellationToken);

        if (created != null)
        {
          var tagItem = new TagItem(created);
          Tags.Add(tagItem);
          SelectedTag = tagItem;

          // Register undo action
          if (_undoRedoService != null)
          {
            var action = new CreateTagAction(
                Tags,
                _tagManagerClient,
                tagItem,
                onUndo: (t) =>
                {
                  if (SelectedTag?.Id == t.Id)
                  {
                    SelectedTag = Tags.FirstOrDefault();
                    IsEditing = false;
                  }
                },
                onRedo: (t) => SelectedTag = t);
            _undoRedoService.RegisterAction(action);
          }

          StartEdit(SelectedTag);
          StatusMessage = ResourceHelper.GetString("TagManager.TagCreated", "Tag created");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("TagManager.TagCreatedSuccess", created.Name),
              ResourceHelper.GetString("Toast.Title.TagCreated", "Tag Created"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CreateTag");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateTagAsync(TagItem? tag, CancellationToken cancellationToken)
    {
      if (tag == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var updated = await _tagManagerClient.UpdateTagAsync(tag.Id, tag.Name ?? string.Empty, tag.Category, tag.Color, tag.Description, cancellationToken);

        if (updated != null)
        {
          tag.UpdateFrom(updated);
        }

        await LoadTagsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("TagManager.TagUpdated", "Tag updated");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("TagManager.TagUpdatedSuccess", tag.Name),
            ResourceHelper.GetString("Toast.Title.TagUpdated", "Tag Updated"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "UpdateTag");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteTagAsync(TagItem? tag, CancellationToken cancellationToken)
    {
      if (tag == null)
        return;

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        await _tagManagerClient.DeleteTagAsync(tag.Id, cancellationToken);

        var tagToDelete = tag;
        var originalIndex = Tags.IndexOf(tag);
        if (SelectedTag == tag)
        {
          SelectedTag = null;
          IsEditing = false;
        }

        // Register undo action before reloading
        if (_undoRedoService != null)
        {
          var action = new DeleteTagAction(
              Tags,
              _tagManagerClient,
              tagToDelete,
              originalIndex,
              onUndo: (t) => SelectedTag = t,
              onRedo: (t) =>
              {
                if (SelectedTag?.Id == t.Id)
                {
                  SelectedTag = null;
                  IsEditing = false;
                }
              });
          _undoRedoService.RegisterAction(action);
        }

        await LoadTagsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("TagManager.TagDeleted", "Tag deleted");
        var tagName = tagToDelete?.Name ?? ResourceHelper.GetString("TagManager.UnknownTag", "Unknown Tag");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("TagManager.TagDeletedDetail", tagName),
            ResourceHelper.GetString("Toast.Title.TagDeleted", "Tag Deleted"));
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TagManager.DeleteTagFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("TagManager.DeleteTagFailed", ex.Message),
            ResourceHelper.GetString("Toast.Title.DeleteTagFailed", "Failed to Delete Tag"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void StartEdit(TagItem? tag)
    {
      if (tag == null)
        return;

      SelectedTag = tag;
      EditingName = tag.Name;
      EditingCategory = tag.Category;
      EditingColor = tag.Color;
      EditingDescription = tag.Description;
      IsEditing = true;
    }

    private void CancelEdit()
    {
      IsEditing = false;
      EditingName = null;
      EditingCategory = null;
      EditingColor = null;
      EditingDescription = null;
      SelectedTag = null;
    }

    private async Task SaveEditAsync(CancellationToken cancellationToken)
    {
      if (SelectedTag == null || string.IsNullOrEmpty(EditingName))
        return;

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var updated = await _tagManagerClient.UpdateTagAsync(SelectedTag.Id, EditingName, EditingCategory, EditingColor, EditingDescription, cancellationToken);

        if (updated != null)
        {
          SelectedTag.UpdateFrom(updated);
        }

        CancelEdit();
        await LoadTagsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("TagManager.TagUpdated", "Tag updated");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("TagManager.TagSavedSuccess", SelectedTag?.Name ?? ResourceHelper.GetString("TagManager.Unknown", "Unknown")),
            ResourceHelper.GetString("Toast.Title.TagSaved", "Tag Saved"));
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TagManager.SaveTagFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.SaveTagFailed", "Failed to Save Tag"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task MergeTagsAsync(TagItem? sourceTag, CancellationToken cancellationToken)
    {
      if (sourceTag == null || SelectedTag == null || sourceTag.Id == SelectedTag.Id)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _tagManagerClient.MergeTagsAsync(sourceTag.Id, SelectedTag.Id, cancellationToken);

        await LoadTagsAsync(cancellationToken);
        StatusMessage = ResourceHelper.FormatString("TagManager.TagsMerged", SelectedTag.Name);
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("TagManager.TagsMergedDetail", SelectedTag.Name),
            ResourceHelper.GetString("Toast.Title.TagsMerged", "Tags Merged"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "MergeTags");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var response = await _tagManagerClient.GetCategoriesAsync(cancellationToken);

        AvailableCategories.Clear();
        if (response?.Categories != null)
        {
          foreach (var category in response.Categories)
          {
            AvailableCategories.Add(category);
          }
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TagManager.LoadCategoriesFailed", ex.Message);
      }
    }

    partial void OnSelectedCategoryChanged(string? value)
    {
      _ = LoadTagsAsync(CancellationToken.None);
    }

    partial void OnSearchQueryChanged(string? value)
    {
      _searchDebounceCts?.Cancel();
      _searchDebounceCts = new CancellationTokenSource();
      var cts = _searchDebounceCts;
      _ = Task.Run(async () =>
      {
        try
        {
          await Task.Delay(SearchDebounceMs, cts.Token);
          Dispatcher.TryEnqueue(() => _ = SearchTagsAsync(cts.Token));
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "TagManagerViewModel.OnSearchQueryChanged");
      }
      });
    }

    public void ToggleTagSelection(string tagId, bool isCtrlPressed, bool isShiftPressed)
    {
      if (_multiSelectState == null)
        return;

      if (isShiftPressed && !string.IsNullOrEmpty(_multiSelectState.RangeAnchorId))
      {
        // Range selection
        var allIds = Tags.Select(t => t.Id).ToList();
        _multiSelectState.SetRange(_multiSelectState.RangeAnchorId, tagId, allIds);
      }
      else if (isCtrlPressed)
      {
        // Toggle selection
        _multiSelectState.Toggle(tagId);
        if (!_multiSelectState.SelectedIds.Contains(tagId))
        {
          _multiSelectState.RangeAnchorId = null;
        }
        else if (_multiSelectState.RangeAnchorId == null)
        {
          _multiSelectState.RangeAnchorId = tagId;
        }
      }
      else
      {
        // Single selection
        _multiSelectState.SetSingle(tagId);
      }

      UpdateTagSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void SelectAllTags()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      foreach (var tag in Tags)
      {
        _multiSelectState.Add(tag.Id);
      }
      UpdateTagSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void ClearTagSelection()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      UpdateTagSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      DeleteSelectedTagsCommand.NotifyCanExecuteChanged();
    }

    private async Task DeleteSelectedTagsAsync(CancellationToken cancellationToken)
    {
      if (_multiSelectState == null || _multiSelectState.SelectedIds.Count == 0)
        return;

      var selectedIds = new System.Collections.Generic.List<string>(_multiSelectState.SelectedIds);

      // Show confirmation dialog (Panel Hardening: IDialogService per PANEL_HARDENING_PATTERN)
      var confirmed = await _dialogService.ShowConfirmationAsync(
          "Delete tags?",
          $"Are you sure you want to delete '{selectedIds.Count} tag(s)'? This action cannot be undone.",
          "Delete",
          "Cancel");

      if (!confirmed)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var tagsToDelete = new System.Collections.Generic.List<TagItem>();
        int deletedCount = 0;

        foreach (var tagId in selectedIds)
        {
          cancellationToken.ThrowIfCancellationRequested();

          try
          {
            await _tagManagerClient.DeleteTagAsync(tagId,
                cancellationToken
            );

            var tag = Tags.FirstOrDefault(t => t.Id == tagId);
            if (tag != null)
            {
              tagsToDelete.Add(tag);
              Tags.Remove(tag);
              if (SelectedTag?.Id == tagId)
              {
                SelectedTag = null;
              }
              deletedCount++;
            }
          }
          catch (OperationCanceledException)
          {
            throw; // Re-throw cancellation to abort batch deletion
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "TagManagerViewModel.DeleteSelectedTagsAsync");
      }
        }

        // Clear selection after deletion
        ClearTagSelection();

        // Show success toast
        if (deletedCount > 0)
        {
          StatusMessage = ResourceHelper.FormatString("TagManager.TagsDeleted", deletedCount);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("TagManager.TagsDeletedDetail", deletedCount),
              ResourceHelper.GetString("Toast.Title.BatchDeleteComplete", "Batch Delete Complete"));
        }
        if (deletedCount < selectedIds.Count)
        {
          _toastNotificationService?.ShowWarning(
              ResourceHelper.FormatString("TagManager.PartialDeleteWarning", deletedCount, selectedIds.Count),
              ResourceHelper.GetString("Toast.Title.PartialDelete", "Partial Delete"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteSelectedTags");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void UpdateTagSelectionProperties()
    {
      if (_multiSelectState == null)
      {
        SelectedTagCount = 0;
        HasMultipleTagSelection = false;
      }
      else
      {
        SelectedTagCount = _multiSelectState.Count;
        HasMultipleTagSelection = _multiSelectState.Count > 1;
      }
      DeleteSelectedTagsCommand.NotifyCanExecuteChanged();
    }

    // Response models (public for ITagManagerClient)
    public class TagCategoriesResponse
    {
      public string[] Categories { get; set; } = Array.Empty<string>();
    }
  }

  // Data models
  public class Tag
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Color { get; set; }
    public string? Description { get; set; }
    public int UsageCount { get; set; }
    public string Created { get; set; } = string.Empty;
    public string Modified { get; set; } = string.Empty;
  }

  public class TagItem : ObservableObject
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public string? Category { get; set; }
    public string? Color { get; set; }
    public string? Description { get; set; }
    public int UsageCount { get; set; }

    public TagItem(Tag tag)
    {
      Id = tag.Id;
      Name = tag.Name;
      Category = tag.Category;
      Color = tag.Color;
      Description = tag.Description;
      UsageCount = tag.UsageCount;
    }

    public void UpdateFrom(Tag tag)
    {
      Name = tag.Name;
      Category = tag.Category;
      Color = tag.Color;
      Description = tag.Description;
      UsageCount = tag.UsageCount;
      OnPropertyChanged(nameof(Name));
      OnPropertyChanged(nameof(Category));
      OnPropertyChanged(nameof(Color));
      OnPropertyChanged(nameof(Description));
      OnPropertyChanged(nameof(UsageCount));
    }
  }
}