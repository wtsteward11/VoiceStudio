using System;

namespace VoiceStudio.App.Services;

/// <summary>
/// Recognized shell file-association extensions (unpackaged Inno/WiX HKCR). GAP-067 slice 4.
/// </summary>
public static class FileActivationArgs
{
    public static readonly string[] RecognizedExtensions = { ".voiceproj", ".vstudio", ".vprofile" };

    public static bool IsRecognizedExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        foreach (var ext in RecognizedExtensions)
        {
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Maps a file path to activation kind. Caller ensures path has a recognized extension.
    /// </summary>
    public static bool TryGetActivationKind(string path, out FileActivationKind kind)
    {
        kind = FileActivationKind.Unknown;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (path.EndsWith(".voiceproj", StringComparison.OrdinalIgnoreCase))
        {
            kind = FileActivationKind.OpenProject;
            return true;
        }

        if (path.EndsWith(".vstudio", StringComparison.OrdinalIgnoreCase))
        {
            kind = FileActivationKind.ImportProject;
            return true;
        }

        if (path.EndsWith(".vprofile", StringComparison.OrdinalIgnoreCase))
        {
            kind = FileActivationKind.ImportProfile;
            return true;
        }

        return false;
    }
}
