namespace VoiceStudio.App.Services;

/// <summary>
/// Last microphone choice from the Recording panel for command-path parity (GAP-035). Ctrl+R uses this id, not a random fallback.
/// </summary>
public interface IRecordingInputCommandState
{
  /// <summary>Backend <see cref="RecordingDevice.Id"/> last published from the Recording panel, or null if none.</summary>
  string? SelectedInputSourceId { get; }

  void SetSelectedInputSourceId(string? inputSourceId);
}
