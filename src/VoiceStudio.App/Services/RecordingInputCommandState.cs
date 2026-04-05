using System;

namespace VoiceStudio.App.Services;

public sealed class RecordingInputCommandState : IRecordingInputCommandState
{
  private readonly object _sync = new();
  private string? _selectedInputSourceId;

  public string? SelectedInputSourceId
  {
    get
    {
      lock (_sync)
        return _selectedInputSourceId;
    }
  }

  public void SetSelectedInputSourceId(string? inputSourceId)
  {
    lock (_sync)
      _selectedInputSourceId = string.IsNullOrWhiteSpace(inputSourceId) ? null : inputSourceId.Trim();
  }
}
