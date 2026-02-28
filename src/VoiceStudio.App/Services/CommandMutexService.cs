// Phase 4.1: Batch Job Mutual Exclusion
// Prevents conflicting commands from executing concurrently.
// When a batch job starts, conflicting commands are automatically disabled.
// Resolves GAP-B06, GAP-B11, GAP-B14.
// GAP-I13: Added lock order validation for deadlock prevention.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.Services;

/// <summary>
/// Interface for command mutual exclusion service.
/// </summary>
public interface ICommandMutexService
{
    /// <summary>
    /// Acquires a lock on the specified command groups.
    /// While the lock is held, commands in those groups return CanExecute = false.
    /// </summary>
    /// <param name="lockId">Unique identifier for this lock.</param>
    /// <param name="commandGroupIds">Command group IDs to lock.</param>
    /// <returns>Disposable that releases the lock when disposed.</returns>
    IDisposable AcquireLock(string lockId, params string[] commandGroupIds);

    /// <summary>
    /// Checks if a command group is currently locked.
    /// </summary>
    /// <param name="commandGroupId">The command group ID to check.</param>
    /// <returns>True if the group is locked, false otherwise.</returns>
    bool IsLocked(string commandGroupId);

    /// <summary>
    /// Gets all currently active locks.
    /// </summary>
    IReadOnlyCollection<string> ActiveLocks { get; }

    /// <summary>
    /// Gets all locked command groups.
    /// </summary>
    IReadOnlyCollection<string> LockedGroups { get; }

    /// <summary>
    /// Event raised when a lock is acquired.
    /// </summary>
    event EventHandler<MutexLockEventArgs>? LockAcquired;

    /// <summary>
    /// Event raised when a lock is released.
    /// </summary>
    event EventHandler<MutexLockEventArgs>? LockReleased;
}

/// <summary>
/// Event arguments for mutex lock events.
/// </summary>
public class MutexLockEventArgs : EventArgs
{
    public string LockId { get; }
    public IReadOnlyList<string> AffectedGroups { get; }

    public MutexLockEventArgs(string lockId, IReadOnlyList<string> affectedGroups)
    {
        LockId = lockId;
        AffectedGroups = affectedGroups;
    }
}

/// <summary>
/// Service that manages mutual exclusion for command groups.
/// </summary>
public class CommandMutexService : ICommandMutexService
{
    private readonly ConcurrentDictionary<string, LockEntry> _activeLocks = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _groupToLocks = new();
    private readonly object _lockSync = new();
    private readonly ICommandInvalidationService? _invalidationService;

    public event EventHandler<MutexLockEventArgs>? LockAcquired;
    public event EventHandler<MutexLockEventArgs>? LockReleased;

    public CommandMutexService(ICommandInvalidationService? invalidationService = null)
    {
        _invalidationService = invalidationService;
    }

    public IReadOnlyCollection<string> ActiveLocks =>
        _activeLocks.Keys.ToList().AsReadOnly();

    public IReadOnlyCollection<string> LockedGroups =>
        _groupToLocks.Where(kvp => kvp.Value.Count > 0)
                     .Select(kvp => kvp.Key)
                     .ToList()
                     .AsReadOnly();

    public IDisposable AcquireLock(string lockId, params string[] commandGroupIds)
    {
        if (string.IsNullOrEmpty(lockId))
            throw new ArgumentException("Lock ID cannot be null or empty", nameof(lockId));

        if (commandGroupIds == null || commandGroupIds.Length == 0)
            throw new ArgumentException("At least one command group ID must be specified", nameof(commandGroupIds));

        var groups = commandGroupIds.ToList();
        var entry = new LockEntry(lockId, groups);

        // GAP-I13: Validate lock ordering (no-op in release builds)
        // CommandMutexService is L7 in the lock hierarchy
        // See: docs/architecture/CONCURRENCY_GUIDE.md
        using var lockValidator = LockOrderValidator.AcquireLock(
            LockOrderValidator.Levels.CommandMutexService,
            $"CommandMutex:{lockId}");

        lock (_lockSync)
        {
            if (_activeLocks.ContainsKey(lockId))
            {
                Debug.WriteLine($"[CommandMutex] Lock '{lockId}' already exists, reusing");
                return new LockHandle(this, lockId);
            }

            _activeLocks[lockId] = entry;

            foreach (var groupId in groups)
            {
                if (!_groupToLocks.TryGetValue(groupId, out var locks))
                {
                    locks = new HashSet<string>();
                    _groupToLocks[groupId] = locks;
                }
                locks.Add(lockId);
            }
        }

        Debug.WriteLine($"[CommandMutex] Lock acquired: '{lockId}' on groups [{string.Join(", ", groups)}]");

        // Invalidate affected groups so buttons update
        InvalidateAffectedGroups(groups);

        LockAcquired?.Invoke(this, new MutexLockEventArgs(lockId, groups));

        return new LockHandle(this, lockId);
    }

    public bool IsLocked(string commandGroupId)
    {
        if (_groupToLocks.TryGetValue(commandGroupId, out var locks))
        {
            return locks.Count > 0;
        }
        return false;
    }

    private void ReleaseLock(string lockId)
    {
        LockEntry? entry;
        List<string>? affectedGroups = null;

        lock (_lockSync)
        {
            if (!_activeLocks.TryRemove(lockId, out entry))
            {
                Debug.WriteLine($"[CommandMutex] Lock '{lockId}' not found for release");
                return;
            }

            affectedGroups = entry.Groups;

            foreach (var groupId in affectedGroups)
            {
                if (_groupToLocks.TryGetValue(groupId, out var locks))
                {
                    locks.Remove(lockId);
                }
            }
        }

        Debug.WriteLine($"[CommandMutex] Lock released: '{lockId}'");

        // Invalidate affected groups so buttons update
        if (affectedGroups != null)
        {
            InvalidateAffectedGroups(affectedGroups);
            LockReleased?.Invoke(this, new MutexLockEventArgs(lockId, affectedGroups));
        }
    }

    private void InvalidateAffectedGroups(List<string> groups)
    {
        if (_invalidationService == null)
            return;

        foreach (var groupId in groups)
        {
            try
            {
                _invalidationService.InvalidateGroup(groupId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommandMutex] Failed to invalidate group '{groupId}': {ex.Message}");
            }
        }
    }

    private sealed class LockEntry
    {
        public string LockId { get; }
        public List<string> Groups { get; }
        public DateTime AcquiredAt { get; }

        public LockEntry(string lockId, List<string> groups)
        {
            LockId = lockId;
            Groups = groups;
            AcquiredAt = DateTime.UtcNow;
        }
    }

    private sealed class LockHandle : IDisposable
    {
        private readonly CommandMutexService _service;
        private readonly string _lockId;
        private bool _disposed;

        public LockHandle(CommandMutexService service, string lockId)
        {
            _service = service;
            _lockId = lockId;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _service.ReleaseLock(_lockId);
        }
    }
}
