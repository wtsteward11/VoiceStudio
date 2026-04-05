namespace VoiceStudio.App.Services;

/// <summary>Result of resolving track + input device ids before a command-path recording start (GAP-042 Slice 2).</summary>
public readonly struct RecordingAuthorityResolution
{
    private RecordingAuthorityResolution(bool success, string? trackId, string? inputSourceId, string? errorMessage)
    {
        Success = success;
        TrackId = trackId;
        InputSourceId = inputSourceId;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }

    public string? TrackId { get; }

    public string? InputSourceId { get; }

    public string? ErrorMessage { get; }

    public static RecordingAuthorityResolution Ok(string trackId, string inputSourceId) =>
        new(true, trackId, inputSourceId, null);

    public static RecordingAuthorityResolution Fail(string message) =>
        new(false, null, null, message);
}
