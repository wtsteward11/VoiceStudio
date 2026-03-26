namespace VoiceStudio.Core.Services;

/// <summary>
/// Typed source for global transport ownership. Replaces magic strings ("Library", "Timeline", etc.).
/// </summary>
public enum TransportSource
{
    None,
    Library,
    Timeline,
    Synthesis,
    Recording,
    Analyzer,
}

/// <summary>
/// Extension methods for TransportSource display.
/// </summary>
public static class TransportSourceExtensions
{
    /// <summary>
    /// Returns display string for UI (e.g. "Library", "Timeline"). None/null returns "—".
    /// </summary>
    public static string ToDisplayString(this TransportSource? source)
    {
        if (source == null || source == TransportSource.None)
            return "—";
        return source.Value.ToString();
    }
}
