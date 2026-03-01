namespace VoiceStudio.App.Configuration;

public class BackendOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8000;
    public string BaseUrl => $"http://{Host}:{Port}";
    public string WebSocketUrl => $"ws://{Host}:{Port}/ws/realtime";
}
