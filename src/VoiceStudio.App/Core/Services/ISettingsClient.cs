using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for settings API. Use instead of IBackendClient for settings panel.
  /// </summary>
  public interface ISettingsClient
  {
    Task<Dictionary<string, object>?> CheckDependenciesAsync(CancellationToken cancellationToken = default);
  }
}
