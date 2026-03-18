namespace VoiceStudio.App.Services;

/// <summary>
/// Writes structured startup diagnostics to %LOCALAPPDATA%\VoiceStudio\logs\ for diagnosing
/// backend startup failures without a debugger.
/// </summary>
public interface IStartupDiagnosticsWriter
{
    /// <summary>
    /// Begins a new startup session and returns the log file path.
    /// Creates the log directory if missing.
    /// </summary>
    void BeginSession();

    /// <summary>
    /// Logs a diagnostic entry with timestamp.
    /// </summary>
    void Log(string key, string value);

    /// <summary>
    /// Logs a failure with category and message.
    /// </summary>
    void LogFailure(string category, string message);

    /// <summary>
    /// Flushes and closes the current session.
    /// </summary>
    void EndSession();
}
