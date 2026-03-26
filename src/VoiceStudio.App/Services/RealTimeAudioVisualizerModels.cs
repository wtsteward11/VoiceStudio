namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Real-Time Audio Visualizer API models. Matches backend /api/realtime-visualizer.
  /// </summary>
  public class VisualizerStartRequest
  {
    public string VisualizationType { get; set; } = "both";
    public double UpdateRate { get; set; } = 30.0;
    public int FftSize { get; set; } = 2048;
    public string WindowType { get; set; } = "hann";
    public bool ShowPhase { get; set; }
    public string ColorScheme { get; set; } = "default";
  }

  public class VisualizerStartResponse
  {
    public string SessionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
  }
}
