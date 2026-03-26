using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Advanced Real-Time Visualization API (/api/visualization, /api/audio/playback-position).
  /// Use instead of IBackendClient for AdvancedRealTimeVisualization panel.
  /// </summary>
  public interface IAdvancedRealTimeVisualizationClient
  {
    Task<Dictionary<string, object>?> GetVisualizationDataAsync(string visualizationType, double updateRate, CancellationToken cancellationToken = default);

    Task<TimeSpan> GetPlaybackPositionAsync(CancellationToken cancellationToken = default);
  }
}
