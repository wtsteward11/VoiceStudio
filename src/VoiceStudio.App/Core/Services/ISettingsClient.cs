using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for settings API. Use instead of IBackendClient for settings panel.
  /// </summary>
  public interface ISettingsClient
  {
    Task<Dictionary<string, object>?> CheckDependenciesAsync(CancellationToken cancellationToken = default);

    /// <summary>GAP-053: Resolved engine priority for Settings UI/diagnostics (not on <see cref="IBackendClient"/>).</summary>
    Task<EffectiveEnginePriorityResponse?> GetEffectiveEnginePriorityAsync(
        string taskType = "tts",
        CancellationToken cancellationToken = default);
  }
}
