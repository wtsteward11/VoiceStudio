namespace VoiceStudio.App.Configuration;

public class AudioOptions
{
    public int DefaultSampleRate { get; set; } = 44100;
    public int DefaultBitDepth { get; set; } = 16;
    public bool EnableGpuAcceleration { get; set; } = true;
}
