using System;
using System.IO;
using System.Linq;

namespace VoiceStudio.App.Services;

/// <summary>
/// Kind of pending shell file activation (double-click / Open from Explorer).
/// </summary>
public enum FileActivationKind
{
    Unknown,
    OpenProject,
    ImportProject,
    ImportProfile,
}

/// <summary>
/// One pending file activation consumed after shell and startup are ready (ADR-047).
/// </summary>
public sealed class FileActivationPendingAction
{
    public FileActivationPendingAction(FileActivationKind kind, string filePath)
    {
        Kind = kind;
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public FileActivationKind Kind { get; }
    public string FilePath { get; }
}

/// <summary>
/// Parses argv when Windows launches the unpackaged app via file association (<c>exe "%1"</c>).
/// Parallel to <see cref="JumpListActivation"/>; jump list is checked first.
/// </summary>
public static class FileActivation
{
    private static readonly object Gate = new();
    private static FileActivationPendingAction? _pending;

    /// <summary>
    /// Records a parsed pending file path when a recognized extension is present (replaces prior pending).
    /// </summary>
    public static void SetPendingIfParsed(string? launchArgs, string[]? commandLineArgs)
    {
        var parsed = TryParse(launchArgs, commandLineArgs);
        if (parsed == null)
            return;

        lock (Gate)
        {
            _pending = parsed;
        }
    }

    /// <summary>
    /// Returns whether a pending file activation is queued (read-only).
    /// </summary>
    public static bool HasPending()
    {
        lock (Gate)
        {
            return _pending != null;
        }
    }

    /// <summary>
    /// Returns and clears the pending action, if any.
    /// </summary>
    public static FileActivationPendingAction? TryConsumePending()
    {
        lock (Gate)
        {
            var p = _pending;
            _pending = null;
            return p;
        }
    }

    /// <summary>
    /// Exposed for unit tests; parses the same rules as shell activation.
    /// </summary>
    public static FileActivationPendingAction? TryParse(string? launchArgs, string[]? commandLineArgs)
    {
        var argv = commandLineArgs ?? Environment.GetCommandLineArgs();
        if (argv.Length <= 1)
            return null;

        for (var i = 1; i < argv.Length; i++)
        {
            var a = argv[i];
            if (string.IsNullOrWhiteSpace(a))
                continue;

            if (a.StartsWith("--", StringComparison.Ordinal))
                continue;

            if (!FileActivationArgs.IsRecognizedExtension(a))
                continue;

            if (!FileActivationArgs.TryGetActivationKind(a, out var kind) || kind == FileActivationKind.Unknown)
                continue;

            try
            {
                var full = Path.GetFullPath(a);
                return new FileActivationPendingAction(kind, full);
            }
            catch (Exception)
            {
                return new FileActivationPendingAction(kind, a.Trim());
            }
        }

        var blob = BuildArgumentBlob(launchArgs, commandLineArgs);
        if (string.IsNullOrWhiteSpace(blob))
            return null;

        var quoted = TryExtractQuotedPath(blob);
        if (!string.IsNullOrEmpty(quoted) && FileActivationArgs.IsRecognizedExtension(quoted))
        {
            if (FileActivationArgs.TryGetActivationKind(quoted, out var qKind) && qKind != FileActivationKind.Unknown)
            {
                try
                {
                    var full = Path.GetFullPath(quoted);
                    return new FileActivationPendingAction(qKind, full);
                }
                catch (Exception)
                {
                    return new FileActivationPendingAction(qKind, quoted.Trim());
                }
            }
        }

        return null;
    }

    private static string? TryExtractQuotedPath(string blob)
    {
        var start = blob.IndexOf('\"');
        if (start < 0)
            return null;

        var end = blob.IndexOf('\"', start + 1);
        if (end <= start + 1)
            return null;

        return blob.Substring(start + 1, end - start - 1).Replace("\\\"", "\"", StringComparison.Ordinal);
    }

    private static string BuildArgumentBlob(string? launchArgs, string[]? commandLineArgs)
    {
        if (!string.IsNullOrWhiteSpace(launchArgs))
            return launchArgs.Trim();

        var argv = commandLineArgs ?? Environment.GetCommandLineArgs();
        if (argv.Length <= 1)
            return string.Empty;

        return string.Join(" ", argv.Skip(1)).Trim();
    }
}
