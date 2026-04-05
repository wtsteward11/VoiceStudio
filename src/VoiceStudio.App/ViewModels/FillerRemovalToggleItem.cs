using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// GAP-047 review lane: one catalog key in the Transcribe flyout with operator remove toggle.
  /// </summary>
  public sealed partial class FillerRemovalToggleItem : ObservableObject
  {
    private readonly Action? _onRemoveEnabledChanged;

    public FillerRemovalToggleItem(
        string key,
        int occurrenceCount,
        bool isRisky,
        bool defaultRemoveEnabled,
        Action? onRemoveEnabledChanged)
    {
      Key = key ?? throw new ArgumentNullException(nameof(key));
      OccurrenceCount = occurrenceCount;
      IsRisky = isRisky;
      _onRemoveEnabledChanged = onRemoveEnabledChanged;
      IsRemoveEnabled = defaultRemoveEnabled;
    }

    public string Key { get; }

    public int OccurrenceCount { get; }

    public bool IsRisky { get; }

    /// <summary>Checkbox / list label for the flyout.</summary>
    public string DisplayLabel =>
        $"{Key} ×{OccurrenceCount}" + (IsRisky ? " (risky)" : string.Empty);

    /// <summary>When true, this key may be stripped by Remove fillers.</summary>
    [ObservableProperty]
    private bool isRemoveEnabled;

    partial void OnIsRemoveEnabledChanged(bool value)
    {
      _onRemoveEnabledChanged?.Invoke();
    }
  }
}
