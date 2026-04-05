using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace VoiceStudio.App.Services;

/// <summary>
/// WinUI ContentDialog preset picker for timeline export loudness.
/// </summary>
public sealed class ExportLufsPresetDialogService : IExportLufsPresetUi
{
  private static readonly IReadOnlyList<(string Id, string Label)> Presets =
      new List<(string, string)>
      {
          ("podcast_stereo", "Podcast (stereo, -16 LUFS)"),
          ("podcast_mono", "Podcast (mono, -19 LUFS)"),
          ("broadcast", "Broadcast (-23 LUFS)"),
          ("streaming", "Streaming (-14 LUFS)"),
          ("neutral", "Neutral (no normalization)"),
      };

  private readonly IDialogService _dialogService;

  public ExportLufsPresetDialogService(IDialogService dialogService)
  {
    _dialogService = dialogService ?? throw new System.ArgumentNullException(nameof(dialogService));
  }

  public async Task<string?> PickPresetAsync(string defaultPresetId, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    var combo = new ComboBox { Width = 360 };
    foreach (var (id, label) in Presets)
    {
      combo.Items.Add(new ComboBoxItem { Content = label, Tag = id });
    }

    var norm = (defaultPresetId ?? "podcast_stereo").Trim().ToLowerInvariant();
    for (var i = 0; i < combo.Items.Count; i++)
    {
      if (combo.Items[i] is ComboBoxItem item && item.Tag is string tid && tid == norm)
      {
        combo.SelectedIndex = i;
        break;
      }
    }

    if (combo.SelectedIndex < 0)
      combo.SelectedIndex = 0;

    var panel = new StackPanel
    {
      Spacing = 8,
      Children =
      {
        new TextBlock { Text = "Loudness preset for this export (see Settings for default):", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
        combo,
      },
    };

    var ok = await _dialogService.ShowContentAsync(
        "Export loudness",
        panel,
        primaryText: "Continue",
        cancelText: "Cancel").ConfigureAwait(true);

    if (!ok)
      return null;

    if (combo.SelectedItem is ComboBoxItem selected && selected.Tag is string sid)
      return sid;

    return "podcast_stereo";
  }
}
