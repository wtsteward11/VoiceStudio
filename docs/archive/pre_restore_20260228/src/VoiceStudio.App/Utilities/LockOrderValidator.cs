// GAP-I13: Lock Order Validator
// Debug-only utility to detect lock acquisition order violations.
// This validator has zero overhead in release builds.
//
// Lock Hierarchy (must acquire in ascending order):
// L1: AppStateStore._lock
// L2: EventAggregator._lock
// L3: SettingsService._semaphore
// L4: AudioPlayerService._playbackLock
// L5: WorkspaceService._rwLock
// L6: PanelStateService._panelLocks[id]
// L7: CommandMutexService._lockSync
//
// See: docs/architecture/CONCURRENCY_GUIDE.md

using System;
using VoiceStudio.App.Logging;
#if DEBUG
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
#endif

namespace VoiceStudio.App.Utilities;

/// <summary>
/// Debug-only validator that detects lock acquisition order violations.
/// In release builds, all methods are no-ops.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// #if DEBUG
/// using var _ = LockOrderValidator.AcquireLock(5, "WorkspaceService");
/// #endif
/// await _semaphore.WaitAsync(timeout);
/// </code>
/// </remarks>
public static class LockOrderValidator
{
    /// <summary>
    /// Lock level constants matching CONCURRENCY_GUIDE.md.
    /// </summary>
    public static class Levels
    {
        public const int AppStateStore = 1;
        public const int EventAggregator = 2;
        public const int SettingsService = 3;
        public const int AudioPlayerService = 4;
        public const int WorkspaceService = 5;
        public const int PanelStateService = 6;
        public const int CommandMutexService = 7;
    }

#if DEBUG
    /// <summary>
    /// Thread-local stack tracking currently held lock levels.
    /// </summary>
    private static readonly ThreadLocal<Stack<LockInfo>> _heldLocks = new(() => new Stack<LockInfo>());

    /// <summary>
    /// AsyncLocal for tracking locks in async contexts.
    /// </summary>
    private static readonly AsyncLocal<Stack<LockInfo>?> _asyncHeldLocks = new();

    /// <summary>
    /// Gets the current lock stack, preferring async context over thread-local.
    /// </summary>
    private static Stack<LockInfo> GetCurrentStack()
    {
        // Prefer async stack if available
        var asyncStack = _asyncHeldLocks.Value;
        if (asyncStack != null)
            return asyncStack;

        // Fall back to thread-local
        return _heldLocks.Value!;
    }
#endif

    /// <summary>
    /// Acquires a lock at the specified level and validates ordering.
    /// </summary>
    /// <param name="lockLevel">The lock level (1-7 per CONCURRENCY_GUIDE.md).</param>
    /// <param name="lockName">Human-readable name for error messages.</param>
    /// <returns>Disposable that releases the lock tracking when disposed.</returns>
    public static IDisposable AcquireLock(int lockLevel, string lockName)
    {
#if DEBUG
        if (lockLevel < 1 || lockLevel > 10)
            throw new ArgumentOutOfRangeException(nameof(lockLevel), "Lock level must be between 1 and 10");

        var stack = GetCurrentStack();

        // Check for lock order violation
        if (stack.Count > 0)
        {
            var held = stack.Peek();
            if (held.Level >= lockLevel)
            {
                var message = $"""
                    LOCK ORDER VIOLATION DETECTED!
                    
                    Attempting to acquire: {lockName} (L{lockLevel})
                    Currently holding: {held.Name} (L{held.Level})
                    
                    Rule: Always acquire locks in ascending order (L1 before L2, etc.)
                    
                    Current lock stack:
                    {FormatStack(stack)}
                    
                    See: docs/architecture/CONCURRENCY_GUIDE.md
                    """;

                Debug.Fail(message);

                // Also write to debug output for logging
                ErrorLogger.LogDebug($"[LockOrderValidator] {message}", "LockOrderValidator");
            }
        }

        // Push the new lock onto the stack
        var info = new LockInfo(lockLevel, lockName);
        stack.Push(info);

        ErrorLogger.LogDebug($"[LockOrderValidator] Acquired: {lockName} (L{lockLevel}), stack depth: {stack.Count}", "LockOrderValidator");

        return new LockReleaser(stack, info);
#else
        // No-op in release builds
        return NoOpDisposable.Instance;
#endif
    }

    /// <summary>
    /// Acquires a lock for async context. Use this for SemaphoreSlim and other async locks.
    /// </summary>
    /// <param name="lockLevel">The lock level.</param>
    /// <param name="lockName">Human-readable name for error messages.</param>
    /// <returns>Disposable that releases the lock tracking when disposed.</returns>
    public static IDisposable AcquireAsyncLock(int lockLevel, string lockName)
    {
#if DEBUG
        // Ensure async stack exists
        if (_asyncHeldLocks.Value == null)
            _asyncHeldLocks.Value = new Stack<LockInfo>();

        return AcquireLock(lockLevel, lockName);
#else
        // No-op in release builds
        return NoOpDisposable.Instance;
#endif
    }

    /// <summary>
    /// Gets the current lock stack depth (for testing/diagnostics).
    /// </summary>
    public static int CurrentStackDepth
    {
        get
        {
#if DEBUG
            return GetCurrentStack().Count;
#else
            return 0;
#endif
        }
    }

    /// <summary>
    /// Checks if a lock at the given level is currently held.
    /// </summary>
    public static bool IsLevelHeld(int lockLevel)
    {
#if DEBUG
        var stack = GetCurrentStack();
        foreach (var info in stack)
        {
            if (info.Level == lockLevel)
                return true;
        }
#endif
        return false;
    }

    /// <summary>
    /// Clears the current thread's lock stack (for testing only).
    /// </summary>
    public static void Reset()
    {
#if DEBUG
        _heldLocks.Value?.Clear();
        _asyncHeldLocks.Value?.Clear();
#endif
    }

#if DEBUG
    private static string FormatStack(Stack<LockInfo> stack)
    {
        var lines = new List<string>();
        var temp = new List<LockInfo>(stack);
        temp.Reverse();
        foreach (var info in temp)
        {
            lines.Add($"  - L{info.Level}: {info.Name}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private readonly struct LockInfo
    {
        public int Level { get; }
        public string Name { get; }

        public LockInfo(int level, string name)
        {
            Level = level;
            Name = name;
        }
    }

    private sealed class LockReleaser : IDisposable
    {
        private readonly Stack<LockInfo> _stack;
        private readonly LockInfo _info;
        private bool _disposed;

        public LockReleaser(Stack<LockInfo> stack, LockInfo info)
        {
            _stack = stack;
            _info = info;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_stack.Count > 0)
            {
                var top = _stack.Peek();
                if (top.Level == _info.Level && top.Name == _info.Name)
                {
                    _stack.Pop();
                    ErrorLogger.LogDebug($"[LockOrderValidator] Released: {_info.Name} (L{_info.Level}), stack depth: {_stack.Count}", "LockOrderValidator");
                }
                else
                {
                    Debug.Fail($"Lock release mismatch: Expected {_info.Name} (L{_info.Level}), found {top.Name} (L{top.Level})");
                }
            }
            else
            {
                Debug.Fail($"Attempted to release {_info.Name} (L{_info.Level}) but lock stack is empty");
            }
        }
    }
#endif

    /// <summary>
    /// No-op disposable for release builds.
    /// </summary>
    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        private NoOpDisposable() { }
        public void Dispose() { }
    }
}
