// Phase 4.3: Command Queueing for Busy State
// When a command cannot execute because the system is busy,
// queue it for automatic execution when the system becomes idle.
// Resolves GAP-B12.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services;

/// <summary>
/// Interface for command queue service.
/// </summary>
public interface ICommandQueueService
{
    /// <summary>
    /// Enqueues a command for later execution if the system is busy.
    /// </summary>
    /// <param name="commandId">The command ID to enqueue.</param>
    /// <param name="parameter">Optional parameter for the command.</param>
    void EnqueueIfBusy(string commandId, object? parameter = null);

    /// <summary>
    /// Processes all queued commands.
    /// Called when system transitions from busy -> idle.
    /// </summary>
    Task ProcessQueueAsync();

    /// <summary>
    /// Clears all queued commands.
    /// </summary>
    void ClearQueue();

    /// <summary>
    /// Gets the current queue depth.
    /// </summary>
    int QueueDepth { get; }

    /// <summary>
    /// Gets all queued command IDs (for diagnostics).
    /// </summary>
    IReadOnlyList<string> GetQueuedCommands();

    /// <summary>
    /// Event raised when a command is enqueued.
    /// </summary>
    event EventHandler<CommandQueueEventArgs>? CommandEnqueued;

    /// <summary>
    /// Event raised when a command is dequeued and executed.
    /// </summary>
    event EventHandler<CommandQueueEventArgs>? CommandDequeued;
}

/// <summary>
/// Event arguments for command queue events.
/// </summary>
public class CommandQueueEventArgs : EventArgs
{
    public string CommandId { get; }
    public object? Parameter { get; }
    public int QueueDepth { get; }

    public CommandQueueEventArgs(string commandId, object? parameter, int queueDepth)
    {
        CommandId = commandId;
        Parameter = parameter;
        QueueDepth = queueDepth;
    }
}

/// <summary>
/// Service that manages command queueing for busy state.
/// </summary>
public class CommandQueueService : ICommandQueueService
{
    private readonly ConcurrentQueue<QueueEntry> _queue = new();
    private readonly IUnifiedCommandRegistry? _commandRegistry;
    private readonly ICommandMutexService? _mutexService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly object _processLock = new();
    private bool _isProcessing;
    private const int MaxQueueSize = 100;

    public event EventHandler<CommandQueueEventArgs>? CommandEnqueued;
    public event EventHandler<CommandQueueEventArgs>? CommandDequeued;

    public int QueueDepth => _queue.Count;

    public CommandQueueService(
        IUnifiedCommandRegistry? commandRegistry = null,
        ICommandMutexService? mutexService = null,
        DispatcherQueue? dispatcherQueue = null)
    {
        _commandRegistry = commandRegistry;
        _mutexService = mutexService;
        _dispatcherQueue = dispatcherQueue;
    }

    public void EnqueueIfBusy(string commandId, object? parameter = null)
    {
        if (string.IsNullOrEmpty(commandId))
            return;

        // Check if we should queue (system is busy)
        if (!IsSystemBusy(commandId))
        {
            ErrorLogger.LogDebug($"System not busy, skipping queue for '{commandId}'", "CommandQueue");
            return;
        }

        // Prevent queue overflow
        if (_queue.Count >= MaxQueueSize)
        {
            ErrorLogger.LogWarning($"Queue full ({MaxQueueSize}), dropping '{commandId}'", "CommandQueue");
            return;
        }

        // Don't queue duplicate commands
        if (_queue.Any(e => e.CommandId == commandId))
        {
            ErrorLogger.LogDebug($"Command '{commandId}' already queued", "CommandQueue");
            return;
        }

        var entry = new QueueEntry(commandId, parameter);
        _queue.Enqueue(entry);

        ErrorLogger.LogDebug($"Enqueued '{commandId}', depth={_queue.Count}", "CommandQueue");

        CommandEnqueued?.Invoke(this, new CommandQueueEventArgs(commandId, parameter, _queue.Count));
    }

    public async Task ProcessQueueAsync()
    {
        lock (_processLock)
        {
            if (_isProcessing)
            {
                ErrorLogger.LogDebug("Already processing, skipping", "CommandQueue");
                return;
            }
            _isProcessing = true;
        }

        try
        {
            ErrorLogger.LogDebug($"Processing queue, depth={_queue.Count}", "CommandQueue");

            while (_queue.TryDequeue(out var entry))
            {
                try
                {
                    await ExecuteCommandAsync(entry);
                    CommandDequeued?.Invoke(this, new CommandQueueEventArgs(
                        entry.CommandId, entry.Parameter, _queue.Count));
                }
                catch (Exception ex)
                {
                    ErrorLogger.LogWarning($"Failed to execute '{entry.CommandId}': {ex.Message}", "CommandQueue");
                }
            }

            ErrorLogger.LogDebug("Queue processing complete", "CommandQueue");
        }
        finally
        {
            lock (_processLock)
            {
                _isProcessing = false;
            }
        }
    }

    public void ClearQueue()
    {
        var cleared = 0;
        while (_queue.TryDequeue(out _))
        {
            cleared++;
        }

        ErrorLogger.LogDebug($"Cleared {cleared} queued commands", "CommandQueue");
    }

    public IReadOnlyList<string> GetQueuedCommands()
    {
        return _queue.Select(e => e.CommandId).ToList().AsReadOnly();
    }

    private bool IsSystemBusy(string commandId)
    {
        // Check if any relevant command groups are locked
        if (_mutexService != null)
        {
            // Check common groups that would indicate system is busy
            var busyGroups = new[] { "batch", "synthesis", "training" };
            foreach (var group in busyGroups)
            {
                if (_mutexService.IsLocked(group))
                    return true;
            }
        }

        // Check command's CanExecute via registry
        if (_commandRegistry != null)
        {
            var descriptor = _commandRegistry.GetCommand(commandId);
            if (descriptor != null && !_commandRegistry.CanExecute(commandId, null))
            {
                return true;
            }
        }

        return false;
    }

    private async Task ExecuteCommandAsync(QueueEntry entry)
    {
        if (_commandRegistry == null)
        {
            ErrorLogger.LogWarning($"No registry, cannot execute '{entry.CommandId}'", "CommandQueue");
            return;
        }

        var descriptor = _commandRegistry.GetCommand(entry.CommandId);
        if (descriptor == null)
        {
            ErrorLogger.LogWarning($"Command '{entry.CommandId}' not found", "CommandQueue");
            return;
        }

        if (!_commandRegistry.CanExecute(entry.CommandId, entry.Parameter))
        {
            ErrorLogger.LogDebug($"Command '{entry.CommandId}' cannot execute", "CommandQueue");
            return;
        }

        // Execute via registry (handles UI thread marshalling if needed)
        try
        {
            await _commandRegistry.ExecuteAsync(entry.CommandId, entry.Parameter);
            ErrorLogger.LogDebug($"Executed '{entry.CommandId}'", "CommandQueue");
        }
        catch (Exception ex)
        {
            ErrorLogger.LogWarning($"Failed to execute '{entry.CommandId}': {ex.Message}", "CommandQueue");
            throw;
        }
    }

    private sealed class QueueEntry
    {
        public string CommandId { get; }
        public object? Parameter { get; }
        public DateTime EnqueuedAt { get; }

        public QueueEntry(string commandId, object? parameter)
        {
            CommandId = commandId;
            Parameter = parameter;
            EnqueuedAt = DateTime.UtcNow;
        }
    }
}
