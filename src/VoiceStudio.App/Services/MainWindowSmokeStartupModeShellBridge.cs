// VoiceStudio - GAP-008 Slice 39: smoke / safe-startup mode probes (bounded shell).
// Extracted from MainWindow.xaml.cs per VOICESTUDIO_BOUNDED_GAP008_SLICE39_MAINWINDOW_SMOKE_STARTUP_MODE_SHELL.md

using System;

namespace VoiceStudio.App.Services;

/// <summary>
/// Centralizes Gate-C smoke / UI-smoke command-line and environment probes and safe-startup
/// (<c>VOICESTUDIO_SAFE_STARTUP</c>) classification. The main window holds an instance for <c>Func&lt;bool&gt;</c>
/// delegates; <see cref="ShellNavigationCoordinator"/> uses <see cref="EvaluateSafeStartup"/> to avoid a duplicate safe-mode implementation.
/// </summary>
public sealed class MainWindowSmokeStartupModeShellBridge
{
    /// <summary>
    /// Same rules as the former <c>MainWindow.IsSafeStartupMode</c> static (single source of truth for navigation safe path).
    /// </summary>
    public static bool EvaluateSafeStartup()
    {
        var v = Environment.GetEnvironmentVariable("VOICESTUDIO_SAFE_STARTUP");
        return string.Equals(v, "1", StringComparison.Ordinal) || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc cref="EvaluateSafeStartup"/>
    public bool IsSafeStartupMode() => EvaluateSafeStartup();

    /// <summary>
    /// Gate-C smoke / UI-smoke mode: env vars, command line, and argv scan (same as former <c>MainWindow.IsGateCSmokeMode</c>).
    /// </summary>
    public bool IsGateCSmokeMode() => EvaluateGateCSmoke();

    /// <summary>Static entry for callers that need the same Gate-C rules without an instance.</summary>
    public static bool EvaluateGateCSmoke()
    {
        try
        {
            static bool IsTruthy(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            if (IsTruthy(Environment.GetEnvironmentVariable("VOICE_STUDIO_SMOKE_EXIT"))
                || IsTruthy(Environment.GetEnvironmentVariable("VOICE_STUDIO_SMOKE_UI")))
            {
                return true;
            }

            var raw = Environment.CommandLine ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(raw)
                && (raw.IndexOf("--smoke", StringComparison.OrdinalIgnoreCase) >= 0
                    || raw.IndexOf("--ui-smoke", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg.Equals("--smoke-exit", StringComparison.OrdinalIgnoreCase)
                    || arg.Equals("--smoke-ui", StringComparison.OrdinalIgnoreCase)
                    || arg.Equals("--ui-smoke", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
