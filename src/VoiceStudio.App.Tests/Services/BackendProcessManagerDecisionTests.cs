using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
[DoNotParallelize]
[TestCategory("Services")]
public class BackendProcessManagerDecisionTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string PythonPath = Path.Combine(RepoRoot, ".venv", "Scripts", "python.exe");
    private static readonly string DecisionArtifactPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceStudio",
        "crashes",
        "startup_decision.json");

    private static async Task WaitForHealthAsync(string baseUrl, int timeoutSeconds = 30)
    {
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            try
            {
                using var response = await client.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BackendProcessManagerDecisionTests] WaitForHealthAsync retry: {ex.Message}");
            }

            await Task.Delay(500);
        }

        Assert.Fail($"Backend at {baseUrl} did not become healthy within {timeoutSeconds}s.");
    }

    private static Process StartManualBackend(int port)
    {
        Assert.IsTrue(File.Exists(PythonPath), $"Expected Python runtime at {PythonPath}");

        var psi = new ProcessStartInfo
        {
            FileName = PythonPath,
            Arguments = $"-m uvicorn backend.api.main:app --host 127.0.0.1 --port {port}",
            WorkingDirectory = RepoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.Environment["PYTHONPATH"] = RepoRoot;
        psi.Environment["PYTHONUNBUFFERED"] = "1";

        var process = new Process { StartInfo = psi };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "VoiceStudio.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail($"Could not locate repository root from {AppContext.BaseDirectory}");
        return string.Empty;
    }

    private static JsonDocument ReadDecisionArtifact()
    {
        Assert.IsTrue(File.Exists(DecisionArtifactPath), $"Expected startup decision artifact at {DecisionArtifactPath}");
        var raw = File.ReadAllText(DecisionArtifactPath);
        return JsonDocument.Parse(raw);
    }

    [TestMethod]
    public async Task EnsureBackendRunningAsync_WhenHealthyBackendExists_WritesReuseDecision()
    {
        const int port = 8015;
        File.Delete(DecisionArtifactPath);

        var oldPortEnv = Environment.GetEnvironmentVariable("VOICESTUDIO_API_PORT");
        var oldAppRootEnv = Environment.GetEnvironmentVariable("VOICESTUDIO_APP_ROOT");
        Environment.SetEnvironmentVariable("VOICESTUDIO_API_PORT", null);
        Environment.SetEnvironmentVariable("VOICESTUDIO_APP_ROOT", RepoRoot);

        Process? manualBackend = null;
        try
        {
            manualBackend = StartManualBackend(port);
            await WaitForHealthAsync($"http://127.0.0.1:{port}");

            using var manager = new BackendProcessManager($"http://127.0.0.1:{port}");
            var started = await manager.EnsureBackendRunningAsync();

            Assert.IsTrue(started, "Expected manager to reuse an already healthy backend.");

            using var doc = ReadDecisionArtifact();
            var root = doc.RootElement;
            Assert.AreEqual("reuse", root.GetProperty("decision").GetString());
            Assert.IsTrue(root.GetProperty("health_probe_result").GetBoolean());
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("backend_pid").ValueKind);
            Assert.AreEqual(1, root.GetProperty("schema_version").GetInt32());
            Assert.IsFalse(root.GetProperty("spawn_attempted").GetBoolean());
            Assert.IsTrue(root.GetProperty("reused_existing_backend").GetBoolean());
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("conflict_category").ValueKind);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOICESTUDIO_API_PORT", oldPortEnv);
            Environment.SetEnvironmentVariable("VOICESTUDIO_APP_ROOT", oldAppRootEnv);
            if (manualBackend is { HasExited: false })
            {
                manualBackend.Kill(entireProcessTree: true);
                manualBackend.WaitForExit(5000);
            }
        }
    }

    [TestMethod]
    public async Task EnsureBackendRunningAsync_WhenBackendMissing_WritesSpawnDecision()
    {
        const int port = 8016;
        File.Delete(DecisionArtifactPath);

        var oldPortEnv = Environment.GetEnvironmentVariable("VOICESTUDIO_API_PORT");
        var oldAppRootEnv = Environment.GetEnvironmentVariable("VOICESTUDIO_APP_ROOT");
        var repoVenvPython = Path.Combine(RepoRoot, "venv", "Scripts", "python.exe");
        var repoVenvPythonBackup = repoVenvPython + ".slice1-backup";
        Environment.SetEnvironmentVariable("VOICESTUDIO_API_PORT", null);
        Environment.SetEnvironmentVariable("VOICESTUDIO_APP_ROOT", RepoRoot);

        using var manager = new BackendProcessManager($"http://127.0.0.1:{port}");
        try
        {
            // Force deterministic interpreter selection to .venv for this proof.
            if (File.Exists(repoVenvPython))
            {
                if (File.Exists(repoVenvPythonBackup))
                {
                    File.Delete(repoVenvPythonBackup);
                }

                File.Move(repoVenvPython, repoVenvPythonBackup);
            }

            var started = await manager.EnsureBackendRunningAsync();

            Assert.IsTrue(started, "Expected manager to spawn backend when none is running.");
            await WaitForHealthAsync($"http://127.0.0.1:{port}");

            using var doc = ReadDecisionArtifact();
            var root = doc.RootElement;
            Assert.AreEqual("spawn", root.GetProperty("decision").GetString());
            Assert.IsFalse(root.GetProperty("health_probe_result").GetBoolean());
            Assert.AreEqual(JsonValueKind.Number, root.GetProperty("backend_pid").ValueKind);
            Assert.IsTrue(root.GetProperty("backend_pid").GetInt32() > 0);
            Assert.AreEqual(1, root.GetProperty("schema_version").GetInt32());
            Assert.IsTrue(root.GetProperty("spawn_attempted").GetBoolean());
            Assert.IsFalse(root.GetProperty("reused_existing_backend").GetBoolean());
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("conflict_category").ValueKind);
        }
        finally
        {
            if (File.Exists(repoVenvPythonBackup))
            {
                if (File.Exists(repoVenvPython))
                {
                    File.Delete(repoVenvPython);
                }

                File.Move(repoVenvPythonBackup, repoVenvPython);
            }

            Environment.SetEnvironmentVariable("VOICESTUDIO_API_PORT", oldPortEnv);
            Environment.SetEnvironmentVariable("VOICESTUDIO_APP_ROOT", oldAppRootEnv);
        }
    }

    [TestMethod]
    public async Task EnsureBackendRunningAsync_WhenPortHeldByNonHttpListener_WritesPortCollisionDecision()
    {
        const int port = 8017;
        File.Delete(DecisionArtifactPath);

        var oldPortEnv = Environment.GetEnvironmentVariable("VOICESTUDIO_API_PORT");
        var oldAppRootEnv = Environment.GetEnvironmentVariable("VOICESTUDIO_APP_ROOT");
        Environment.SetEnvironmentVariable("VOICESTUDIO_API_PORT", port.ToString());
        Environment.SetEnvironmentVariable("VOICESTUDIO_APP_ROOT", RepoRoot);

        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();

            using var manager = new BackendProcessManager($"http://127.0.0.1:{port}");
            var started = await manager.EnsureBackendRunningAsync();

            Assert.IsFalse(started, "Expected port collision when port is held by a non-health TCP endpoint.");
            Assert.IsNotNull(manager.LastFailure);
            Assert.AreEqual(BackendStartFailureCategory.PortCollision, manager.LastFailure.FailureCategory);

            using var doc = ReadDecisionArtifact();
            var root = doc.RootElement;
            Assert.AreEqual("port_collision", root.GetProperty("decision").GetString());
            Assert.IsFalse(root.GetProperty("health_probe_result").GetBoolean());
            Assert.IsTrue(root.GetProperty("port_occupied").GetBoolean());
            Assert.IsFalse(root.GetProperty("spawn_attempted").GetBoolean());
            Assert.IsFalse(root.GetProperty("reused_existing_backend").GetBoolean());
            Assert.AreEqual("port_collision", root.GetProperty("conflict_category").GetString());
            Assert.AreEqual(1, root.GetProperty("schema_version").GetInt32());
        }
        finally
        {
            listener?.Stop();
            Environment.SetEnvironmentVariable("VOICESTUDIO_API_PORT", oldPortEnv);
            Environment.SetEnvironmentVariable("VOICESTUDIO_APP_ROOT", oldAppRootEnv);
        }
    }

    [TestMethod]
    public async Task EnsureBackendRunningAsync_SecondCall_ReusesWithoutSecondSpawn()
    {
        const int port = 8018;
        File.Delete(DecisionArtifactPath);

        var oldPortEnv = Environment.GetEnvironmentVariable("VOICESTUDIO_API_PORT");
        var oldAppRootEnv = Environment.GetEnvironmentVariable("VOICESTUDIO_APP_ROOT");
        var repoVenvPython = Path.Combine(RepoRoot, "venv", "Scripts", "python.exe");
        var repoVenvPythonBackup = repoVenvPython + ".slice3-repeat-backup";
        Environment.SetEnvironmentVariable("VOICESTUDIO_API_PORT", null);
        Environment.SetEnvironmentVariable("VOICESTUDIO_APP_ROOT", RepoRoot);

        BackendProcessManager? manager = null;
        try
        {
            if (File.Exists(repoVenvPython))
            {
                if (File.Exists(repoVenvPythonBackup))
                {
                    File.Delete(repoVenvPythonBackup);
                }

                File.Move(repoVenvPython, repoVenvPythonBackup);
            }

            manager = new BackendProcessManager($"http://127.0.0.1:{port}");
            var started1 = await manager.EnsureBackendRunningAsync();
            Assert.IsTrue(started1, "Expected first call to spawn backend.");
            await WaitForHealthAsync($"http://127.0.0.1:{port}");

            using (var doc1 = ReadDecisionArtifact())
            {
                var root1 = doc1.RootElement;
                Assert.AreEqual("spawn", root1.GetProperty("decision").GetString());
                var pid1 = root1.GetProperty("backend_pid").GetInt32();
                Assert.IsTrue(pid1 > 0);
            }

            File.Delete(DecisionArtifactPath);

            var started2 = await manager.EnsureBackendRunningAsync();
            Assert.IsTrue(started2, "Expected second call to reuse healthy backend.");

            using var doc2 = ReadDecisionArtifact();
            var root2 = doc2.RootElement;
            Assert.AreEqual("reuse", root2.GetProperty("decision").GetString());
            Assert.IsTrue(root2.GetProperty("health_probe_result").GetBoolean());
            Assert.IsFalse(root2.GetProperty("spawn_attempted").GetBoolean());
            Assert.IsTrue(root2.GetProperty("reused_existing_backend").GetBoolean());
            Assert.AreEqual(JsonValueKind.Null, root2.GetProperty("conflict_category").ValueKind);

            Assert.IsTrue(manager.IsRunning, "Manager should still reference the single spawned backend process.");
        }
        finally
        {
            manager?.Dispose();
            if (File.Exists(repoVenvPythonBackup))
            {
                if (File.Exists(repoVenvPython))
                {
                    File.Delete(repoVenvPython);
                }

                File.Move(repoVenvPythonBackup, repoVenvPython);
            }

            Environment.SetEnvironmentVariable("VOICESTUDIO_API_PORT", oldPortEnv);
            Environment.SetEnvironmentVariable("VOICESTUDIO_APP_ROOT", oldAppRootEnv);
        }
    }
}
