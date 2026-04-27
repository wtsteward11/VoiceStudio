using System;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 40: isolates the WinUI window <c>Activated</c> handler try/catch + warning log shell.
/// Slice 11 startup welcome activation bridge remains owner of <c>HandleActivatedAsync</c> orchestration.
/// </summary>
public sealed class MainWindowWindowActivatedLoggingShellBridge
{
    private const string LogScope = "MainWindow.MainWindow_Activated";

    public async Task RunActivatedAsync(Func<Task> innerAsync)
    {
        ArgumentNullException.ThrowIfNull(innerAsync);

        try
        {
            await innerAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorLogger.LogWarning($"Activated handler failed: {ex.Message}", LogScope);
        }
    }
}
