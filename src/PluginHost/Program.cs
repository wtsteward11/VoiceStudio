using System.Text.Json;

class Program
{
    static int Main(string[] args)
    {
        var pluginDir = Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%/VoiceStudio/plugins");
        Directory.CreateDirectory(pluginDir);
        foreach (var file in Directory.GetFiles(pluginDir, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(file);
                var doc = JsonDocument.Parse(json);
                var id = doc.RootElement.GetProperty("id").GetString();
                Console.WriteLine($"Loaded plugin manifest: {id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Bad manifest at {file}: {ex.Message}");
            }
        }
        Console.WriteLine("PluginHost OK (stub).");
        return 0;
    }
}
