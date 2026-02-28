using System;
using System.Collections.Generic;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace VoiceStudio.App.Core.Commands
{
    /// <summary>
    /// Named collection of commands that can be invalidated, enabled, or disabled together.
    /// Resolves GAP-B10 (command groups) and supports bulk operations.
    /// </summary>
    public sealed class CommandGroup
    {
        private readonly List<WeakReference<IRelayCommand>> _commands = new();
        private readonly object _lock = new();

        /// <summary>
        /// Unique identifier for the group (e.g., "profiles", "playback", "synthesis").
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Human-readable name for diagnostics and UI.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// When false, callers can use this to disable all commands in the group
        /// (e.g. ViewModels can check this in their CanExecute).
        /// </summary>
        public bool IsEnabled { get; private set; } = true;

        public CommandGroup(string id, string? displayName = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = displayName ?? id;
        }

        /// <summary>
        /// Adds a command to the group.
        /// </summary>
        public void Add(IRelayCommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            lock (_lock)
            {
                _commands.Add(new WeakReference<IRelayCommand>(command));
            }
        }

        /// <summary>
        /// Removes a command from the group (by reference equality).
        /// </summary>
        public void Remove(IRelayCommand command)
        {
            if (command == null) return;

            lock (_lock)
            {
                for (var i = _commands.Count - 1; i >= 0; i--)
                {
                    if (_commands[i].TryGetTarget(out var target) && target == command)
                    {
                        _commands.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Raises CanExecuteChanged on all commands in the group so the UI re-evaluates.
        /// Prunes dead weak references.
        /// </summary>
        public void InvalidateAll()
        {
            List<WeakReference<IRelayCommand>> copy;

            lock (_lock)
            {
                copy = new List<WeakReference<IRelayCommand>>(_commands);
            }

            var toRemove = new List<WeakReference<IRelayCommand>>();

            foreach (var weak in copy)
            {
                if (weak.TryGetTarget(out var command))
                {
                    try
                    {
                        command.NotifyCanExecuteChanged();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CommandGroup] NotifyCanExecuteChanged failed: {ex.Message}");
                    }
                }
                else
                {
                    toRemove.Add(weak);
                }
            }

            if (toRemove.Count > 0)
            {
                lock (_lock)
                {
                    foreach (var dead in toRemove)
                    {
                        _commands.Remove(dead);
                    }
                }
            }
        }

        /// <summary>
        /// Sets the group-level enabled flag. When false, callers (e.g. CanExecute logic)
        /// should treat commands in this group as disabled. Call InvalidateAll() after
        /// to refresh UI.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled) return;

            IsEnabled = enabled;
            InvalidateAll();
        }

        /// <summary>
        /// Returns the current number of commands in the group (alive references only).
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    var count = 0;
                    foreach (var weak in _commands)
                    {
                        if (weak.TryGetTarget(out _)) count++;
                    }

                    return count;
                }
            }
        }
    }
}
