using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for /api/advanced-settings and /api/gpu-status/devices.
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public interface IAdvancedSettingsClient
  {
    /// <summary>
    /// Gets the current advanced settings from the backend.
    /// </summary>
    Task<AdvancedSettingsData?> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available GPU devices.
    /// </summary>
    Task<List<GpuDeviceInfo>> GetGpuDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves advanced settings to the backend.
    /// </summary>
    /// <param name="settings">Settings payload (object with ui, performance, audio_processing, engine, system).</param>
    Task SaveSettingsAsync(object settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets advanced settings to defaults.
    /// </summary>
    Task ResetSettingsAsync(CancellationToken cancellationToken = default);
  }
}
