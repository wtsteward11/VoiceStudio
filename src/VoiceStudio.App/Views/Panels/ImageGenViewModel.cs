using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;
using ImageGenModels = VoiceStudio.Core.Models;

namespace VoiceStudio.App.Views.Panels
{
  public partial class ImageGenViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;

    public string PanelId => "image_gen";
    public string DisplayName => ResourceHelper.GetString("Panel.ImageGeneration.DisplayName", "Image Generation");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<string> engines = new()
        {
            "sdxl",
            "sdxl_comfy",
            "comfyui",
            "automatic1111",
            "sdnext",
            "invokeai",
            "fooocus",
            "localai",
            "realistic_vision",
            "openjourney",
            "sd_cpu",
            "fastsd_cpu"
        };

    [ObservableProperty]
    private string selectedEngine = "sdxl";

    [ObservableProperty]
    private string prompt = string.Empty;

    [ObservableProperty]
    private string negativePrompt = string.Empty;

    [ObservableProperty]
    private int width = 512;

    [ObservableProperty]
    private int height = 512;

    [ObservableProperty]
    private int steps = 20;

    [ObservableProperty]
    private double cfgScale = 7.0;

    [ObservableProperty]
    private string? sampler;

    [ObservableProperty]
    private int? seed;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private ObservableCollection<GeneratedImage> generatedImages = new();

    [ObservableProperty]
    private GeneratedImage? selectedImage;

    [ObservableProperty]
    private string? currentImageUrl;

    [ObservableProperty]
    private bool hasGeneratedImages;

    [ObservableProperty]
    private ObservableCollection<ImageQualityPreset> qualityPresets = new();

    [ObservableProperty]
    private ImageQualityPreset? selectedQualityPreset;

    [ObservableProperty]
    private string upscaleFactor = "4x";

    [ObservableProperty]
    private bool hasQualityComparison;

    [ObservableProperty]
    private string? currentQualityMetrics;

    [ObservableProperty]
    private string? presetQualityMetrics;

    [ObservableProperty]
    private double imageClarity;

    [ObservableProperty]
    private double imageDetail;

    [ObservableProperty]
    private double imageStyleFidelity;

    [ObservableProperty]
    private double imageOverallQuality;

    [ObservableProperty]
    private bool enablePreprocessing;

    [ObservableProperty]
    private string denoisingMethod = "None";

    [ObservableProperty]
    private string enhancementMethod = "None";

    [ObservableProperty]
    private double enhancementStrength = 50.0;

    public bool CanGenerate => !IsLoading && !string.IsNullOrWhiteSpace(Prompt);
    public bool CanUpscale => SelectedImage != null && !IsLoading;

    partial void OnGeneratedImagesChanged(ObservableCollection<GeneratedImage> value)
    {
      HasGeneratedImages = value.Count > 0;
    }

