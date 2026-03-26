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
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the MultilingualSupportView panel - Multi-language interface.
  /// </summary>
  public partial class MultilingualSupportViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IMultilingualSupportClient _client;
    private readonly ToastNotificationService? _toastNotificationService;

    public string PanelId => PanelIds.Multilingual;
    public string DisplayName => ResourceHelper.GetString("Panel.MultilingualSupport.DisplayName", "Multilingual Support");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<LanguageItem> supportedLanguages = new();

    [ObservableProperty]
    private ObservableCollection<string> selectedTargetLanguages = new();

    [ObservableProperty]
    private string? sourceLanguage;

    [ObservableProperty]
    private string? detectedLanguage;

    [ObservableProperty]
    private string text = string.Empty;

    [ObservableProperty]
    private string translatedText = string.Empty;

    [ObservableProperty]
    private bool autoDetectLanguage = true;

    [ObservableProperty]
    private bool preserveEmotion = true;

    [ObservableProperty]
    private bool preserveStyle = true;

    [ObservableProperty]
    private ObservableCollection<string> availableProfiles = new();

    [ObservableProperty]
    private ObservableCollection<MultilingualAudioItem> synthesizedAudios = new();

    public MultilingualSupportViewModel(IViewModelContext context, IMultilingualSupportClient client)
        : base(context)
    {
      _client = client ?? throw new ArgumentNullException(nameof(client));

      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
      }
      catch
      {
        _toastNotificationService = null;
      }

      LoadSupportedLanguagesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadSupportedLanguages");
        await LoadSupportedLanguagesAsync(ct);
      }, () => !IsLoading);
      TranslateCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("TranslateText");
        await TranslateTextAsync(ct);
      }, () => !IsLoading);
      SynthesizeCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SynthesizeMultilingual");
        await SynthesizeMultilingualAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
    }

    public IAsyncRelayCommand LoadSupportedLanguagesCommand { get; }
    public IAsyncRelayCommand TranslateCommand { get; }
    public IAsyncRelayCommand SynthesizeCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    Task IPanelLifecycle.OnActivatedAsync(CancellationToken cancellationToken)
    {
      return LoadSupportedLanguagesAsync(cancellationToken);
    }

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    Task IPanelLifecycle.RefreshAsync(CancellationToken cancellationToken)
    {
      return RefreshAsync(cancellationToken);
    }

    private async Task LoadSupportedLanguagesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var response = await _client.GetSupportedLanguagesAsync(cancellationToken);

        if (response?.Languages != null)
        {
          SupportedLanguages.Clear();
          foreach (var lang in response.Languages)
          {
            SupportedLanguages.Add(new LanguageItem(lang));
          }
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("MultilingualSupport.LanguagesLoadedDetail", response.Languages.Length),
              ResourceHelper.GetString("Toast.Title.LanguagesLoaded", "Languages Loaded"));
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadSupportedLanguages");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task TranslateTextAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(Text))
      {
        ErrorMessage = ResourceHelper.GetString("MultilingualSupport.TextRequired", "Text is required");
        return;
      }

      if (string.IsNullOrEmpty(SourceLanguage) || SelectedTargetLanguages.Count == 0)
      {
        ErrorMessage = ResourceHelper.GetString("MultilingualSupport.SourceAndTargetRequired", "Source and target languages must be selected");
        return;
      }

      var targetLang = SelectedTargetLanguages.FirstOrDefault();
      if (string.IsNullOrEmpty(targetLang))
      {
        ErrorMessage = ResourceHelper.GetString("MultilingualSupport.TargetLanguageRequired", "Target language must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var response = await _client.TranslateAsync(Text, SourceLanguage, targetLang, cancellationToken);

        if (response != null)
        {
          TranslatedText = response.TranslatedText;
          StatusMessage = ResourceHelper.FormatString("MultilingualSupport.TranslationComplete", response.SourceLanguage, response.TargetLanguage);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("MultilingualSupport.TranslationCompleteDetail", response.SourceLanguage, response.TargetLanguage),
              ResourceHelper.GetString("Toast.Title.TranslationComplete", "Translation Complete"));
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "TranslateText");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task SynthesizeMultilingualAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(Text))
      {
        ErrorMessage = ResourceHelper.GetString("MultilingualSupport.TextRequired", "Text is required");
        return;
      }

      if (SelectedTargetLanguages.Count == 0)
      {
        ErrorMessage = ResourceHelper.GetString("MultilingualSupport.AtLeastOneTargetRequired", "At least one target language must be selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new MultilingualSynthesisRequest
        {
          Text = Text,
          SourceLanguage = AutoDetectLanguage ? null : SourceLanguage,
          TargetLanguages = SelectedTargetLanguages.ToArray(),
          ProfileIds = new System.Collections.Generic.Dictionary<string, string>(),
          PreserveEmotion = PreserveEmotion,
          PreserveStyle = PreserveStyle
        };

        var response = await _client.SynthesizeAsync(request, cancellationToken);

        if (response != null)
        {
          DetectedLanguage = response.DetectedLanguage;
          SynthesizedAudios.Clear();

          foreach (var kvp in response.AudioIds)
          {
            SynthesizedAudios.Add(new MultilingualAudioItem
            {
              LanguageCode = kvp.Key,
              LanguageName = SupportedLanguages.FirstOrDefault(l => l.Code == kvp.Key)?.Name ?? kvp.Key,
              AudioId = kvp.Value
            });
          }

          StatusMessage = response.Message;
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("MultilingualSupport.SynthesisCompleteDetail", SynthesizedAudios.Count),
              ResourceHelper.GetString("Toast.Title.SynthesisComplete", "Synthesis Complete"));
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "SynthesizeMultilingual");
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
        await LoadSupportedLanguagesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("MultilingualSupport.LanguagesRefreshed", "Languages refreshed");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("MultilingualSupport.LanguagesRefreshedSuccessfully", "Languages refreshed successfully"),
            ResourceHelper.GetString("Toast.Title.Refreshed", "Refreshed"));
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Refresh");
      }
    }
  }

  public class LanguageItem : ObservableObject
  {
    public string Code { get; set; }
    public string Name { get; set; }

    public LanguageItem(LanguageInfo info)
    {
      Code = info.Code;
      Name = info.Name;
    }
  }

  public class MultilingualAudioItem : ObservableObject
  {
    public string LanguageCode { get; set; } = string.Empty;
    public string LanguageName { get; set; } = string.Empty;
    public string AudioId { get; set; } = string.Empty;
  }
}
