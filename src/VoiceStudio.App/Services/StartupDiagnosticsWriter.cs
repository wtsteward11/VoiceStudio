using System;
using System.IO;

namespace VoiceStudio.App.Services;

/// <summary>
/// Writes structured startup diagnostics to %LOCALAPPDATA%\VoiceStudio\logs\.
/// Works for both packaged and unpackaged runs.
/// </summary>
public sealed class StartupDiagnosticsWriter : IStartupDiagnosticsWriter
{
    private StreamWriter? _writer;
    private string? _logPath;

    private static string GetLogDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "VoiceStudio", "logs");
    }

    public void BeginSession()
    {
        try
        {
            var logDir = GetLogDirectory();
            Directory.CreateDirectory(logDir);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _logPath = Path.Combine(logDir, $"startup-{timestamp}.log");
            _writer = new StreamWriter(_logPath, append: false)
            {
                AutoFlush = true
            };
            _writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] STARTUP SESSION START");
        }
        catch (Exception)
        {
            // Silent failure - diagnostics are best-effort; do not crash startup
        }
    }

    public void Log(string key, string value)
    {
        try
        {
            _writer?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {key}={value}");
        }
        catch
        {
            // Best-effort
        }
    }

    public void LogFailure(string category, string message)
    {
        try
        {
            _writer?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] FAILURE category={category} message={message}");
        }
        catch
        {
            // Best-effort
        }
    }

    public void EndSession()
    {
        try
        {
            _writer?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] SESSION END");
            _writer?.Dispose();
            _writer = null;
        }
        catch
        {
            // Best-effort
        }
    }
}
