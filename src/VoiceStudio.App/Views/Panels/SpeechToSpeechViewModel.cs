using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Bounded batch speech-to-speech panel (GAP-051).
  /// </summary>
  public partial class SpeechToSpeechViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly ISpeechToSpeechService _speechToSpeechService;
    private readonly IProfilesClient _profilesClient;

    public SpeechToSpeechViewModel(
        IViewModelContext context,
        ISpeechToSpeechService speechToSpeechService,
        IProfilesClient profilesClient)
        : base(context)
    {
      _speechToSpeechService = speechToSpeechService ?? throw new ArgumentNullException(nameof(speechToSpeechService));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));
    }

    public string PanelId => PanelIds.SpeechToSpeech;

    public string DisplayName =>
        ResourceHelper.GetString("Panel.SpeechToSpeech.DisplayName", "Speech to Speech");

    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private string sourceAudioId = string.Empty;

    [ObservableProperty]
    private ObservableCollection<VoiceProfile> profiles = new();

    [ObservableProperty]
    private VoiceProfile? selectedTargetProfile;

    [ObservableProperty]
    private string statusText =
        "Enter a registered source audio id and select a target voice profile.";

    [ObservableProperty]
    private bool isConverting;

    [ObservableProperty]
    private string? outputAudioId;

    [ObservableProperty]
    private string? outputAudioUrl;

    [ObservableProperty]
    private double pitchShift;

    [ObservableProperty]
    private double indexRate = 0.5;

    [ObservableProperty]
    private double protect = 0.33;

    [ObservableProperty]
    private bool consentAcknowledged;

    [ObservableProperty]
    private bool outputIsTransformed;

    [ObservableProperty]
    private string? outputDisclosureText;

    [ObservableProperty]
    private bool outputMarkingVerified;

    [ObservableProperty]
    private string? outputMarkingType;

    [ObservableProperty]
    private bool outputWatermarkApplied;

    [ObservableProperty]
    private bool? outputWatermarkVerified;

    [ObservableProperty]
    private string? outputWatermarkMethod;

    public bool HasOutputDisclosure => !string.IsNullOrEmpty(OutputDisclosureText);

    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public async Task OnActivatedAsync(CancellationToken cancellationToken = default) =>
        await RefreshAsync(cancellationToken).ConfigureAwait(false);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        var list = await _profilesClient.GetProfilesAsync(cancellationToken).ConfigureAwait(false);
        Dispatcher.TryEnqueue(() =>
        {
          Profiles = new ObservableCollection<VoiceProfile>(list);
        });
      }
      catch (Exception ex)
      {
        Logger.LogWarning(ex, "Failed to load profiles for speech-to-speech panel.");
      }
    }

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertAsync()
    {
      if (SelectedTargetProfile == null || string.IsNullOrWhiteSpace(SourceAudioId))
        return;

      IsConverting = true;
      ErrorMessage = null;
      StatusText = "Converting…";
      OutputAudioId = null;
      OutputAudioUrl = null;
      OutputIsTransformed = false;
      OutputDisclosureText = null;
      OutputMarkingVerified = false;
      OutputMarkingType = null;
      OutputWatermarkApplied = false;
      OutputWatermarkVerified = null;
      OutputWatermarkMethod = null;

      try
      {
        var req = new SpeechToSpeechRequest
        {
          SourceAudioId = SourceAudioId.Trim(),
          TargetVoiceProfileId = SelectedTargetProfile.Id,
          PitchShift = PitchShift,
          IndexRate = IndexRate,
          Protect = Protect,
          ConsentAcknowledged = ConsentAcknowledged,
        };

        var res = await _speechToSpeechService.ConvertSpeechAsync(req).ConfigureAwait(true);
        OutputAudioId = res.AudioId;
        OutputAudioUrl = res.AudioUrl;
        OutputIsTransformed = res.IsTransformed;
        OutputDisclosureText = res.DisclosureText;
        StatusText = $"Done. Duration {res.Duration:F2}s.";

        try
        {
          var marking = await _speechToSpeechService
              .GetMarkingAsync(res.AudioId)
              .ConfigureAwait(true);
          OutputMarkingVerified = marking?.IsTransformed == true;
          OutputMarkingType = marking?.TransformationType;
          OutputWatermarkApplied = marking?.WatermarkApplied == true;
          OutputWatermarkVerified = marking?.WatermarkVerified;
          OutputWatermarkMethod = marking?.WatermarkMethod;
        }
        catch (Exception ex)
        {
          Logger.LogWarning(ex, "Marking status lookup failed (non-blocking).");
        }
      }
      catch (Exception ex)
      {
        StatusText = "Conversion failed.";
        ErrorMessage = ex.Message;
        Logger.LogWarning(ex, "Speech-to-speech conversion failed.");
      }
      finally
      {
        IsConverting = false;
      }
    }

    private bool CanConvert() =>
        !IsConverting
        && !string.IsNullOrWhiteSpace(SourceAudioId)
        && SelectedTargetProfile != null
        && ConsentAcknowledged;

    partial void OnIsConvertingChanged(bool value) => ConvertCommand.NotifyCanExecuteChanged();

    partial void OnSourceAudioIdChanged(string value) => ConvertCommand.NotifyCanExecuteChanged();

    partial void OnSelectedTargetProfileChanged(VoiceProfile? value) => ConvertCommand.NotifyCanExecuteChanged();

    partial void OnConsentAcknowledgedChanged(bool value) => ConvertCommand.NotifyCanExecuteChanged();

    partial void OnOutputDisclosureTextChanged(string? value) =>
        OnPropertyChanged(nameof(HasOutputDisclosure));
  }
}
