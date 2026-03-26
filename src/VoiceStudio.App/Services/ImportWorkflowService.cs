// VoiceStudio - Import workflow implementation (Transport Coherence Wave 4 Phase 2)
// Extracted from MainWindow.xaml.cs per TRANSPORT_WAVE_4_SHELL_DECOMPOSITION_PLAN.md

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Services;
using Windows.Storage.Pickers;

namespace VoiceStudio.App.Services;

/// <summary>
/// Orchestrates import: file picker, upload via ILibraryClient, AssetAddedEvent,
/// SetCurrentPlayable, toast notifications.
/// </summary>
public sealed class ImportWorkflowService : IImportWorkflowService
{
    private readonly ILibraryClient _libraryClient;
    private readonly IEventAggregator? _eventAggregator;
    private readonly IContextManager _contextManager;
    private readonly IProjectAudioClient _projectAudioClient;
    private readonly IErrorLoggingService? _logService;

    public ImportWorkflowService(
        ILibraryClient libraryClient,
        IContextManager contextManager,
        IProjectAudioClient projectAudioClient,
        IErrorLoggingService? logService = null,
        IEventAggregator? eventAggregator = null)
    {
        _libraryClient = libraryClient ?? throw new ArgumentNullException(nameof(libraryClient));
        _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
        _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
        _logService = logService;
        _eventAggregator = eventAggregator;
    }

    /// <inheritdoc />
    public async Task<bool> ImportAudioFileAsync(IntPtr parentWindowHandle, CancellationToken ct = default)
    {
        string? filePath = null;

        try
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
                picker.FileTypeFilter.Add(".wav");
                picker.FileTypeFilter.Add(".mp3");
                picker.FileTypeFilter.Add(".flac");
                picker.FileTypeFilter.Add(".ogg");
                picker.FileTypeFilter.Add(".m4a");
                picker.FileTypeFilter.Add(".aac");
                picker.FileTypeFilter.Add(".wma");

                WinRT.Interop.InitializeWithWindow.Initialize(picker, parentWindowHandle);
                var file = await picker.PickSingleFileAsync().AsTask(ct);
                filePath = file?.Path;
            }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80004005))
            {
                filePath = await NativeFileDialog.ShowOpenFileDialogAsync(
                    parentWindowHandle, "Import Audio File", ".wav", ".mp3", ".flac", ".ogg", ".m4a", ".aac", ".wma");
            }

            if (string.IsNullOrEmpty(filePath))
                return false;

            var uploadedAsset = await _libraryClient.UploadLibraryAssetAsync(filePath, ct);
            if (uploadedAsset == null)
            {
                var toast = AppServices.TryGetToastNotificationService();
                toast?.ShowToast(ToastType.Warning, "Import Incomplete",
                    $"Selected {Path.GetFileName(filePath)} but upload returned no asset. Try again.");
                return false;
            }

            await ApplyPostSingleFileLibraryImportSuccessAsync(uploadedAsset, filePath, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ImportWorkflowService] Import failed: {ex.Message}");
            var toast = AppServices.TryGetToastNotificationService();
            toast?.ShowToast(ToastType.Error, "Import Failed", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Pass 05 P05-Persist-A2: success path after <see cref="ILibraryClient.UploadLibraryAssetAsync"/> (project save, event, transport, toast).
    /// Public for seam tests (same pattern as <see cref="VoiceStudio.App.ViewModels.RecordingViewModel.ApplyPostLibraryUploadSuccessAsync"/>).
    /// </summary>
    public async Task ApplyPostSingleFileLibraryImportSuccessAsync(LibraryAsset uploadedAsset, string filePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uploadedAsset);
        var playbackId = GetPlaybackAudioId(uploadedAsset) ?? uploadedAsset.Id;
        var fileName = Path.GetFileName(filePath);

        await ImportToProjectPersistence.TrySaveAfterSingleFileImportAsync(
            _projectAudioClient,
            _logService,
            _contextManager.ActiveProjectId,
            playbackId,
            filePath,
            ct).ConfigureAwait(false);

        _eventAggregator?.Publish(new AssetAddedEvent("import-workflow", playbackId, "audio", filePath));
        _contextManager.SetCurrentPlayable(playbackId, TransportSource.Library, fileName);
        _contextManager.SetActiveAsset(uploadedAsset.Id, "audio", fileName);

        var toastService = AppServices.TryGetToastNotificationService();
        toastService?.ShowToast(ToastType.Success, "Imported", $"{fileName}. Selected and ready to play.");
    }

    private static string? GetPlaybackAudioId(LibraryAsset asset)
    {
        if (asset == null) return null;
        if (!string.IsNullOrEmpty(asset.AudioId)) return asset.AudioId;
        if (asset.Metadata != null && asset.Metadata.TryGetValue("upload_id", out var v))
        {
            var s = v as string;
#if NET6_0_OR_GREATER
            if (s == null && v is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.String)
                s = je.GetString();
#endif
            if (!string.IsNullOrEmpty(s)) return s;
        }
        return asset.Id;
    }
}
