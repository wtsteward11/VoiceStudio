namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Response from GET /api/recording/devices.
  /// </summary>
  public class RecordingDevicesResponse
  {
    public RecordingDevice[] Devices { get; set; } = Array.Empty<RecordingDevice>();
  }

  public class RecordingDevice
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
  }
}