    public ImageGenViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      LoadQualityPresets();
    }

    partial void OnSelectedQualityPresetChanged(ImageQualityPreset? value)
    {
      if (value != null)
      {
        ApplyQualityPreset(value);
        UpdateQualityComparison();
      }
    }

    partial void OnStepsChanged(int value)
    {
      UpdateQualityComparison();
    }

    partial void OnWidthChanged(int value)
    {
      UpdateQualityComparison();
    }

    partial void OnHeightChanged(int value)
    {
      UpdateQualityComparison();
    }

    partial void OnCfgScaleChanged(double value)
    {
      UpdateQualityComparison();
    }

    partial void OnSelectedImageChanged(GeneratedImage? value)
    {
      if (value != null)
      {
        CurrentImageUrl = value.ImageUrl;
        LoadImageQualityMetrics(value);
      }
      else
      {
        ImageClarity = 0.0;
        ImageDetail = 0.0;
        ImageStyleFidelity = 0.0;
        ImageOverallQuality = 0.0;
      }
    }

    private void LoadImageQualityMetrics(GeneratedImage image)
    {
      if (!string.IsNullOrEmpty(image.QualityMetrics))
      {
        try
        {
          // Parse quality metrics from JSON string if available
          var metrics = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(image.QualityMetrics);

          if (metrics.TryGetProperty("clarity", out var clarityElement))
            ImageClarity = clarityElement.GetDouble();
          else if (metrics.TryGetProperty("imageClarity", out var imageClarityElement))
            ImageClarity = imageClarityElement.GetDouble();

          if (metrics.TryGetProperty("detail", out var detailElement))
            ImageDetail = detailElement.GetDouble();
          else if (metrics.TryGetProperty("imageDetail", out var imageDetailElement))
            ImageDetail = imageDetailElement.GetDouble();

          if (metrics.TryGetProperty("styleFidelity", out var styleFidelityElement))
            ImageStyleFidelity = styleFidelityElement.GetDouble();
          else if (metrics.TryGetProperty("imageStyleFidelity", out var imageStyleFidelityElement))
            ImageStyleFidelity = imageStyleFidelityElement.GetDouble();

          if (metrics.TryGetProperty("overallQuality", out var overallQualityElement))
            ImageOverallQuality = overallQualityElement.GetDouble();
          else if (metrics.TryGetProperty("imageOverallQuality", out var imageOverallQualityElement))
            ImageOverallQuality = imageOverallQualityElement.GetDouble();
          else
            ImageOverallQuality = (ImageClarity + ImageDetail + ImageStyleFidelity) / 3.0;
        }
        catch
        {
          // If parsing fails, fall back to calculation based on parameters
          CalculateQualityMetricsFromParameters();
        }
      }
      else
      {
        // Calculate based on generation parameters
        CalculateQualityMetricsFromParameters();
      }
    }

    private void CalculateQualityMetricsFromParameters()
    {
      var qualityScore = (Steps / 60.0 * 30.0) + // Steps contribution (max 30%)
                         (CfgScale / 10.0 * 20.0) + // CFG contribution (max 20%)
                         (Width * Height / (1024.0 * 1024.0) * 50.0); // Resolution contribution (max 50%)

      ImageClarity = Math.Min(100.0, qualityScore * 0.9);
      ImageDetail = Math.Min(100.0, qualityScore * 0.85);
      ImageStyleFidelity = Math.Min(100.0, qualityScore * 0.95);
      ImageOverallQuality = (ImageClarity + ImageDetail + ImageStyleFidelity) / 3.0;
    }

    private void LoadQualityPresets()
    {
      QualityPresets.Clear();
      QualityPresets.Add(new ImageQualityPreset
      {
        Id = "standard",
        Name = "Standard",
        Description = "Balanced quality and speed",
        Steps = 20,
        CfgScale = 7.0,
        Width = 512,
        Height = 512,
        DetailLevel = "Medium"
      });
      QualityPresets.Add(new ImageQualityPreset
      {
        Id = "high",
        Name = "High",
        Description = "Higher quality, slower generation",
        Steps = 40,
        CfgScale = 8.5,
        Width = 768,
        Height = 768,
        DetailLevel = "High"
      });
      QualityPresets.Add(new ImageQualityPreset
      {
        Id = "ultra",
        Name = "Ultra",
        Description = "Maximum quality, slowest generation",
        Steps = 60,
        CfgScale = 10.0,
        Width = 1024,
        Height = 1024,
        DetailLevel = "Ultra"
      });

      SelectedQualityPreset = QualityPresets.FirstOrDefault(p => p.Id == "standard");
    }

    private void ApplyQualityPreset(ImageQualityPreset preset)
    {
      Steps = preset.Steps;
      CfgScale = preset.CfgScale;
      Width = preset.Width;
      Height = preset.Height;
    }

    private void UpdateQualityComparison()
    {
      HasQualityComparison = SelectedQualityPreset != null;
      if (SelectedQualityPreset != null)
      {
        CurrentQualityMetrics = $"Steps: {Steps}, CFG: {CfgScale:F1}, Size: {Width}×{Height}";
        PresetQualityMetrics = $"Steps: {SelectedQualityPreset.Steps}, CFG: {SelectedQualityPreset.CfgScale:F1}, Size: {SelectedQualityPreset.Width}×{SelectedQualityPreset.Height}";
      }
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(Prompt))
        return;

      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = "Generating image...";

      try
      {
        var request = new ImageGenModels.ImageGenerateRequest
        {
          Engine = SelectedEngine,
          Prompt = Prompt,
          NegativePrompt = NegativePrompt,
          Width = Width,
          Height = Height,
          Steps = Steps,
          CfgScale = CfgScale,
          Sampler = Sampler,
          Seed = Seed,
          EnablePreprocessing = EnablePreprocessing,
          DenoisingMethod = DenoisingMethod != "None" ? DenoisingMethod : null,
          EnhancementMethod = EnhancementMethod != "None" ? EnhancementMethod : null,
          EnhancementStrength = EnablePreprocessing ? (int)EnhancementStrength : 0
        };

        var response = await _backendClient.SendRequestAsync<ImageGenModels.ImageGenerateRequest, ImageGenModels.ImageGenerateResponse>(
            "/api/image/generate",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response != null)
        {
          var image = new GeneratedImage
          {
            ImageId = response.ImageId,
            ImageUrl = response.ImageUrl,
            ImageBase64 = response.ImageBase64,
            Width = response.Width,
            Height = response.Height,
            Format = response.Format,
            Prompt = Prompt,
            Engine = SelectedEngine,
            GeneratedAt = DateTime.Now
          };

          GeneratedImages.Insert(0, image);
          SelectedImage = image;
          CurrentImageUrl = response.ImageUrl;
          StatusMessage = "Image generated successfully!";
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to generate image: {ex.Message}";
        StatusMessage = null;
        await HandleErrorAsync(ex, "GenerateImage");
      }
      finally
      {
        IsLoading = false;
      }
    }

    [RelayCommand(CanExecute = nameof(CanUpscale))]
    private async Task UpscaleAsync(CancellationToken cancellationToken)
    {
      if (SelectedImage == null)
        return;

      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = "Upscaling image...";

      try
      {
        var scale = int.Parse(UpscaleFactor.Replace("x", ""));
        var request = new ImageGenModels.ImageUpscaleRequest
        {
          ImageId = SelectedImage.ImageId,
          Scale = scale
        };

        var response = await _backendClient.SendRequestAsync<ImageGenModels.ImageUpscaleRequest, ImageGenModels.ImageUpscaleResponse>(
            "/api/image/upscale",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response != null)
        {
          var upscaledImage = new GeneratedImage
          {
            ImageId = response.ImageId,
            ImageUrl = response.ImageUrl,
            ImageBase64 = response.ImageBase64,
            Width = response.Width,
            Height = response.Height,
            Format = "png",
            Prompt = $"{SelectedImage.Prompt} (Upscaled {response.Scale}x)",
            Engine = "realesrgan",
            GeneratedAt = DateTime.Now
          };

          GeneratedImages.Insert(0, upscaledImage);
          SelectedImage = upscaledImage;
          CurrentImageUrl = response.ImageUrl;
          StatusMessage = "Image upscaled successfully!";
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to upscale image: {ex.Message}";
        StatusMessage = null;
        await HandleErrorAsync(ex, "UpscaleImage");
      }
      finally
      {
        IsLoading = false;
      }
    }

    [RelayCommand]
    private Task LoadEnginesAsync(CancellationToken cancellationToken)
    {
      try
      {
        cancellationToken.ThrowIfCancellationRequested();
        // Try to load engines from backend
        // For now, use default engines list
        // In future, can add dedicated endpoint or use SendRequestAsync with proper response model
      }
      catch (OperationCanceledException)
      {
        return Task.CompletedTask; // User cancelled
      }
      catch (Exception ex)
      {
        // Silently fail - use default engines
        System.Diagnostics.Debug.WriteLine($"Failed to load engines: {ex.Message}");
      }

      return Task.CompletedTask;
    }
  }

  public class GeneratedImage
  {
    public string ImageId { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string ImageBase64 { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Format { get; set; } = "png";
    public string Prompt { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public string? QualityMetrics { get; set; }
  }

  public class ImageQualityPreset
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Steps { get; set; }
    public double CfgScale { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string DetailLevel { get; set; } = string.Empty;
  }
}