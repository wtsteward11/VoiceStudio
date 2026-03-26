using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the MultiVoiceGeneratorView panel - Generate multiple voice synthesis jobs simultaneously.
  /// </summary>
  public partial class MultiVoiceGeneratorViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IMultiVoiceGeneratorClient _multiVoiceClient;

    public string PanelId => PanelIds.MultiVoiceGenerator;
    public string DisplayName => ResourceHelper.GetString("Panel.MultiVoiceGenerator.DisplayName", "Multi-Voice Generator");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<VoiceGenerationItem> generationQueue = new();

    [ObservableProperty]
    private VoiceGenerationItem? selectedQueueItem;

    [ObservableProperty]
    private string? newItemProfileId;

    [ObservableProperty]
    private string? newItemText;

    [ObservableProperty]
    private string? newItemEngine = "xtts";

    [ObservableProperty]
    private string? newItemQualityMode = "standard";

    [ObservableProperty]
    private string? newItemLanguage = "en";

    [ObservableProperty]
    private string? newItemEmotion;

    [ObservableProperty]
    private ObservableCollection<string> availableEngines = new();

    [ObservableProperty]
    private ObservableCollection<string> qualityModes = new() { "fast", "standard", "high", "ultra" };

    [ObservableProperty]
    private string? currentJobId;

    [ObservableProperty]
    private string? currentJobName;

    private CancellationTokenSource? _pollingCts;

    [ObservableProperty]
    private float jobProgress;

    [ObservableProperty]
    private string? jobStatus;

    [ObservableProperty]
    private ObservableCollection<VoiceGenerationResultItem> results = new();

    [ObservableProperty]
    private string resultsViewMode = "grid"; // grid, list, comparison

    [ObservableProperty]
    private ObservableCollection<string> selectedAudioIdsForComparison = new();

    public MultiVoiceGeneratorViewModel(IViewModelContext context, IMultiVoiceGeneratorClient multiVoiceClient)
        : base(context)
    {
      _multiVoiceClient = multiVoiceClient ?? throw new ArgumentNullException(nameof(multiVoiceClient));

      AddToQueueCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AddToQueue");
        await AddToQueueAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(NewItemProfileId) && !string.IsNullOrWhiteSpace(NewItemText));
      RemoveFromQueueCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RemoveFromQueue");
        await RemoveFromQueueAsync(ct);
      }, () => SelectedQueueItem != null);
      ClearQueueCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ClearQueue");
        await ClearQueueAsync(ct);
      }, () => GenerationQueue.Count > 0);
      ImportCSVCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ImportCSV");
        await ImportCSVAsync(ct);
      });
      ExportCSVCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ExportCSV");
        await ExportCSVAsync(ct);
      }, () => Results.Count > 0);
      StartGenerationCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("StartGeneration");
        await StartGenerationAsync(ct);
      }, () => GenerationQueue.Count > 0 && !string.IsNullOrWhiteSpace(CurrentJobName));
      LoadJobStatusCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadJobStatus");
        await LoadJobStatusAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(CurrentJobId));
      LoadResultsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadResults");
        await LoadResultsAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(CurrentJobId));
      CompareVoicesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CompareVoices");
        await CompareVoicesAsync(ct);
      }, () => SelectedAudioIdsForComparison.Count >= 2);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      });
    }

    Task IPanelLifecycle.OnActivatedAsync(CancellationToken ct)
    {
      _ = LoadEnginesAsync(ct);
      return Task.CompletedTask;
    }

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken ct) => Task.CompletedTask;

    async Task IPanelLifecycle.RefreshAsync(CancellationToken ct) => await RefreshAsync(ct);

    public IAsyncRelayCommand AddToQueueCommand { get; }
    public IAsyncRelayCommand RemoveFromQueueCommand { get; }
    public IAsyncRelayCommand ClearQueueCommand { get; }
    public IAsyncRelayCommand ImportCSVCommand { get; }
    public IAsyncRelayCommand ExportCSVCommand { get; }
    public IAsyncRelayCommand StartGenerationCommand { get; }
    public IAsyncRelayCommand LoadJobStatusCommand { get; }
    public IAsyncRelayCommand LoadResultsCommand { get; }
    public IAsyncRelayCommand CompareVoicesCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    partial void OnNewItemProfileIdChanged(string? value)
    {
      AddToQueueCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewItemTextChanged(string? value)
    {
      AddToQueueCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedQueueItemChanged(VoiceGenerationItem? value)
    {
      RemoveFromQueueCommand.NotifyCanExecuteChanged();
    }

    partial void OnGenerationQueueChanged(ObservableCollection<VoiceGenerationItem> value)
    {
      ClearQueueCommand.NotifyCanExecuteChanged();
      StartGenerationCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentJobNameChanged(string? value)
    {
      StartGenerationCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentJobIdChanged(string? value)
    {
      LoadJobStatusCommand.NotifyCanExecuteChanged();
      LoadResultsCommand.NotifyCanExecuteChanged();
    }

    partial void OnResultsChanged(ObservableCollection<VoiceGenerationResultItem> value)
    {
      ExportCSVCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedAudioIdsForComparisonChanged(ObservableCollection<string> value)
    {
      CompareVoicesCommand.NotifyCanExecuteChanged();
    }

    private Task AddToQueueAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();

      if (string.IsNullOrWhiteSpace(NewItemProfileId) || string.IsNullOrWhiteSpace(NewItemText))
      {
        return Task.CompletedTask;
      }

      if (GenerationQueue.Count >= 20)
      {
        ErrorMessage = ResourceHelper.GetString("MultiVoiceGenerator.MaxQueueItems", "Maximum 20 items allowed in queue");
        return Task.CompletedTask;
      }

      var item = new VoiceGenerationItem
      {
        ItemId = Guid.NewGuid().ToString(),
        ProfileId = NewItemProfileId,
        Text = NewItemText,
        Engine = NewItemEngine ?? "xtts",
        QualityMode = NewItemQualityMode ?? "standard",
        Language = NewItemLanguage ?? "en",
        Emotion = NewItemEmotion,
        Status = ResourceHelper.GetString("MultiVoiceGenerator.StatusPending", "pending")
      };

      GenerationQueue.Add(item);

      // Clear form
      NewItemProfileId = null;
      NewItemText = null;
      NewItemEngine = "xtts";
      NewItemQualityMode = "standard";
      NewItemLanguage = "en";
      NewItemEmotion = null;

      StatusMessage = ResourceHelper.GetString("MultiVoiceGenerator.ItemAddedToQueue", "Item added to queue");

      return Task.CompletedTask;
    }

    private Task RemoveFromQueueAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();

      if (SelectedQueueItem != null)
      {
        GenerationQueue.Remove(SelectedQueueItem);
        SelectedQueueItem = null;
        StatusMessage = ResourceHelper.GetString("MultiVoiceGenerator.ItemRemoved", "Item removed from queue");
      }

      return Task.CompletedTask;
    }

    private Task ClearQueueAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();

      GenerationQueue.Clear();
      SelectedQueueItem = null;
      StatusMessage = ResourceHelper.GetString("MultiVoiceGenerator.QueueCleared", "Queue cleared");

      return Task.CompletedTask;
    }

    private async Task ImportCSVAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var picker = new FileOpenPicker();
        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".csv");

        // WinUI 3 requires initializing the picker with the window handle
        var window = App.MainWindowInstance;
        if (window != null)
        {
          var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
          WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
          cancellationToken.ThrowIfCancellationRequested();

          // Read CSV content
          var csvContent = await FileIO.ReadTextAsync(file);

          var response = await _multiVoiceClient.ImportCSVAsync(csvContent, cancellationToken);

          if (response?.Items != null)
          {
            GenerationQueue.Clear();
            foreach (var itemData in response.Items)
            {
              var item = new VoiceGenerationItem
              {
                ItemId = Guid.NewGuid().ToString(),
                ProfileId = itemData.ProfileId,
                Text = itemData.Text,
                Engine = itemData.Engine,
                QualityMode = itemData.QualityMode,
                Language = itemData.Language,
                Emotion = itemData.Emotion,
                Status = ResourceHelper.GetString("MultiVoiceGenerator.StatusPending", "pending")
              };
              GenerationQueue.Add(item);
            }

            StatusMessage = ResourceHelper.FormatString("MultiVoiceGenerator.ImportedFromCSV", response.Count);
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("MultiVoiceGenerator.ImportCSVFailed", ex.Message);
        await HandleErrorAsync(ex, "ImportCSV");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ExportCSVAsync(CancellationToken cancellationToken)
    {
      if (Results.Count == 0)
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("CSV File", new[] { ".csv" });
        picker.SuggestedFileName = $"multi_voice_results_{DateTime.Now:yyyyMMdd_HHmmss}";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          cancellationToken.ThrowIfCancellationRequested();

          var response = await _multiVoiceClient.ExportCSVAsync(CurrentJobId ?? "", cancellationToken);

          if (response != null && !string.IsNullOrWhiteSpace(response.CsvContent))
          {
            await FileIO.WriteTextAsync(file, response.CsvContent);
            StatusMessage = ResourceHelper.FormatString("MultiVoiceGenerator.ExportedToCSV", Results.Count);
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("MultiVoiceGenerator.ExportCSVFailed", ex.Message);
        await HandleErrorAsync(ex, "ExportCSV");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task StartGenerationAsync(CancellationToken cancellationToken = default)
    {
      if (GenerationQueue.Count == 0 || string.IsNullOrWhiteSpace(CurrentJobName))
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // Convert queue items to request format
        var items = new List<Dictionary<string, object>>();
        foreach (var item in GenerationQueue)
        {
          var itemDict = new Dictionary<string, object>
                    {
                        { "profile_id", item.ProfileId },
                        { "text", item.Text },
                        { "engine", item.Engine },
                        { "quality_mode", item.QualityMode },
                        { "language", item.Language }
                    };
          if (!string.IsNullOrWhiteSpace(item.Emotion))
          {
            itemDict["emotion"] = item.Emotion;
          }
          items.Add(itemDict);
        }

        var request = new MultiVoiceGenerateRequest
        {
          Name = CurrentJobName!,
          Items = items
        };

        var response = await _multiVoiceClient.GenerateAsync(request, cancellationToken);

        if (response != null)
        {
          CurrentJobId = response.JobId;
          JobStatus = response.Status;
          StatusMessage = ResourceHelper.GetString("MultiVoiceGenerator.GenerationStarted", "Generation started");

          // Start polling for status (Phase 3: use CTS for cancellation on job complete/navigate)
          _pollingCts?.Cancel();
          _pollingCts?.Dispose();
          _pollingCts = new CancellationTokenSource();
          _ = PollJobStatusAsync(_pollingCts.Token);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("MultiVoiceGenerator.StartGenerationFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task PollJobStatusAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(CurrentJobId))
      {
        return;
      }

      try
      {
        while ((JobStatus == "processing" || JobStatus == "pending") && !cancellationToken.IsCancellationRequested)
        {
          await Task.Delay(1000, cancellationToken); // Poll every second

          var status = await _multiVoiceClient.GetJobStatusAsync(CurrentJobId!, cancellationToken);

          if (status != null)
          {
            JobProgress = status.Progress;
            JobStatus = status.Status;

            // Update queue items with status (match by index; backend preserves order)
            for (var i = 0; i < status.Items.Count && i < GenerationQueue.Count; i++)
            {
              var statusItem = status.Items[i];
              var queueItem = GenerationQueue[i];
              queueItem.Status = statusItem.Status;
              queueItem.Progress = statusItem.Progress;
              queueItem.AudioId = statusItem.AudioId;
              queueItem.AudioUrl = statusItem.AudioUrl;
              queueItem.QualityScore = statusItem.QualityScore;
            }

            if (status.Status == "completed")
            {
              StopPolling();
              await LoadResultsAsync(cancellationToken);
              StatusMessage = ResourceHelper.GetString("MultiVoiceGenerator.GenerationCompleted", "Generation completed");
              break;
            }
            else if (status.Status == "failed")
            {
              StopPolling();
              ErrorMessage = ResourceHelper.GetString("MultiVoiceGenerator.GenerationFailed", "Generation failed");
              break;
            }
          }
        }
      }
      catch (OperationCanceledException)
      {
        // Polling cancelled - expected behavior
        return;
      }
      catch (Exception ex)
      {
        StopPolling();
        ErrorMessage = ResourceHelper.FormatString("MultiVoiceGenerator.PollJobStatusFailed", ex.Message);
        await HandleErrorAsync(ex, "PollJobStatus");
      }
    }

    private void StopPolling()
    {
      _pollingCts?.Cancel();
      _pollingCts?.Dispose();
      _pollingCts = null;
    }

    private async Task LoadJobStatusAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(CurrentJobId))
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var status = await _multiVoiceClient.GetJobStatusAsync(CurrentJobId!, cancellationToken);

        if (status != null)
        {
          JobProgress = status.Progress;
          JobStatus = status.Status;
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("MultiVoiceGenerator.LoadJobStatusFailed", ex.Message);
        await HandleErrorAsync(ex, "LoadJobStatus");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadResultsAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(CurrentJobId))
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var results = await _multiVoiceClient.GetResultsAsync(CurrentJobId!, cancellationToken);

        if (results != null)
        {
          Results.Clear();
          foreach (var item in results.Items)
          {
            Results.Add(new VoiceGenerationResultItem(item));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("MultiVoiceGenerator.LoadResultsFailed", ex.Message);
        await HandleErrorAsync(ex, "LoadResults");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CompareVoicesAsync(CancellationToken cancellationToken = default)
    {
      if (SelectedAudioIdsForComparison.Count < 2)
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new MultiVoiceCompareRequest
        {
          AudioIds = SelectedAudioIdsForComparison.ToList(),
          ComparisonType = "quality"
        };

        var response = await _multiVoiceClient.CompareVoicesAsync(request, cancellationToken);

        if (response != null)
        {
          StatusMessage = ResourceHelper.FormatString(
              "MultiVoiceGenerator.BestAudio",
              response.BestAudioId ?? string.Empty,
              response.BestScore ?? 0f);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("MultiVoiceGenerator.CompareVoicesFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadEnginesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var engines = await _multiVoiceClient.GetEnginesAsync(cancellationToken);
        AvailableEngines.Clear();
        foreach (var eng in engines)
          AvailableEngines.Add(eng);
      }
      catch (OperationCanceledException) { Debug.WriteLine("MultiVoiceGeneratorViewModel: LoadEnginesAsync cancelled"); }
      catch (Exception ex) { ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "MultiVoiceGeneratorViewModel.LoadEnginesAsync"); }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await LoadEnginesAsync(cancellationToken);
      if (!string.IsNullOrWhiteSpace(CurrentJobId))
      {
        await LoadJobStatusAsync(cancellationToken);
        await LoadResultsAsync(cancellationToken);
      }
      StatusMessage = ResourceHelper.GetString("MultiVoiceGenerator.Refreshed", "Refreshed");
    }
  }

  // Data models
  public class VoiceGenerationItem : ObservableObject
  {
    public string ItemId { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public string QualityMode { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? Emotion { get; set; }
    public string Status { get; set; } = string.Empty;
    public float Progress { get; set; }
    public string? AudioId { get; set; }
    public string? AudioUrl { get; set; }
    public float? QualityScore { get; set; }

    public string StatusDisplay => Status.ToUpper();
    public string ProgressDisplay => $"{Progress:P0}";
    public string QualityScoreDisplay => QualityScore.HasValue ? $"{QualityScore.Value:F2}" : "N/A";
  }

  public class VoiceGenerationResultItem : ObservableObject
  {
    public string ItemId { get; set; }
    public string ProfileId { get; set; }
    public string Text { get; set; }
    public string Engine { get; set; }
    public string QualityMode { get; set; }
    public string Language { get; set; }
    public string? Emotion { get; set; }
    public string? AudioId { get; set; }
    public string? AudioUrl { get; set; }
    public float? QualityScore { get; set; }
    public Dictionary<string, object>? QualityMetrics { get; set; }

    public string QualityScoreDisplay => QualityScore.HasValue ? $"{QualityScore.Value:F2}" : "N/A";
    public string TextPreview => Text.Length > 50 ? Text.Substring(0, 50) + "..." : Text;

    public VoiceGenerationResultItem(MultiVoiceResultItem data)
    {
      ItemId = data.ItemId;
      ProfileId = data.ProfileId;
      Text = data.Text;
      Engine = data.Engine;
      QualityMode = data.QualityMode;
      Language = data.Language;
      Emotion = data.Emotion;
      AudioId = data.AudioId;
      AudioUrl = data.AudioUrl;
      QualityScore = data.QualityScore;
      QualityMetrics = data.QualityMetrics;
    }
  }
}