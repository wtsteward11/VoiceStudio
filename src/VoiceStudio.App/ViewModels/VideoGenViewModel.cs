using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Storage.Pickers;
using Windows.Storage;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for video generation panel.
  /// </summary>
  public class VideoGenViewModel : BaseViewModel, IPanelView
  {
    public string PanelId => "video-gen";
    public string DisplayName => ResourceHelper.GetString("Panel.VideoGen.DisplayName", "Video Generation");
    public PanelRegion Region => PanelRegion.Center;
    private readonly IBackendClient _backendClient;
    private readonly ToastNotificationService? _toastNotificationService;

    private string _selectedEngine = string.Empty;
    private string _prompt = string.Empty;
    private string? _selectedImagePath;
    private string? _selectedAudioPath;
    private int _width = 512;
    private int _height = 512;
    private double _fps = 24;
    private double _duration = 5.0;
    private int _steps = 20;
    private double _cfgScale = 7.0;
    private int? _seed;
    private GeneratedVideo? _selectedVideo;
    private VideoQualityPreset? _selectedQualityPreset;
    private double _bitrate = 10.0;
    private string _codec = "H.264";
    private bool _hasQualityComparison;
    private string? _currentQualityMetrics;
    private string? _presetQualityMetrics;
    private double _videoClarity;
    private double _videoCompression;
    private string _videoResolution = string.Empty;
    private double _videoFrameRate;
    private bool _enablePreprocessing;
    private string _denoisingMethod = "None";
    private string _enhancementMethod = "None";
    private double _enhancementStrength = 50.0;

    public VideoGenViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      // Get services using helper (reduces code duplication)
      _toastNotificationService = ServiceInitializationHelper.TryGetService(() => AppServices.TryGetToastNotificationService());

      Engines = new ObservableCollection<string>();
      GeneratedVideos = new ObservableCollection<GeneratedVideo>();
      QualityPresets = new ObservableCollection<VideoQualityPreset>();

      GenerateCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("GenerateVideo");
        await GenerateVideoAsync(ct);
      }, () => CanGenerate);
      SelectImageCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SelectImage");
        await SelectImageAsync(ct);
      }, () => !IsLoading);
      SelectAudioCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SelectAudio");
        await SelectAudioAsync(ct);
      }, () => !IsLoading);
      UpscaleCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpscaleVideo");
        await UpscaleVideoAsync(ct);
      }, () => SelectedVideo != null && !IsLoading);
      AutoOptimizeQualityCommand = new RelayCommand(AutoOptimizeQuality, () => !IsLoading);

      LoadQualityPresets();

      // Load engines asynchronously
      _ = LoadEnginesAsync(CancellationToken.None);
    }

    private async Task LoadEnginesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var engines = await _backendClient.ListVideoEnginesAsync(cancellationToken);
        Engines.Clear();
        foreach (var engine in engines)
        {
          Engines.Add(engine);
        }

        // If no engines loaded, use fallback list
        if (Engines.Count == 0)
        {
          foreach (var engine in new[] { "svd", "deforum", "fomm", "sadtalker", "deepfacelab", "moviepy", "ffmpeg_ai", "video_creator", "voice_ai", "lyrebird" })
          {
            Engines.Add(engine);
          }
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        // Use fallback list if loading fails
        var fallbackEngines = new[] { "svd", "deforum", "fomm", "sadtalker", "deepfacelab", "moviepy", "ffmpeg_ai", "video_creator", "voice_ai", "lyrebird" };
        Engines.Clear();
        foreach (var engine in fallbackEngines)
        {
          Engines.Add(engine);
        }
        await HandleErrorAsync(ex, "LoadEngines");
      }
    }

    public ObservableCollection<string> Engines { get; }
    public ObservableCollection<GeneratedVideo> GeneratedVideos { get; }

    public string SelectedEngine
    {
      get => _selectedEngine;
      set
      {
        if (SetProperty(ref _selectedEngine, value))
        {
          UpdateInputVisibility();
          ((EnhancedAsyncRelayCommand)GenerateCommand).NotifyCanExecuteChanged();
        }
      }
    }

    public string Prompt
    {
      get => _prompt;
      set
      {
        SetProperty(ref _prompt, value);
        ((EnhancedAsyncRelayCommand)GenerateCommand).NotifyCanExecuteChanged();
      }
    }

    public string? SelectedImagePath
    {
      get => _selectedImagePath;
      set => SetProperty(ref _selectedImagePath, value);
    }

    public string? SelectedAudioPath
    {
      get => _selectedAudioPath;
      set => SetProperty(ref _selectedAudioPath, value);
    }

    public double Duration
    {
      get => _duration;
      set => SetProperty(ref _duration, value);
    }

    public int Steps
    {
      get => _steps;
      set
      {
        if (SetProperty(ref _steps, value))
        {
          UpdateQualityComparison();
        }
      }
    }

    public double CfgScale
    {
      get => _cfgScale;
      set
      {
        if (SetProperty(ref _cfgScale, value))
        {
          UpdateQualityComparison();
        }
      }
    }

    public int Width
    {
      get => _width;
      set
      {
        if (SetProperty(ref _width, value))
        {
          UpdateQualityComparison();
        }
      }
    }

    public int Height
    {
      get => _height;
      set
      {
        if (SetProperty(ref _height, value))
        {
          UpdateQualityComparison();
        }
      }
    }

    public double Fps
    {
      get => _fps;
      set
      {
        if (SetProperty(ref _fps, value))
        {
          UpdateQualityComparison();
        }
      }
    }

    public int? Seed
    {
      get => _seed;
      set => SetProperty(ref _seed, value);
    }

    public GeneratedVideo? SelectedVideo
    {
      get => _selectedVideo;
      set
      {
        if (SetProperty(ref _selectedVideo, value))
        {
          ((EnhancedAsyncRelayCommand)UpscaleCommand).NotifyCanExecuteChanged();
          if (value != null)
          {
            LoadVideoQualityMetrics(value);
          }
          else
          {
            VideoClarity = 0.0;
            VideoCompression = 0.0;
            VideoResolution = string.Empty;
            VideoFrameRate = 0.0;
          }
        }
      }
    }

    public bool ShowImageInput => SelectedEngine == "svd" || SelectedEngine == "fomm" || SelectedEngine == "sadtalker" || SelectedEngine == "deepfacelab";
    public bool ShowAudioInput => SelectedEngine == "sadtalker" || SelectedEngine == "video_creator";
    public bool CanGenerate => !string.IsNullOrWhiteSpace(SelectedEngine) && !IsLoading;
    public bool HasGeneratedVideos => GeneratedVideos.Count > 0;

    public bool EnablePreprocessing
    {
      get => _enablePreprocessing;
      set => SetProperty(ref _enablePreprocessing, value);
    }

    public string DenoisingMethod
    {
      get => _denoisingMethod;
      set => SetProperty(ref _denoisingMethod, value);
    }

    public string EnhancementMethod
    {
      get => _enhancementMethod;
      set => SetProperty(ref _enhancementMethod, value);
    }

    public double EnhancementStrength
    {
      get => _enhancementStrength;
      set => SetProperty(ref _enhancementStrength, value);
    }

    public ICommand GenerateCommand { get; }
    public ICommand SelectImageCommand { get; }
    public ICommand SelectAudioCommand { get; }
    public ICommand UpscaleCommand { get; }
    public ICommand AutoOptimizeQualityCommand { get; }

    public ObservableCollection<VideoQualityPreset> QualityPresets { get; }

    public VideoQualityPreset? SelectedQualityPreset
    {
      get => _selectedQualityPreset;
      set
      {
        if (SetProperty(ref _selectedQualityPreset, value) && value != null)
        {
          ApplyQualityPreset(value);
          UpdateQualityComparison();
        }
      }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
      base.OnPropertyChanged(e);

      if (e.PropertyName == nameof(IsLoading))
      {
        NotifyCommandStatesChanged();
        OnPropertyChanged(nameof(CanGenerate));
      }
    }

    private void NotifyCommandStatesChanged()
    {
      if (GenerateCommand is EnhancedAsyncRelayCommand gen)
      {
        gen.NotifyCanExecuteChanged();
      }
      if (SelectImageCommand is EnhancedAsyncRelayCommand selectImage)
      {
        selectImage.NotifyCanExecuteChanged();
      }
      if (SelectAudioCommand is EnhancedAsyncRelayCommand selectAudio)
      {
        selectAudio.NotifyCanExecuteChanged();
      }
      if (UpscaleCommand is EnhancedAsyncRelayCommand upscale)
      {
        upscale.NotifyCanExecuteChanged();
      }
      if (AutoOptimizeQualityCommand is RelayCommand autoOptimize)
      {
        autoOptimize.NotifyCanExecuteChanged();
      }
    }

    public double Bitrate
    {
      get => _bitrate;
      set
      {
        if (SetProperty(ref _bitrate, value))
        {
          UpdateQualityComparison();
        }
      }
    }

    public string Codec
    {
      get => _codec;
      set
      {
        if (SetProperty(ref _codec, value))
        {
          UpdateQualityComparison();
        }
      }
    }

    public bool HasQualityComparison
    {
      get => _hasQualityComparison;
      private set => SetProperty(ref _hasQualityComparison, value);
    }

    public string? CurrentQualityMetrics
    {
      get => _currentQualityMetrics;
      private set => SetProperty(ref _currentQualityMetrics, value);
    }

    public string? PresetQualityMetrics
    {
      get => _presetQualityMetrics;
      private set => SetProperty(ref _presetQualityMetrics, value);
    }

    public double VideoClarity
    {
      get => _videoClarity;
      private set => SetProperty(ref _videoClarity, value);
    }

    public double VideoCompression
    {
      get => _videoCompression;
      private set => SetProperty(ref _videoCompression, value);
    }

    public string VideoResolution
    {
      get => _videoResolution;
      private set => SetProperty(ref _videoResolution, value);
    }

    public double VideoFrameRate
    {
      get => _videoFrameRate;
      private set => SetProperty(ref _videoFrameRate, value);
    }

    private void UpdateInputVisibility()
    {
      OnPropertyChanged(nameof(ShowImageInput));
      OnPropertyChanged(nameof(ShowAudioInput));
    }

    private void LoadQualityPresets()
    {
      QualityPresets.Clear();
      QualityPresets.Add(new VideoQualityPreset
      {
        Id = "standard",
        Name = "Standard",
        Description = "Balanced quality and speed",
        Width = 512,
        Height = 512,
        Fps = 24,
        Bitrate = 5.0,
        Codec = "H.264"
      });
      QualityPresets.Add(new VideoQualityPreset
      {
        Id = "high",
        Name = "High",
        Description = "Higher quality, slower generation",
        Width = 768,
        Height = 768,
        Fps = 30,
        Bitrate = 10.0,
        Codec = "H.265"
      });
      QualityPresets.Add(new VideoQualityPreset
      {
        Id = "ultra",
        Name = "Ultra",
        Description = "Maximum quality, slowest generation",
        Width = 1024,
        Height = 1024,
        Fps = 60,
        Bitrate = 20.0,
        Codec = "H.265"
      });

      SelectedQualityPreset = QualityPresets.FirstOrDefault(p => p.Id == "standard");
    }

    private void ApplyQualityPreset(VideoQualityPreset preset)
    {
      Width = preset.Width;
      Height = preset.Height;
      Fps = preset.Fps;
      Bitrate = preset.Bitrate;
      Codec = preset.Codec;
    }

    private void UpdateQualityComparison()
    {
      HasQualityComparison = SelectedQualityPreset != null;
      if (SelectedQualityPreset != null)
      {
        CurrentQualityMetrics = ResourceHelper.FormatString("VideoGen.QualityMetricsFormat", Width, Height, Fps, Bitrate, Codec);
        PresetQualityMetrics = ResourceHelper.FormatString("VideoGen.QualityMetricsFormat", SelectedQualityPreset.Width, SelectedQualityPreset.Height, SelectedQualityPreset.Fps, SelectedQualityPreset.Bitrate, SelectedQualityPreset.Codec);
      }
    }

    private void LoadVideoQualityMetrics(GeneratedVideo video)
    {
      VideoResolution = ResourceHelper.FormatString("VideoGen.VideoResolutionFormat", video.Width, video.Height);
      VideoFrameRate = video.Fps;

      _ = LoadVideoQualityMetricsAsync(video, CancellationToken.None);
    }

    private async Task LoadVideoQualityMetricsAsync(GeneratedVideo video, CancellationToken cancellationToken)
    {
      ArgumentNullException.ThrowIfNull(video);

      try
      {
        var metricsResponse = await _backendClient.SendRequestAsync<object, VideoQualityMetricsResponse>(
            $"/api/video/{video.VideoId}/quality",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        if (metricsResponse != null)
        {
          VideoClarity = metricsResponse.Clarity;
          VideoCompression = metricsResponse.Compression;
          return;
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "VideoGenViewModel.LoadVideoQualityMetricsAsync");
      }

      CalculateQualityMetricsFromProperties(video);
    }

    private void CalculateQualityMetricsFromProperties(GeneratedVideo video)
    {
      var resolutionScore = Math.Min(100.0, video.Width * video.Height / (1920.0 * 1080.0) * 100.0);
      var fpsScore = Math.Min(100.0, video.Fps / 60.0 * 100.0);
      var bitrateScore = Math.Min(100.0, Bitrate / 20.0 * 100.0);

      VideoClarity = (resolutionScore * 0.5) + (fpsScore * 0.3) + (bitrateScore * 0.2);
      VideoCompression = Math.Max(0.0, Math.Min(100.0, 100.0 - (Bitrate / 20.0 * 50.0)));
    }

    private class VideoQualityMetricsResponse
    {
      public double Clarity { get; set; }
      public double Compression { get; set; }
    }

    private void AutoOptimizeQuality()
    {
      // Auto-optimize quality settings based on requirements
      // Selects High quality preset as the default optimization
      SelectedQualityPreset = QualityPresets.FirstOrDefault(p => p.Id == "high");
      _toastNotificationService?.ShowInfo(
          ResourceHelper.GetString("VideoGen.QualityOptimized", "Quality optimized to High preset"),
          ResourceHelper.GetString("Toast.Title.AutoOptimize", "Auto-Optimize"));
    }

    private async Task GenerateVideoAsync(CancellationToken cancellationToken)
    {
      if (!CanGenerate)
        return;

      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.GetString("VideoGen.GeneratingVideo", "Generating video...");

      try
      {
        var request = new VideoGenerateRequest
        {
          Engine = SelectedEngine,
          Prompt = !string.IsNullOrWhiteSpace(Prompt) ? Prompt : null,
          ImageId = !string.IsNullOrWhiteSpace(SelectedImagePath) ? SelectedImagePath : null,
          AudioId = !string.IsNullOrWhiteSpace(SelectedAudioPath) ? SelectedAudioPath : null,
          Width = Width,
          Height = Height,
          Fps = Fps,
          Duration = Duration,
          Steps = Steps,
          CfgScale = CfgScale,
          Seed = Seed,
          EnablePreprocessing = EnablePreprocessing,
          DenoisingMethod = DenoisingMethod != "None" ? DenoisingMethod : null,
          EnhancementMethod = EnhancementMethod != "None" ? EnhancementMethod : null,
          EnhancementStrength = EnablePreprocessing ? (int)EnhancementStrength : 0
        };

        var response = await _backendClient.GenerateVideoAsync(request, cancellationToken);

        var video = new GeneratedVideo
        {
          VideoId = response.VideoId,
          VideoUrl = response.VideoUrl,
          Prompt = Prompt,
          Engine = SelectedEngine,
          Width = response.Width,
          Height = response.Height,
          Fps = response.Fps,
          Duration = response.Duration
        };

        GeneratedVideos.Insert(0, video);
        SelectedVideo = video;
        OnPropertyChanged(nameof(HasGeneratedVideos));

        StatusMessage = ResourceHelper.GetString("VideoGen.VideoGeneratedSuccess", "Video generated successfully!");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("VideoGen.VideoGenerated", response.Duration, response.Width, response.Height),
            ResourceHelper.GetString("Toast.Title.GenerationComplete", "Generation Complete"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        await HandleErrorAsync(ex, "GenerateVideo");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("VideoGen.GenerateVideoFailed", ErrorHandler.GetUserFriendlyMessage(ex)),
            ResourceHelper.GetString("Toast.Title.GenerationFailed", "Generation Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task SelectImageAsync(CancellationToken cancellationToken)
    {
      try
      {
        // FileOpenPicker doesn't support cancellation, but we check the token before and after
        cancellationToken.ThrowIfCancellationRequested();

        var openPicker = new FileOpenPicker();
        openPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        openPicker.FileTypeFilter.Add(".jpg");
        openPicker.FileTypeFilter.Add(".jpeg");
        openPicker.FileTypeFilter.Add(".png");
        openPicker.FileTypeFilter.Add(".bmp");
        openPicker.FileTypeFilter.Add(".webp");
        openPicker.FileTypeFilter.Add(".gif");

        // WinUI 3 requires initializing the picker with the window handle
        var window = App.MainWindowInstance;
        if (window != null)
        {
          var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
          WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);
        }

        var file = await openPicker.PickSingleFileAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (file != null)
        {
          SelectedImagePath = file.Path;
          StatusMessage = ResourceHelper.FormatString("VideoGen.ImageSelected", file.Name);
          ErrorMessage = null;
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VideoGen.SelectImageFailed", ex.Message);
        await HandleErrorAsync(ex, "SelectImage");
      }
    }

    private async Task SelectAudioAsync(CancellationToken cancellationToken)
    {
      try
      {
        // FileOpenPicker doesn't support cancellation, but we check the token before and after
        cancellationToken.ThrowIfCancellationRequested();

        var openPicker = new FileOpenPicker();
        openPicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
        openPicker.FileTypeFilter.Add(".wav");
        openPicker.FileTypeFilter.Add(".mp3");
        openPicker.FileTypeFilter.Add(".flac");
        openPicker.FileTypeFilter.Add(".ogg");
        openPicker.FileTypeFilter.Add(".m4a");
        openPicker.FileTypeFilter.Add(".aac");

        // WinUI 3 requires initializing the picker with the window handle
        var window = App.MainWindowInstance;
        if (window != null)
        {
          var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
          WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);
        }

        var file = await openPicker.PickSingleFileAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (file != null)
        {
          SelectedAudioPath = file.Path;
          StatusMessage = ResourceHelper.FormatString("VideoGen.AudioSelected", file.Name);
          ErrorMessage = null;
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "SelectAudio");
      }
    }

    private async Task UpscaleVideoAsync(CancellationToken cancellationToken)
    {
      if (SelectedVideo == null)
        return;

      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.GetString("VideoGen.UpscalingVideo", "Upscaling video...");

      try
      {
        var request = new VideoUpscaleRequest
        {
          VideoId = SelectedVideo.VideoId,
          Scale = 2 // Default 2x upscale
        };

        var response = await _backendClient.UpscaleVideoAsync(request, cancellationToken);

        // Update the selected video with upscaled version
        SelectedVideo.VideoId = response.VideoId;
        SelectedVideo.VideoUrl = response.VideoUrl;
        SelectedVideo.Width = response.Width;
        SelectedVideo.Height = response.Height;

        StatusMessage = ResourceHelper.FormatString("VideoGen.VideoUpscaledSuccess", response.Width, response.Height);
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("VideoGen.VideoUpscaled", response.Width, response.Height),
            ResourceHelper.GetString("Toast.Title.UpscaleComplete", "Upscale Complete"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        await HandleErrorAsync(ex, "UpscaleVideo");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("VideoGen.UpscaleVideoFailed", ErrorHandler.GetUserFriendlyMessage(ex)),
            ResourceHelper.GetString("Toast.Title.UpscaleFailed", "Upscale Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }
  }

  /// <summary>
  /// Model for generated video.
  /// </summary>
  public class GeneratedVideo
  {
    public string VideoId { get; set; } = string.Empty;
    public string VideoUrl { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; }
    public double Duration { get; set; }
    public string? QualityMetrics { get; set; }
  }

  /// <summary>
  /// Model for video quality preset.
  /// </summary>
  public class VideoQualityPreset
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; }
    public double Bitrate { get; set; }
    public string Codec { get; set; } = "H.264";
  }
}