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
using VoiceStudio.Core.Models;
using VoiceStudio.App.Utilities;
using EmbeddingVectorModel = VoiceStudio.App.Services.EmbeddingVector;
using EmbeddingSimilarityModel = VoiceStudio.App.Services.EmbeddingSimilarity;
using EmbeddingVisualizationModel = VoiceStudio.App.Services.EmbeddingVisualization;
using EmbeddingClusterModel = VoiceStudio.App.Services.EmbeddingCluster;
using Windows.Storage.Pickers;
using Windows.Storage;
using Windows.Storage.Streams;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the EmbeddingExplorerView panel - Speaker embedding visualization.
  /// </summary>
  public partial class EmbeddingExplorerViewModel : BaseViewModel, IPanelView
  {
    private readonly IEmbeddingExplorerClient _embeddingClient;
    private readonly IProjectsClient _projectsClient;
    private readonly IProjectAudioClient _projectAudioClient;
    private readonly IProfilesClient _profilesClient;
    private readonly ToastNotificationService? _toastNotificationService;

    public string PanelId => PanelIds.EmbeddingExplorer;
    public string DisplayName => ResourceHelper.GetString("Panel.EmbeddingExplorer.DisplayName", "Speaker Embedding Explorer");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<EmbeddingItem> embeddings = new();

    [ObservableProperty]
    private EmbeddingItem? selectedEmbedding1;

    [ObservableProperty]
    private EmbeddingItem? selectedEmbedding2;

    [ObservableProperty]
    private EmbeddingSimilarityItem? similarityResult;

    [ObservableProperty]
    private string? sourceAudioId;

    [ObservableProperty]
    private ObservableCollection<string> availableAudioIds = new();

    [ObservableProperty]
    private string? selectedVoiceProfileId;

    [ObservableProperty]
    private ObservableCollection<string> availableVoiceProfiles = new();

    [ObservableProperty]
    private string visualizationMethod = "pca";

    [ObservableProperty]
    private ObservableCollection<string> availableVisualizationMethods = new() { "pca", "t-sne", "umap" };

    [ObservableProperty]
    private int visualizationDimensions = 2;

    [ObservableProperty]
    private ObservableCollection<EmbeddingVisualizationItem> visualizationData = new();

    [ObservableProperty]
    private int numClusters = 5;

    [ObservableProperty]
    private ObservableCollection<EmbeddingClusterItem> clusters = new();

    public IRelayCommand DeleteSelectedEmbeddingsCommand { get; }

    partial void OnEmbeddingsChanged(ObservableCollection<EmbeddingItem> value)
    {
      ExportEmbeddingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnVisualizationDataChanged(ObservableCollection<EmbeddingVisualizationItem> value)
    {
      ExportVisualizationCommand.NotifyCanExecuteChanged();
    }

    public EmbeddingExplorerViewModel(IViewModelContext context, IEmbeddingExplorerClient embeddingClient, IProjectsClient projectsClient, IProjectAudioClient projectAudioClient, IProfilesClient profilesClient)
        : base(context)
    {
      _embeddingClient = embeddingClient ?? throw new ArgumentNullException(nameof(embeddingClient));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));

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

      LoadEmbeddingsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadEmbeddings");
        await LoadEmbeddingsAsync(ct);
      }, () => !IsLoading);
      ExtractEmbeddingCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ExtractEmbedding");
        await ExtractEmbeddingAsync(ct);
      }, () => !string.IsNullOrEmpty(SourceAudioId) && !IsLoading);
      DeleteEmbeddingCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteEmbedding");
        await DeleteEmbeddingAsync(ct);
      }, () => SelectedEmbedding1 != null && !IsLoading);
      CompareEmbeddingsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CompareEmbeddings");
        await CompareEmbeddingsAsync(ct);
      }, () => SelectedEmbedding1 != null && SelectedEmbedding2 != null && !IsLoading);
      DeleteSelectedEmbeddingsCommand = new RelayCommand(
          () =>
          {
            if (SelectedEmbedding1 != null)
            {
              Embeddings.Remove(SelectedEmbedding1);
              SelectedEmbedding1 = null;
              SelectedEmbedding2 = null;
            }
          },
          () => SelectedEmbedding1 != null);
      VisualizeEmbeddingsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("VisualizeEmbeddings");
        await VisualizeEmbeddingsAsync(ct);
      }, () => Embeddings.Count > 0 && !IsLoading);
      ClusterEmbeddingsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ClusterEmbeddings");
        await ClusterEmbeddingsAsync(ct);
      }, () => Embeddings.Count > 0 && !IsLoading);
      LoadAudioFilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadAudioFiles");
        await LoadAudioFilesAsync(ct);
      }, () => !IsLoading);
      LoadVoiceProfilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadVoiceProfiles");
        await LoadVoiceProfilesAsync(ct);
      }, () => !IsLoading);
      ExportEmbeddingsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ExportEmbeddings");
        await ExportEmbeddingsAsync(ct);
      }, () => Embeddings.Count > 0 && !IsLoading);
      ExportVisualizationCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ExportVisualization");
        await ExportVisualizationAsync(ct);
      }, () => VisualizationData.Count > 0 && !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);

    }

    /// <summary>
    /// Initialize panel data. Call from View Loaded event (ADR-047: no constructor fire-and-forget).
    /// </summary>
    public void InitializeAsync()
    {
      var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
      _ = LoadEmbeddingsAsync(cts.Token);
      _ = LoadAudioFilesAsync(cts.Token);
      _ = LoadVoiceProfilesAsync(cts.Token);
    }

    public IAsyncRelayCommand LoadEmbeddingsCommand { get; }
    public IAsyncRelayCommand ExtractEmbeddingCommand { get; }
    public IAsyncRelayCommand DeleteEmbeddingCommand { get; }
    public IAsyncRelayCommand CompareEmbeddingsCommand { get; }
    public IAsyncRelayCommand VisualizeEmbeddingsCommand { get; }
    public IAsyncRelayCommand ClusterEmbeddingsCommand { get; }
    public IAsyncRelayCommand LoadAudioFilesCommand { get; }
    public IAsyncRelayCommand LoadVoiceProfilesCommand { get; }
    public IAsyncRelayCommand ExportEmbeddingsCommand { get; }
    public IAsyncRelayCommand ExportVisualizationCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task LoadEmbeddingsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var embeddings = await _embeddingClient.GetEmbeddingsAsync(cancellationToken);

        if (embeddings != null)
        {
          Embeddings.Clear();
          foreach (var embedding in embeddings)
          {
            Embeddings.Add(new EmbeddingItem(embedding));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadEmbeddings");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ExtractEmbeddingAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SourceAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("EmbeddingExplorer.SourceAudioRequired", "Source audio must be selected");
        return;
      }

      // Validate audio ID exists
      if (!AvailableAudioIds.Contains(SourceAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("EmbeddingExplorer.AudioFileDoesNotExist", "Selected audio file does not exist. Please refresh and select a valid audio file.");
        SourceAudioId = null;
        return;
      }

      // Validate voice profile if provided
      if (!string.IsNullOrEmpty(SelectedVoiceProfileId) && !AvailableVoiceProfiles.Contains(SelectedVoiceProfileId))
      {
        ErrorMessage = ResourceHelper.GetString("EmbeddingExplorer.VoiceProfileDoesNotExist", "Selected voice profile does not exist. Please refresh and select a valid voice profile.");
        SelectedVoiceProfileId = null;
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var embedding = await _embeddingClient.ExtractEmbeddingAsync(SourceAudioId, SelectedVoiceProfileId, cancellationToken);

        if (embedding != null)
        {
          var embeddingItem = new EmbeddingItem(embedding);
          Embeddings.Add(embeddingItem);
          StatusMessage = ResourceHelper.FormatString("EmbeddingExplorer.EmbeddingExtracted", embedding.EmbeddingId);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("EmbeddingExplorer.EmbeddingExtractedDetail", embedding.EmbeddingId),
              ResourceHelper.GetString("Toast.Title.EmbeddingExtracted", "Embedding Extracted"));
          ((System.Windows.Input.ICommand)ExportEmbeddingsCommand).NotifyCanExecuteChanged();
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ExtractEmbedding");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteEmbeddingAsync(CancellationToken cancellationToken)
    {
      if (SelectedEmbedding1 == null)
      {
        ErrorMessage = ResourceHelper.GetString("EmbeddingExplorer.NoEmbeddingSelected", "No embedding selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _embeddingClient.DeleteEmbeddingAsync(SelectedEmbedding1.EmbeddingId, cancellationToken);

        Embeddings.Remove(SelectedEmbedding1);
        SelectedEmbedding1 = null;
        StatusMessage = ResourceHelper.GetString("EmbeddingExplorer.EmbeddingDeleted", "Embedding deleted");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("EmbeddingExplorer.EmbeddingDeletedDetail", "Embedding has been deleted"),
            ResourceHelper.GetString("Toast.Title.EmbeddingDeleted", "Embedding Deleted"));
        ((System.Windows.Input.ICommand)ExportEmbeddingsCommand).NotifyCanExecuteChanged();
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteEmbedding");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CompareEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
      if (SelectedEmbedding1 == null || SelectedEmbedding2 == null)
      {
        ErrorMessage = ResourceHelper.GetString("EmbeddingExplorer.TwoEmbeddingsRequired", "Two embeddings must be selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var similarity = await _embeddingClient.CompareEmbeddingsAsync(SelectedEmbedding1.EmbeddingId, SelectedEmbedding2.EmbeddingId, cancellationToken);

        if (similarity != null)
        {
          SimilarityResult = new EmbeddingSimilarityItem(similarity);
          StatusMessage = ResourceHelper.FormatString("EmbeddingExplorer.SimilarityResult", SimilarityResult.SimilarityDisplay);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("EmbeddingExplorer.CompareEmbeddingsFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task VisualizeEmbeddingsAsync(CancellationToken cancellationToken)
    {
      if (Embeddings.Count == 0)
      {
        ErrorMessage = ResourceHelper.GetString("EmbeddingExplorer.NoEmbeddingsToVisualize", "No embeddings to visualize");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var embeddingIds = Embeddings.Select(e => e.EmbeddingId).ToList();

        var visualizations = await _embeddingClient.VisualizeEmbeddingsAsync(embeddingIds, VisualizationMethod, VisualizationDimensions, cancellationToken);

        if (visualizations != null)
        {
          VisualizationData.Clear();
          foreach (var vis in visualizations)
          {
            VisualizationData.Add(new EmbeddingVisualizationItem(vis));
          }
          StatusMessage = ResourceHelper.FormatString("EmbeddingExplorer.VisualizedEmbeddings", VisualizationData.Count);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("EmbeddingExplorer.VisualizationComplete", VisualizationData.Count, VisualizationMethod),
              ResourceHelper.GetString("Toast.Title.VisualizationComplete", "Visualization Complete"));
          ((System.Windows.Input.ICommand)ExportVisualizationCommand).NotifyCanExecuteChanged();
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "VisualizeEmbeddings");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ClusterEmbeddingsAsync(CancellationToken cancellationToken)
    {
      if (Embeddings.Count == 0)
      {
        ErrorMessage = ResourceHelper.GetString("EmbeddingExplorer.NoEmbeddingsToCluster", "No embeddings to cluster");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var embeddingIds = Embeddings.Select(e => e.EmbeddingId).ToList();

        var clusters = await _embeddingClient.ClusterEmbeddingsAsync(embeddingIds, NumClusters, cancellationToken);

        if (clusters != null)
        {
          Clusters.Clear();
          foreach (var cluster in clusters)
          {
            Clusters.Add(new EmbeddingClusterItem(cluster));
          }
          StatusMessage = ResourceHelper.FormatString("EmbeddingExplorer.ClustersCreated", Clusters.Count);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("EmbeddingExplorer.ClusteringComplete", Clusters.Count, Embeddings.Count),
              ResourceHelper.GetString("Toast.Title.ClusteringComplete", "Clustering Complete"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ClusterEmbeddings");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadAudioFilesAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var projects = await _projectsClient.GetProjectsAsync(cancellationToken);
        var audioIds = new System.Collections.Generic.List<string>();

        foreach (var project in projects)
        {
          var audioFiles = await _projectAudioClient.ListProjectAudioAsync(project.Id, cancellationToken);
          foreach (var audioFile in audioFiles)
          {
            if (!string.IsNullOrEmpty(audioFile.AudioId))
            {
              audioIds.Add(audioFile.AudioId);
            }
          }
        }

        AvailableAudioIds.Clear();
        foreach (var audioId in audioIds.Distinct())
        {
          AvailableAudioIds.Add(audioId);
        }

        StatusMessage = ResourceHelper.FormatString("EmbeddingExplorer.AudioFilesLoaded", AvailableAudioIds.Count);
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("EmbeddingExplorer.LoadAudioFilesFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadVoiceProfilesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var profiles = await _profilesClient.GetProfilesAsync(cancellationToken);

        AvailableVoiceProfiles.Clear();
        foreach (var profile in profiles)
        {
          AvailableVoiceProfiles.Add(profile.Id);
        }

        StatusMessage = ResourceHelper.FormatString("EmbeddingExplorer.VoiceProfilesLoaded", AvailableVoiceProfiles.Count);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadVoiceProfiles");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      try
      {
        await LoadEmbeddingsAsync(cancellationToken);
        await LoadAudioFilesAsync(cancellationToken);
        await LoadVoiceProfilesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("EmbeddingExplorer.Refreshed", "Refreshed");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Refresh");
      }
    }

    private async Task ExportEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
      if (Embeddings.Count == 0)
      {
        ErrorMessage = ResourceHelper.GetString("EmbeddingExplorer.NoEmbeddingsToExport", "No embeddings to export");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // Create export data
        var exportData = new
        {
          embeddings = Embeddings.Select(e => new
          {
            embedding_id = e.EmbeddingId,
            voice_profile_id = e.VoiceProfileId,
            dimension = e.Dimension,
            created = e.Created
          }).ToList(),
          clusters = Clusters.Select(c => new
          {
            cluster_id = c.ClusterId,
            embedding_ids = c.EmbeddingIds.ToList(),
            size = c.Size
          }).ToList(),
          similarity = SimilarityResult != null ? new
          {
            embedding_id_1 = SimilarityResult.EmbeddingId1,
            embedding_id_2 = SimilarityResult.EmbeddingId2,
            similarity = SimilarityResult.Similarity,
            distance = SimilarityResult.Distance
          } : null,
          exported = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
          version = "1.0"
        };

        // Convert to JSON
        var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
        {
          WriteIndented = true
        });

        // Use file picker to save
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = $"embeddings_export_{DateTime.Now:yyyyMMdd_HHmmss}";
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });

        // WinUI 3 requires initializing the picker with the window handle
        var window = App.MainWindowInstance;
        if (window != null)
        {
          var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
          WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSaveFileAsync();
        if (file == null)
        {
          return;
        }

        await FileIO.WriteTextAsync(file, json);
        StatusMessage = ResourceHelper.FormatString("EmbeddingExplorer.EmbeddingsExported", Embeddings.Count);
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("EmbeddingExplorer.EmbeddingsExportedDetail", Embeddings.Count, file.Name),
            ResourceHelper.GetString("Toast.Title.ExportComplete", "Export Complete"));
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("EmbeddingExplorer.ExportEmbeddingsFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.ExportFailed", "Export Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ExportVisualizationAsync(CancellationToken cancellationToken)
    {
      if (VisualizationData.Count == 0)
      {
        ErrorMessage = ResourceHelper.GetString("EmbeddingExplorer.NoVisualizationDataToExport", "No visualization data to export");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Create export data
        var exportData = new
        {
          method = VisualizationMethod,
          dimensions = VisualizationDimensions,
          visualization = VisualizationData.Select(v => new
          {
            embedding_id = v.EmbeddingId,
            x = v.X,
            y = v.Y,
            z = v.Z,
            color = v.Color
          }).ToList(),
          exported = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
          version = "1.0"
        };

        // Convert to JSON
        var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
        {
          WriteIndented = true
        });

        // Use file picker to save
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = $"visualization_export_{DateTime.Now:yyyyMMdd_HHmmss}";
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });

        // WinUI 3 requires initializing the picker with the window handle
        var window = App.MainWindowInstance;
        if (window != null)
        {
          var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
          WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSaveFileAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (file == null)
        {
          return;
        }

        await FileIO.WriteTextAsync(file, json);
        StatusMessage = ResourceHelper.FormatString("EmbeddingExplorer.VisualizationExported", VisualizationData.Count);
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("EmbeddingExplorer.VisualizationExportedDetail", file.Name),
            ResourceHelper.GetString("Toast.Title.ExportComplete", "Export Complete"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ExportVisualization");
      }
      finally
      {
        IsLoading = false;
      }
    }

  }

  // Data models
  public class EmbeddingItem : ObservableObject
  {
    public string EmbeddingId { get; set; }
    public string VoiceProfileId { get; set; }
    public int Dimension { get; set; }
    public string Created { get; set; }
    public string DimensionDisplay => $"{Dimension}D";

    public EmbeddingItem(EmbeddingVectorModel embedding)
    {
      EmbeddingId = embedding.EmbeddingId;
      VoiceProfileId = embedding.VoiceProfileId;
      Dimension = embedding.Dimension;
      Created = embedding.Created;
    }
  }

  public class EmbeddingSimilarityItem : ObservableObject
  {
    public string EmbeddingId1 { get; set; }
    public string EmbeddingId2 { get; set; }
    public double Similarity { get; set; }
    public double Distance { get; set; }
    public string SimilarityDisplay => $"{Similarity:P1}";
    public string DistanceDisplay => $"{Distance:F3}";

    public EmbeddingSimilarityItem(EmbeddingSimilarityModel similarity)
    {
      EmbeddingId1 = similarity.EmbeddingId1;
      EmbeddingId2 = similarity.EmbeddingId2;
      Similarity = similarity.Similarity;
      Distance = similarity.Distance;
    }
  }

  public class EmbeddingVisualizationItem : ObservableObject
  {
    public string EmbeddingId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double? Z { get; set; }
    public string? Color { get; set; }

    public EmbeddingVisualizationItem(EmbeddingVisualizationModel visualization)
    {
      EmbeddingId = visualization.EmbeddingId;
      X = visualization.X;
      Y = visualization.Y;
      Z = visualization.Z;
      Color = visualization.Color;
    }
  }

  public class EmbeddingClusterItem : ObservableObject
  {
    public string ClusterId { get; set; }
    public ObservableCollection<string> EmbeddingIds { get; set; }
    public double[] Centroid { get; set; }
    public int Size { get; set; }
    public string SizeDisplay => $"{Size} embeddings";

    public EmbeddingClusterItem(EmbeddingClusterModel cluster)
    {
      ClusterId = cluster.ClusterId;
      EmbeddingIds = new ObservableCollection<string>(cluster.EmbeddingIds);
      Centroid = cluster.Centroid;
      Size = cluster.Size;
    }
  }
}