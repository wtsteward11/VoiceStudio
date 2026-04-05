namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-034: canonical operator-facing copy for OS completion notifications.
/// </summary>
internal static class CompletionOsNotificationMessages
{
    internal const string BatchCompleteTitle = "Batch complete";
    internal const string BatchFailedTitle = "Batch failed";

    internal const string TrainingCompleteTitle = "Training complete";
    internal const string TrainingFailedTitle = "Training failed";

    internal const string ExportCompleteTitle = "Export complete";
    internal const string ExportFailedTitle = "Export failed";

    /// <summary>Trim notification body; avoid long stack traces and sensitive overflow in the toast.</summary>
    internal static string Shorten(string? text, int maxChars = 160)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var t = text.Trim();
        return t.Length <= maxChars ? t : string.Concat(t.AsSpan(0, maxChars - 1), "\u2026");
    }
}
