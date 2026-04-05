using System;

namespace VoiceStudio.App.Services;

/// <summary>
/// Project-scoped dirty flag for session autosave (GOV-VOICESTUDIO-SESSION-AUTOSAVE-01).
/// Suppress notifications during bulk load/open to avoid false dirty transitions.
/// </summary>
public interface IProjectSessionDirtyState
{
    /// <summary>True when timeline/mixer or other project-scoped edits need a unified save.</summary>
    bool IsProjectDirty { get; }

    /// <summary>Raised when <see cref="IsProjectDirty"/> changes.</summary>
    event EventHandler? DirtyStateChanged;

    void MarkProjectDirty(string reason);

    void MarkProjectClean();

    /// <summary>Increment suppression depth (e.g. while loading projects list). Pair with <see cref="ExitSuppressDirtyNotifications"/>.</summary>
    void EnterSuppressDirtyNotifications();

    /// <summary>Decrement suppression depth.</summary>
    void ExitSuppressDirtyNotifications();
}
