using System;
using System.Collections.Generic;

namespace VoiceStudio.App.Services;

/// <summary>
/// Test/production seam for logging tool catalog shell failures (GAP-008 Slice 10).
/// </summary>
public interface IToolCatalogShellDiagnostics
{
    void LogToolCatalogFailure(string message, string source, Exception ex, IReadOnlyDictionary<string, object>? context);
}
