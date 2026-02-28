using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.State;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Service that subscribes to EventAggregator and AppStateStore and automatically
    /// invalidates command CanExecute state for affected groups. Coalesces rapid
    /// invalidations and marshals to UI thread.
    /// </summary>
    public interface ICommandInvalidationService
    {
        /// <summary>
        /// Registers a rule: when an event of type TEvent is published, invalidate the given command group(s).
        /// </summary>
        void RegisterInvalidationRule<TEvent>(string commandGroupId, Func<TEvent, bool>? filter = null) where TEvent : class;

        /// <summary>
        /// Registers a rule: when an event of type TEvent is published, invalidate all given command groups.
        /// </summary>
        void RegisterInvalidationRule<TEvent>(Func<TEvent, bool>? filter, params string[] commandGroupIds) where TEvent : class;

        /// <summary>
        /// Invalidates all commands in the specified group (raises CanExecuteChanged on UI thread).
        /// </summary>
        void InvalidateGroup(string groupId);

        /// <summary>
        /// Invalidates all registered commands in all groups.
        /// </summary>
        void InvalidateAll();

        /// <summary>
        /// Adds a command to a group. The command will be invalidated when the group is invalidated.
        /// </summary>
        void AddToGroup(string groupId, IRelayCommand command);

        /// <summary>
        /// Removes a command from a group.
        /// </summary>
        void RemoveFromGroup(string groupId, IRelayCommand command);
    }

    /// <summary>
    /// Implementation of automatic command re-evaluation: subscribes to events and state changes,
    /// debounces invalidations (16ms), and marshals NotifyCanExecuteChanged to the UI thread.
    /// </summary>
    public sealed class CommandInvalidationService : ICommandInvalidationService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly DispatcherQueue? _dispatcherQueue;
        private readonly Dictionary<string, List<WeakReference<IRelayCommand>>> _groups = new();
        private readonly List<object> _subscriptionTokens = new();
        private readonly object _groupsLock = new();
        private readonly object _pendingLock = new();
        private HashSet<string> _pendingGroups = new();
        private DispatcherQueueTimer? _debounceTimer;
        private const int DebounceMs = 16;

        private CommandInvalidationService(IEventAggregator eventAggregator, IAppStateStore? appStateStore)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            try
            {
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            }
            catch
            {
                _dispatcherQueue = null;
            }

            if (_dispatcherQueue != null)
            {
                _debounceTimer = _dispatcherQueue.CreateTimer();
                _debounceTimer.Interval = TimeSpan.FromMilliseconds(DebounceMs);
                _debounceTimer.Tick += (_, _) => FlushPendingInvalidations();
            }

            if (appStateStore != null)
            {
                appStateStore.StateChanged += (_, _) => InvalidateAllDebounced();
            }
        }

        /// <summary>
        /// Factory for DI registration. Optional AppStateStore subscribes to global state changes.
        /// </summary>
        public static CommandInvalidationService Create(IEventAggregator eventAggregator, IAppStateStore? appStateStore = null)
        {
            return new CommandInvalidationService(eventAggregator, appStateStore);
        }

        /// <inheritdoc />
        public void RegisterInvalidationRule<TEvent>(string commandGroupId, Func<TEvent, bool>? filter = null) where TEvent : class
        {
            RegisterInvalidationRule<TEvent>(filter, commandGroupId);
        }

        /// <inheritdoc />
        public void RegisterInvalidationRule<TEvent>(Func<TEvent, bool>? filter, params string[] commandGroupIds) where TEvent : class
        {
            if (commandGroupIds == null || commandGroupIds.Length == 0)
                return;
            var token = _eventAggregator.Subscribe<TEvent>(evt =>
            {
                if (filter != null && !filter(evt))
                    return;
                foreach (var groupId in commandGroupIds)
                    InvalidateGroupDebounced(groupId);
            });
            lock (_subscriptionTokens)
            {
                _subscriptionTokens.Add(token);
            }
        }

        /// <inheritdoc />
        public void InvalidateGroup(string groupId)
        {
            if (string.IsNullOrEmpty(groupId))
                return;

            if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
            {
                _dispatcherQueue.TryEnqueue(() => InvalidateGroupCore(groupId));
            }
            else
            {
                InvalidateGroupCore(groupId);
            }
        }

        /// <inheritdoc />
        public void InvalidateAll()
        {
            List<string> groupIds;
            lock (_groupsLock)
            {
                groupIds = _groups.Keys.ToList();
            }
            foreach (var id in groupIds)
                InvalidateGroup(id);
        }

        /// <inheritdoc />
        public void AddToGroup(string groupId, IRelayCommand command)
        {
            if (string.IsNullOrEmpty(groupId))
                throw new ArgumentException("Group ID cannot be empty", nameof(groupId));
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            lock (_groupsLock)
            {
                if (!_groups.TryGetValue(groupId, out var list))
                {
                    list = new List<WeakReference<IRelayCommand>>();
                    _groups[groupId] = list;
                }
                list.Add(new WeakReference<IRelayCommand>(command));
            }
        }

        /// <inheritdoc />
        public void RemoveFromGroup(string groupId, IRelayCommand command)
        {
            if (string.IsNullOrEmpty(groupId)) return;
            if (command == null) return;

            lock (_groupsLock)
            {
                if (!_groups.TryGetValue(groupId, out var list))
                    return;
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].TryGetTarget(out var cmd) && ReferenceEquals(cmd, command))
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private void InvalidateAllDebounced()
        {
            List<string> groupIds;
            lock (_groupsLock)
            {
                groupIds = _groups.Keys.ToList();
            }
            lock (_pendingLock)
            {
                foreach (var id in groupIds)
                    _pendingGroups.Add(id);
            }
            if (_debounceTimer != null)
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
            else
            {
                FlushPendingInvalidations();
            }
        }

        private void InvalidateGroupDebounced(string groupId)
        {
            if (string.IsNullOrEmpty(groupId)) return;

            lock (_pendingLock)
            {
                _pendingGroups.Add(groupId);
            }

            if (_debounceTimer != null)
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
            else
            {
                FlushPendingInvalidations();
            }
        }

        private void FlushPendingInvalidations()
        {
            HashSet<string> toInvalidate;
            lock (_pendingLock)
            {
                toInvalidate = _pendingGroups;
                _pendingGroups = new HashSet<string>();
            }
            if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    foreach (var id in toInvalidate)
                        InvalidateGroupCore(id);
                });
            }
            else
            {
                foreach (var id in toInvalidate)
                    InvalidateGroupCore(id);
            }
        }

        private void InvalidateGroupCore(string groupId)
        {
            List<WeakReference<IRelayCommand>>? list;
            lock (_groupsLock)
            {
                if (!_groups.TryGetValue(groupId, out list))
                    return;
                list = list.ToList();
            }

            var toRemove = new List<WeakReference<IRelayCommand>>();
            foreach (var weak in list)
            {
                if (weak.TryGetTarget(out var cmd))
                {
                    cmd.NotifyCanExecuteChanged();
                }
                else
                {
                    toRemove.Add(weak);
                }
            }

            if (toRemove.Count > 0)
            {
                lock (_groupsLock)
                {
                    if (_groups.TryGetValue(groupId, out var current))
                    {
                        foreach (var w in toRemove)
                            current.Remove(w);
                    }
                }
            }
        }
    }
}
