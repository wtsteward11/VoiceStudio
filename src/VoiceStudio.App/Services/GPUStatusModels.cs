namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// GPU Status API models. Matches backend /api/gpu-status.
  /// </summary>
  public class GPUStatusResponse
  {
    public GPUStatusDevice[] Devices { get; set; } = System.Array.Empty<GPUStatusDevice>();
    public int TotalDevices { get; set; }
    public int AvailableDevices { get; set; }
    public string? PrimaryDevice { get; set; }
  }

  public class GPUStatusDevice
  {
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public int MemoryTotalMb { get; set; }
    public int MemoryUsedMb { get; set; }
    public int MemoryFreeMb { get; set; }
    public double UtilizationPercent { get; set; }
    public double? TemperatureCelsius { get; set; }
    public double? PowerUsageWatts { get; set; }
    public string? DriverVersion { get; set; }
    public string? ComputeCapability { get; set; }
    public bool IsAvailable { get; set; }
  }
}
