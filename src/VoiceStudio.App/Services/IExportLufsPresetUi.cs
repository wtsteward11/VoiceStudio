using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>Per-export LUFS preset picker (hybrid with settings default). GAP-041.</summary>
public interface IExportLufsPresetUi
{
  /// <summary>User selects a preset; <c>null</c> if cancelled.</summary>
  Task<string?> PickPresetAsync(string defaultPresetId, CancellationToken cancellationToken = default);
}
