using System;
using System.Threading;

namespace VoiceStudio.App.Services;

/// <summary>
/// Thread-safe project dirty flag with re-entrant suppression for load/open operations.
/// </summary>
public sealed class ProjectSessionDirtyState : IProjectSessionDirtyState
{
    private int _suppressDepth;
    private volatile bool _isDirty;

    public bool IsProjectDirty => _isDirty;

    public event EventHandler? DirtyStateChanged;

    public void EnterSuppressDirtyNotifications()
    {
        Interlocked.Increment(ref _suppressDepth);
    }

    public void ExitSuppressDirtyNotifications()
    {
        var v = Interlocked.Decrement(ref _suppressDepth);
        if (v < 0)
            Interlocked.Exchange(ref _suppressDepth, 0);
    }

    public void MarkProjectDirty(string reason)
    {
        if (Interlocked.CompareExchange(ref _suppressDepth, 0, 0) != 0)
            return;

        _ = reason;
        var was = _isDirty;
        _isDirty = true;
        if (!was)
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkProjectClean()
    {
        var was = _isDirty;
        _isDirty = false;
        if (was)
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
