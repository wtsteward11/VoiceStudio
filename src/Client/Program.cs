using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Grpc.Net.Client;
using VoiceStudio.IPC;

class Program
{
    static async Task<int> Main(string[] args)
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        if (args.Length < 2 || args[0] != "--job")
        {
            Console.WriteLine("Usage: dotnet run -- --job C:\\path\\to\\job.json");
            return 1;
        }

        var jobPath = args[1];
        if (!File.Exists(jobPath))
        {
            Console.Error.WriteLine("Job file not found: " + jobPath);
            return 2;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(jobPath));
        var root = doc.RootElement;

        var job = new Job
        {
            Id       = root.TryGetProperty("Id", out var id) ? id.GetString() ?? $"VSJ-{DateTime.UtcNow:yyyyMMddHHmmss}" : $"VSJ-{DateTime.UtcNow:yyyyMMddHHmmss}",
            Type     = root.GetProperty("Type").GetString() ?? "",
            InPath   = root.TryGetProperty("InPath", out var ip) ? ip.GetString() ?? "" : "",
            OutPath  = root.TryGetProperty("OutPath", out var op) ? op.GetString() ?? "" : "",
            ArgsJson = root.TryGetProperty("ArgsJson", out var aj) ? aj.GetString() ?? "" : ""
        };

        var channel = GrpcChannel.ForAddress("http://127.0.0.1:5071");
        var client  = new Core.CoreClient(channel);

        var health = await client.HealthAsync(new Empty());
        Console.WriteLine($"Health: {health.Status} v{health.Version}");

        var res = await client.RunAsync(new RunRequest { Job = job });
        Console.WriteLine($"Run status: {res.Status}, code: {res.Code}");
        if (!string.IsNullOrWhiteSpace(res.Message))
            Console.WriteLine("Message:\n" + res.Message.Trim());

        foreach (var p in res.Outputs)
            Console.WriteLine("Output: " + p);

        return res.Status == "ok" ? 0 : 3;
    }
}
