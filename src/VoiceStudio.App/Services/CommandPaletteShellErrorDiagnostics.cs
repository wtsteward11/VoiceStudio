using System;
using System.Collections.Generic;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services;

/// <summary>
/// Default diagnostics: forwards to <see cref="ErrorLogger.LogError"/>.
/// </summary>
public sealed class CommandPaletteShellErrorDiagnostics : ICommandPaletteShellDiagnostics
{
    public static readonly CommandPaletteShellErrorDiagnostics Instance = new();

    private CommandPaletteShellErrorDiagnostics()
    {
    }

    public void LogCommandPaletteOpenFailure(string message, string source, Exception ex, IReadOnlyDictionary<string, object>? context)
    {
        IDictionary<string, object>? dict = context is null ? null : new Dictionary<string, object>(context);
        ErrorLogger.LogError(message, source, dict);
    }
}
