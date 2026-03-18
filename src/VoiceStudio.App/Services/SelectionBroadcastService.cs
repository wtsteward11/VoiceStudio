// VoiceStudio - Panel Architecture Phase D: Selection Synchronization
// Service implementation for broadcasting selection changes

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Implementation of ISelectionBroadcastService that manages selection synchronization
    /// across panels. Uses weak references to avoid memory leaks from panel registrations.
    /// </summary>
    public sealed class SelectionBroadcastService : ISelectionBroadcastService
    {
        private readonly ILogger<SelectionBroadcastService>? _logger;
        private readonly IEventAggregator? _eventAggregator;
        private readonly ThrottledEventPublisher? _throttledPublisher;
        private readonly List<WeakReference<ISelectionFollower>> _followers = new();
        private readonly Dictionary<string, bool> _panelFollowState = new();
        private readonly List<SelectionInfo> _selectionHistory = new();
        private readonly object _lock = new();
        private readonly int _maxHistorySize;

        private SelectionInfo _currentSelection = SelectionInfo.Empty;

        /// <inheritdoc />
        public SelectionInfo CurrentSelection
        {
            get
            {
                lock (_lock)
                {
                    return _currentSelection;
                }
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<SelectionInfo> SelectionHistory
        {
            get
            {
                lock (_lock)
                {
                    return _selectionHistory.ToArray();
                }
            }
        }

        /// <inheritdoc />
        public event EventHandler<PanelSelectionChangedEventArgs>? SelectionBroadcast;

        public SelectionBroadcastService(
            IEventAggregator? eventAggregator = null,
            ILogger<SelectionBroadcastService>? logger = null,
            ThrottledEventPublisher? throttledPublisher = null,
            int maxHistorySize = 50)
        {
            _eventAggregator = eventAggregator;
            _logger = logger;
            _throttledPublisher = throttledPublisher;
            _maxHistorySize = maxHistorySize;
        }

        /// <inheritdoc />
        public void BroadcastSelection(SelectionInfo selection)
        {
            if (selection == null) return;

            SelectionInfo previousSelection;
            List<ISelectionFollower> activeFollowers;

            lock (_lock)
            {
                previousSelection = _currentSelection;
                _currentSelection = selection;

                // Add to history
                if (!selection.IsEmpty)
                {
                    _selectionHistory.Add(selection);
                    TrimHistory();
                }

                // Get active followers (cleaning up dead references)
                activeFollowers = CleanAndGetFollowers();
            }

            _logger?.LogDebug("Broadcasting selection: {Type} {Id} from {Source}",
                selection.Type, selection.Id, selection.SourcePanelId);

            // Notify followers asynchronously
            var args = new PanelSelectionChangedEventArgs(previousSelection, selection);
            
            foreach (var follower in activeFollowers)
            {
                // Check if this follower is enabled and supports this selection type
                if (!follower.IsFollowingSelection) continue;
                if (!SupportsSelectionType(follower, selection.Type)) continue;

                // Fire and forget - we don't want slow followers to block
                _ = NotifyFollowerAsync(follower, selection);
            }

            // Raise event for any other listeners
            SelectionBroadcast?.Invoke(this, args);

            // Also publish through event aggregator if available
            // HIGH_FREQUENCY_EVENT_THROTTLE_POLICY: 50ms Trailing
            var evt = new SelectionBroadcastEvent(previousSelection, selection);
            if (_throttledPublisher != null)
                _throttledPublisher.Publish(evt, throttleMs: 50, mode: ThrottleMode.Trailing);
            else
                _eventAggregator?.Publish(evt);
        }

        /// <inheritdoc />
        public void RegisterFollower(ISelectionFollower follower)
        {
            if (follower == null) return;

            lock (_lock)
            {
                // Check if already registered
                foreach (var weakRef in _followers)
                {
                    if (weakRef.TryGetTarget(out var existing) && ReferenceEquals(existing, follower))
                    {
                        return; // Already registered
                    }
                }

                _followers.Add(new WeakReference<ISelectionFollower>(follower));
                _logger?.LogDebug("Registered selection follower");
            }
        }

        /// <inheritdoc />
        public void UnregisterFollower(ISelectionFollower follower)
        {
            if (follower == null) return;

            lock (_lock)
            {
                for (int i = _followers.Count - 1; i >= 0; i--)
                {
                    if (_followers[i].TryGetTarget(out var existing) && ReferenceEquals(existing, follower))
                    {
                        _followers.RemoveAt(i);
                        _logger?.LogDebug("Unregistered selection follower");
                        return;
                    }
                }
            }
        }

        /// <inheritdoc />
        public bool IsPanelFollowing(string panelId)
        {
            if (string.IsNullOrEmpty(panelId)) return false;

            lock (_lock)
            {
                return _panelFollowState.TryGetValue(panelId, out var following) && following;
            }
        }

        /// <inheritdoc />
        public void SetPanelFollowing(string panelId, bool follow)
        {
            if (string.IsNullOrEmpty(panelId)) return;

            lock (_lock)
            {
                _panelFollowState[panelId] = follow;
            }

            _logger?.LogDebug("Panel {PanelId} follow selection: {Follow}", panelId, follow);
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> GetFollowerPanelIds()
        {
            lock (_lock)
            {
                return _panelFollowState
                    .Where(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToArray();
            }
        }

        private List<ISelectionFollower> CleanAndGetFollowers()
        {
            var active = new List<ISelectionFollower>();
            var toRemove = new List<int>();

            for (int i = 0; i < _followers.Count; i++)
            {
                if (_followers[i].TryGetTarget(out var follower))
                {
                    active.Add(follower);
                }
                else
                {
                    toRemove.Add(i);
                }
            }

            // Remove dead references (in reverse order to maintain indices)
            for (int i = toRemove.Count - 1; i >= 0; i--)
            {
                _followers.RemoveAt(toRemove[i]);
            }

            return active;
        }

        private bool SupportsSelectionType(ISelectionFollower follower, SelectionType type)
        {
            var supported = follower.SupportedSelectionTypes;
            if (supported == null || supported.Length == 0) return false;

            // "Any" type matches all
            if (supported.Contains(SelectionType.Any)) return true;

            return supported.Contains(type);
        }

        private async Task NotifyFollowerAsync(ISelectionFollower follower, SelectionInfo selection)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await follower.OnSelectionChangedAsync(selection, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("Selection follower timed out");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error notifying selection follower");
            }
        }

        private void TrimHistory()
        {
            while (_selectionHistory.Count > _maxHistorySize)
            {
                _selectionHistory.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Event published through the EventAggregator when selection is broadcast.
    /// </summary>
    public sealed class SelectionBroadcastEvent : VoiceStudio.Core.Events.PanelEventBase
    {
        public SelectionInfo Previous { get; }
        public SelectionInfo Current { get; }

        public SelectionBroadcastEvent(SelectionInfo previous, SelectionInfo current)
            : base("SelectionBroadcast")
        {
            Previous = previous ?? SelectionInfo.Empty;
            Current = current ?? SelectionInfo.Empty;
        }

        public override string ToString() =>
            $"SelectionBroadcast: {Current.Type} {Current.Id} from {Current.SourcePanelId}";
    }
}
