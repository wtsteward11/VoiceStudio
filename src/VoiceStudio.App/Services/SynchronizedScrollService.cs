// VoiceStudio - Panel Architecture Phase D: Synchronized Scrolling
// Service implementation for coordinating scroll position across panels

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Implementation of ISynchronizedScrollService that coordinates scroll position
    /// synchronization across panels. Uses weak references to prevent memory leaks.
    /// </summary>
    public sealed class SynchronizedScrollService : ISynchronizedScrollService
    {
        private readonly ILogger<SynchronizedScrollService>? _logger;
        private readonly IEventAggregator? _eventAggregator;
        private readonly ThrottledEventPublisher? _throttledPublisher;
        private readonly List<WeakReference<ISynchronizedScrolling>> _panels = new();
        private readonly object _lock = new();
        private bool _isEnabled = true;
        private bool _isBroadcasting; // Prevent re-entrancy

        /// <inheritdoc />
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                _logger?.LogDebug("Synchronized scrolling {State}", value ? "enabled" : "disabled");
            }
        }

        /// <inheritdoc />
        public event EventHandler<ScrollPositionChangedEventArgs>? ScrollBroadcast;

        public SynchronizedScrollService(
            IEventAggregator? eventAggregator = null,
            ILogger<SynchronizedScrollService>? logger = null,
            ThrottledEventPublisher? throttledPublisher = null)
        {
            _eventAggregator = eventAggregator;
            _logger = logger;
            _throttledPublisher = throttledPublisher;
        }

        /// <inheritdoc />
        public void Register(ISynchronizedScrolling panel)
        {
            if (panel == null) return;

            lock (_lock)
            {
                // Check if already registered
                foreach (var weakRef in _panels)
                {
                    if (weakRef.TryGetTarget(out var existing) && ReferenceEquals(existing, panel))
                    {
                        return; // Already registered
                    }
                }

                _panels.Add(new WeakReference<ISynchronizedScrolling>(panel));

                // Subscribe to the panel's scroll changes
                panel.ScrollPositionChanged += OnPanelScrollChanged;

                _logger?.LogDebug("Registered panel {PanelId} for synchronized scrolling in group {Group}",
                    panel.ScrollPanelId, panel.ScrollGroup);
            }
        }

        /// <inheritdoc />
        public void Unregister(ISynchronizedScrolling panel)
        {
            if (panel == null) return;

            lock (_lock)
            {
                for (int i = _panels.Count - 1; i >= 0; i--)
                {
                    if (_panels[i].TryGetTarget(out var existing) && ReferenceEquals(existing, panel))
                    {
                        // Unsubscribe from scroll changes
                        panel.ScrollPositionChanged -= OnPanelScrollChanged;
                        _panels.RemoveAt(i);

                        _logger?.LogDebug("Unregistered panel {PanelId} from synchronized scrolling",
                            panel.ScrollPanelId);
                        return;
                    }
                }
            }
        }

        /// <inheritdoc />
        public void BroadcastScroll(ScrollPositionChangedEventArgs args)
        {
            if (args == null) return;
            if (!_isEnabled) return;

            // Prevent re-entrancy (scroll changes triggering more broadcast)
            if (_isBroadcasting) return;

            List<ISynchronizedScrolling> panelsToNotify;

            lock (_lock)
            {
                _isBroadcasting = true;

                try
                {
                    // Get active panels in the same group (excluding source)
                    panelsToNotify = CleanAndGetPanels()
                        .Where(p => p.ScrollGroup == args.ScrollGroup &&
                                    p.ScrollPanelId != args.SourcePanelId &&
                                    p.IsSynchronizedScrollEnabled)
                        .ToList();
                }
                finally
                {
                    _isBroadcasting = false;
                }
            }

            _logger?.LogTrace("Broadcasting scroll to {Count} panels in group {Group}",
                panelsToNotify.Count, args.ScrollGroup);

            // Notify panels outside the lock
            foreach (var panel in panelsToNotify)
            {
                try
                {
                    if (args.TimePosition.HasValue)
                    {
                        panel.SetTimePosition(args.TimePosition.Value, args.SourcePanelId);
                    }
                    else
                    {
                        panel.SetScrollPosition(args.NormalizedPosition, args.SourcePanelId);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error synchronizing scroll to panel {PanelId}",
                        panel.ScrollPanelId);
                }
            }

            // Raise event for external listeners
            ScrollBroadcast?.Invoke(this, args);

            // Publish through event aggregator
            // HIGH_FREQUENCY_EVENT_THROTTLE_POLICY: 50-100ms Trailing
            var evt = new ScrollSyncEvent(args);
            if (_throttledPublisher != null)
                _throttledPublisher.Publish(evt, throttleMs: 75, mode: ThrottleMode.Trailing);
            else
                _eventAggregator?.Publish(evt);
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> GetGroupMembers(string groupName)
        {
            lock (_lock)
            {
                return CleanAndGetPanels()
                    .Where(p => p.ScrollGroup == groupName)
                    .Select(p => p.ScrollPanelId)
                    .ToArray();
            }
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> GetGroups()
        {
            lock (_lock)
            {
                return CleanAndGetPanels()
                    .Select(p => p.ScrollGroup)
                    .Distinct()
                    .ToArray();
            }
        }

        private void OnPanelScrollChanged(object? sender, ScrollPositionChangedEventArgs args)
        {
            // Only broadcast if enabled and not re-entrant
            if (_isEnabled && !_isBroadcasting)
            {
                BroadcastScroll(args);
            }
        }

        private List<ISynchronizedScrolling> CleanAndGetPanels()
        {
            var active = new List<ISynchronizedScrolling>();
            var toRemove = new List<int>();

            for (int i = 0; i < _panels.Count; i++)
            {
                if (_panels[i].TryGetTarget(out var panel))
                {
                    active.Add(panel);
                }
                else
                {
                    toRemove.Add(i);
                }
            }

            // Remove dead references (in reverse order to maintain indices)
            for (int i = toRemove.Count - 1; i >= 0; i--)
            {
                _panels.RemoveAt(toRemove[i]);
            }

            return active;
        }
    }

    /// <summary>
    /// Event published through EventAggregator when scroll is synchronized.
    /// </summary>
    public sealed class ScrollSyncEvent : VoiceStudio.Core.Events.PanelEventBase
    {
        public ScrollPositionChangedEventArgs ScrollArgs { get; }

        public ScrollSyncEvent(ScrollPositionChangedEventArgs args)
            : base("ScrollSync")
        {
            ScrollArgs = args;
        }

        public override string ToString() =>
            $"ScrollSync: {ScrollArgs.ScrollGroup} @ {ScrollArgs.NormalizedPosition:F3} from {ScrollArgs.SourcePanelId}";
    }
}
