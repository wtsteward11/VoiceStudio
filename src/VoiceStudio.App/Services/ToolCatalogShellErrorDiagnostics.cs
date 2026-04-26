using System;
using System.Collections.Generic;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services;

/// <summary>
/// Default diagnostics: forwards to <see cref="ErrorLogger.LogError"/>.
/// </summary>
public sealed class ToolCatalogShellErrorDiagnostics : IToolCatalogShellDiagnostics
{
    public static readonly ToolCatalogShellErrorDiagnostics Instance = new();

    private ToolCatalogShellErrorDiagnostics()
    {
    }

    public void LogToolCatalogFailure(string message, string source, Exception ex, IReadOnlyDictionary<string, object>? context)
    {
        IDictionary<string, object>? dict = context is null ? null : new Dictionary<string, object>(context);
        if (dict is not null)
        {
            dict["ExceptionType"] = ex.GetType().FullName ?? string.Empty;
        }

        ErrorLogger.LogError(message, source, dict);
    }
}
