using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-034: Windows App Notifications for batch / training / export terminal outcomes with in-process dedupe.
/// </summary>
public sealed class CompletionOsNotificationService : ICompletionOsNotificationService
{
    private readonly object _gate = new();
    private readonly HashSet<string> _publishedKeys = new(StringComparer.Ordinal);
    private readonly Action<string, string>? _showForTests;

    /// <summary>Production ctor. <paramref name="showForTests"/> overrides Windows show for unit tests.</summary>
    public CompletionOsNotificationService(Action<string, string>? showForTests = null)
    {
        _showForTests = showForTests;
    }

    /// <inheritdoc />
    public void TryNotifyTerminalCompletion(
        CompletionOsNotificationCategory category,
        string operationId,
        bool success,
        string title,
        string body)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            Debug.WriteLine("[CompletionOsNotification] Skip: empty operationId");
            return;
        }

        var key = $"{(int)category}\u001f{operationId}\u001f{(success ? 1 : 0)}";
        lock (_gate)
        {
            if (!_publishedKeys.Add(key))
            {
                Debug.WriteLine($"[CompletionOsNotification] Deduped: {key}");
                return;
            }
        }

        try
        {
            if (_showForTests != null)
            {
                _showForTests(title, body);
                return;
            }

            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(body)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CompletionOsNotification] Show failed: {ex.Message}");
        }
    }
}
