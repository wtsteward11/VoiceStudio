using System;
using System.Collections.Generic;

namespace VoiceStudio.App.Services;

/// <summary>
/// Test/production seam for logging command palette open failures (Slice 8 hardening, Task 333).
/// </summary>
public interface ICommandPaletteShellDiagnostics
{
    void LogCommandPaletteOpenFailure(string message, string source, Exception ex, IReadOnlyDictionary<string, object>? context);
}
