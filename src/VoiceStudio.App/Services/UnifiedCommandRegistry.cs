using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using VoiceStudio.App.Core.Commands;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Centralized command registry that manages all application commands.
    /// Provides registration, execution, status tracking, and keyboard shortcut integration.
    /// </summary>
    public sealed class UnifiedCommandRegistry : IUnifiedCommandRegistry
    {
        private readonly ConcurrentDictionary<string, CommandEntry> _commands = new();
        private readonly ConcurrentDictionary<string, RegistryRelayCommand> _commandWrappers = new();
        private readonly KeyboardShortcutService? _shortcutService;
        private readonly object _lock = new();

        // GAP-B12: Command queue service for busy-state handling
        private ICommandQueueService? _queueService;
        private volatile bool _isBusy;

        /// <summary>
        /// Gets or sets whether the registry is in "busy" mode.
        /// When busy, non-essential commands are queued instead of executed immediately.
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    Debug.WriteLine($"[CommandRegistry] Busy state changed: {value}");
                }
            }
        }

        /// <summary>
        /// Sets the queue service for busy-state command queueing.
        /// GAP-B12: Commands are queued when IsBusy is true and BypassBusy is false.
        /// </summary>
        public void SetQueueService(ICommandQueueService queueService)
        {
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        }

        public event EventHandler<CommandExecutedEventArgs>? CommandExecuted;
        public event EventHandler<CommandFailedEventArgs>? CommandFailed;
        public event EventHandler<CommandDescriptor>? CommandRegistered;
        public event EventHandler<string>? CommandUnregistered;

        public UnifiedCommandRegistry(KeyboardShortcutService? shortcutService = null)
        {
            _shortcutService = shortcutService;
        }

        #region Registration

        public void Register(CommandDescriptor descriptor, ISyncCommandHandler handler)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (string.IsNullOrWhiteSpace(descriptor.Id))
                throw new ArgumentException("Command ID cannot be empty", nameof(descriptor));

            var entry = new CommandEntry
            {
                Descriptor = descriptor,
                State = new CommandRuntimeState { Descriptor = descriptor, Status = CommandStatus.Working },
                SyncHandler = handler
            };

            RegisterInternal(entry);
        }

        public void Register(CommandDescriptor descriptor, IAsyncCommandHandler handler)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (string.IsNullOrWhiteSpace(descriptor.Id))
                throw new ArgumentException("Command ID cannot be empty", nameof(descriptor));

            var entry = new CommandEntry
            {
                Descriptor = descriptor,
                State = new CommandRuntimeState { Descriptor = descriptor, Status = CommandStatus.Working },
                AsyncHandler = handler
            };

            RegisterInternal(entry);
        }

        public void Register(CommandDescriptor descriptor, Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (execute == null) throw new ArgumentNullException(nameof(execute));

            var handler = new DelegateCommandHandler(execute, canExecute);
            Register(descriptor, handler);
        }

        public void Register(CommandDescriptor descriptor, Func<object?, CancellationToken, Task> executeAsync, Func<object?, bool>? canExecute = null)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (executeAsync == null) throw new ArgumentNullException(nameof(executeAsync));

            var handler = new AsyncDelegateCommandHandler(executeAsync, canExecute);
            Register(descriptor, handler);
        }

        private void RegisterInternal(CommandEntry entry)
        {
            var commandId = entry.Descriptor.Id;

            // GAP-B19: Validate command ID against known IDs
            if (!CommandIds.IsKnown(commandId))
            {
                Debug.WriteLine($"[CommandRegistry] WARNING: Unregistered command ID: {commandId}. " +
                    $"Add it to CommandIds.cs for compile-time safety.");
            }

            _commands[commandId] = entry;
            _commandWrappers[commandId] = new RegistryRelayCommand(this, commandId);

            // Register with keyboard shortcut service if shortcut is specified
            if (_shortcutService != null && !string.IsNullOrEmpty(entry.Descriptor.KeyboardShortcut))
            {
                _shortcutService.RegisterHandler(commandId, () =>
                {
                    _ = ExecuteAsync(commandId);
                });
            }

            Debug.WriteLine($"[CommandRegistry] Registered: {commandId}");
            CommandRegistered?.Invoke(this, entry.Descriptor);
        }

        public bool Unregister(string commandId)
        {
            if (string.IsNullOrWhiteSpace(commandId)) return false;

            var removed = _commands.TryRemove(commandId, out _);
            _commandWrappers.TryRemove(commandId, out _);

            if (removed)
            {
                Debug.WriteLine($"[CommandRegistry] Unregistered: {commandId}");
                CommandUnregistered?.Invoke(this, commandId);
            }

            return removed;
        }

        #endregion

        #region Execution

        private static void FileLog(string msg)
        {
            var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceStudio", "import_debug.log");
            // ALLOWED: empty catch - Best effort debug logging, failure is acceptable
            try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}"); } catch { }
        }

        public async Task ExecuteAsync(string commandId, object? parameter = null, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"[CommandRegistry] ExecuteAsync called for: {commandId}");
            FileLog($"[CommandRegistry] ExecuteAsync called for: {commandId}");
            if (!_commands.TryGetValue(commandId, out var entry))
            {
                Debug.WriteLine($"[CommandRegistry] Command not found: {commandId}");
                FileLog($"[CommandRegistry] Command not found: {commandId}");
                FileLog($"[CommandRegistry] Registered commands: {string.Join(", ", _commands.Keys)}");
                throw new InvalidOperationException($"Command not registered: {commandId}");
            }

            Debug.WriteLine($"[CommandRegistry] Found command: {commandId}, IsEnabled: {entry.Descriptor.IsEnabled}");
            FileLog($"[CommandRegistry] Found command: {commandId}, IsEnabled: {entry.Descriptor.IsEnabled}");
            if (!entry.Descriptor.IsEnabled)
            {
                Debug.WriteLine($"[CommandRegistry] Command disabled: {commandId}");
                FileLog($"[CommandRegistry] Command disabled: {commandId}");
                return;
            }

            // GAP-B12: Queue command if busy and command doesn't bypass busy state
            if (_isBusy && !entry.Descriptor.BypassBusy && _queueService != null)
            {
                Debug.WriteLine($"[CommandRegistry] Busy - queueing command: {commandId}");
                FileLog($"[CommandRegistry] Busy - queueing command: {commandId}");
                _queueService.EnqueueIfBusy(commandId, parameter);
                return;
            }

            Debug.WriteLine($"[CommandRegistry] Executing handler for: {commandId}");
            FileLog($"[CommandRegistry] Executing handler for: {commandId}");

            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (entry.AsyncHandler != null)
                {
                    await entry.AsyncHandler.ExecuteAsync(parameter, cancellationToken);
                }
                else if (entry.SyncHandler != null)
                {
                    entry.SyncHandler.Execute(parameter);
                }
                else
                {
                    throw new InvalidOperationException($"No handler registered for command: {commandId}");
                }

                stopwatch.Stop();

                // Update state
                entry.State.LastExecuted = DateTime.UtcNow;
                entry.State.SuccessCount++;
                entry.State.Status = CommandStatus.Working;
                entry.State.LastError = null;

                // Update average execution time
                var totalExecTime = entry.State.AverageExecutionMs * (entry.State.SuccessCount - 1) + stopwatch.ElapsedMilliseconds;
                entry.State.AverageExecutionMs = totalExecTime / entry.State.SuccessCount;

                Debug.WriteLine($"[CommandRegistry] Executed: {commandId} ({stopwatch.ElapsedMilliseconds}ms)");

                CommandExecuted?.Invoke(this, new CommandExecutedEventArgs
                {
                    CommandId = commandId,
                    Parameter = parameter,
                    Duration = stopwatch.Elapsed
                });
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Update state
                entry.State.FailureCount++;
                entry.State.LastError = ex.Message;
                entry.State.Status = CommandStatus.Broken;

                Debug.WriteLine($"[CommandRegistry] Failed: {commandId} - {ex.Message}");

                CommandFailed?.Invoke(this, new CommandFailedEventArgs
                {
                    CommandId = commandId,
                    Parameter = parameter,
                    Exception = ex
                });

                throw;
            }
        }

        public bool CanExecute(string commandId, object? parameter = null)
        {
            if (!_commands.TryGetValue(commandId, out var entry))
                return false;

            if (!entry.Descriptor.IsEnabled)
                return false;

            try
            {
                if (entry.AsyncHandler != null)
                    return entry.AsyncHandler.CanExecute(parameter);
                if (entry.SyncHandler != null)
                    return entry.SyncHandler.CanExecute(parameter);
                return false;
            }
            catch
            {
                return false;
            }
        }

        public ICommand? GetCommand(string commandId)
        {
            return _commandWrappers.TryGetValue(commandId, out var command) ? command : null;
        }

        #endregion

        #region Discovery

        public CommandDescriptor? GetDescriptor(string commandId)
        {
            return _commands.TryGetValue(commandId, out var entry) ? entry.Descriptor : null;
        }

        public IReadOnlyList<CommandDescriptor> GetAllCommands()
        {
            return _commands.Values.Select(e => e.Descriptor).ToList().AsReadOnly();
        }

        public IReadOnlyList<CommandDescriptor> GetCommandsByCategory(string category)
        {
            return _commands.Values
                .Where(e => string.Equals(e.Descriptor.Category, category, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Descriptor)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<string> GetCategories()
        {
            return _commands.Values
                .Select(e => e.Descriptor.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList()
                .AsReadOnly();
        }

        public bool IsRegistered(string commandId)
        {
            return _commands.ContainsKey(commandId);
        }

        #endregion

        #region Status Tracking

        public CommandRuntimeState? GetState(string commandId)
        {
            return _commands.TryGetValue(commandId, out var entry) ? entry.State : null;
        }

        public CommandStatus GetStatus(string commandId)
        {
            return _commands.TryGetValue(commandId, out var entry)
                ? entry.State.Status
                : CommandStatus.Unknown;
        }

        public IReadOnlyDictionary<string, CommandStatus> GetHealthReport()
        {
            return _commands.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.State.Status);
        }

        public IReadOnlyDictionary<CommandStatus, int> GetStatusCounts()
        {
            return _commands.Values
                .GroupBy(e => e.State.Status)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>
        /// Generates a formatted health report string.
        /// </summary>
        public string GetHealthReportString()
        {
            var counts = GetStatusCounts();
            var total = _commands.Count;
            var working = counts.GetValueOrDefault(CommandStatus.Working, 0);
            var broken = counts.GetValueOrDefault(CommandStatus.Broken, 0);
            var disabled = counts.GetValueOrDefault(CommandStatus.Disabled, 0);
            var unknown = counts.GetValueOrDefault(CommandStatus.Unknown, 0);

            var report = new System.Text.StringBuilder();
            report.AppendLine("=== Command Health Report ===");
            report.AppendLine($"Total Commands: {total}");
            report.AppendLine($"Working: {working} ({(total > 0 ? working * 100 / total : 0)}%)");
            report.AppendLine($"Broken: {broken} ({(total > 0 ? broken * 100 / total : 0)}%)");

            if (broken > 0)
            {
                var brokenCommands = _commands.Values
                    .Where(e => e.State.Status == CommandStatus.Broken)
                    .Take(10);

                foreach (var cmd in brokenCommands)
                {
                    report.AppendLine($"  - {cmd.Descriptor.Id}: {cmd.State.LastError ?? "No handler"}");
                }
            }

            report.AppendLine($"Disabled: {disabled}");
            report.AppendLine($"Unknown: {unknown}");

            return report.ToString();
        }

        #endregion

        #region Internal Types

        private sealed class CommandEntry
        {
            public required CommandDescriptor Descriptor { get; init; }
            public required CommandRuntimeState State { get; init; }
            public ISyncCommandHandler? SyncHandler { get; init; }
            public IAsyncCommandHandler? AsyncHandler { get; init; }
        }

        private sealed class DelegateCommandHandler : ISyncCommandHandler
        {
            private readonly Action<object?> _execute;
            private readonly Func<object?, bool>? _canExecute;

            public DelegateCommandHandler(Action<object?> execute, Func<object?, bool>? canExecute)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
            public void Execute(object? parameter) => _execute(parameter);
        }

        private sealed class AsyncDelegateCommandHandler : IAsyncCommandHandler
        {
            private readonly Func<object?, CancellationToken, Task> _execute;
            private readonly Func<object?, bool>? _canExecute;

            public AsyncDelegateCommandHandler(Func<object?, CancellationToken, Task> execute, Func<object?, bool>? canExecute)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
            public Task ExecuteAsync(object? parameter, CancellationToken cancellationToken) => _execute(parameter, cancellationToken);
        }

        /// <summary>
        /// ICommand wrapper that delegates to the registry.
        /// </summary>
        private sealed class RegistryRelayCommand : ICommand
        {
            private readonly UnifiedCommandRegistry _registry;
            private readonly string _commandId;

            public RegistryRelayCommand(UnifiedCommandRegistry registry, string commandId)
            {
                _registry = registry;
                _commandId = commandId;
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => _registry.CanExecute(_commandId, parameter);

            public void Execute(object? parameter)
            {
                _ = _registry.ExecuteAsync(_commandId, parameter);
            }

            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
