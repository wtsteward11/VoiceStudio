using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Views;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Bridges the NavigationHandler command system with MainWindow's panel switching.
    /// This allows commands to trigger navigation while respecting the existing UI architecture.
    /// </summary>
    public sealed class NavigationBridge : INavigationService
    {
        private readonly List<NavigationEntry> _backStack = new();
        private readonly List<NavigationEntry> _forwardStack = new();
        private string? _currentPanelId;
        private Action<PanelRegion, string, Func<UserControl>>? _switchPanelCallback;
        private Action<string>? _setActiveNavButtonCallback;

        /// <inheritdoc />
        public event EventHandler<NavigationEventArgs>? NavigationChanged;

        /// <inheritdoc />
        public event EventHandler? BackStackChanged;

        /// <summary>
        /// Panel registry mapping panel IDs to their factory functions and default regions.
        /// </summary>
        private readonly Dictionary<string, (PanelRegion DefaultRegion, string Title, Func<UserControl> Factory)> _panelRegistry;

        public NavigationBridge()
        {
            _panelRegistry = new(StringComparer.OrdinalIgnoreCase)
            {
                // Core synthesis panels
                ["studio"] = (PanelRegion.Center, "Timeline", () => new TimelineView()),
                ["timeline"] = (PanelRegion.Center, "Timeline", () => new TimelineView()),
                ["profiles"] = (PanelRegion.Left, "Profiles", () => new ProfilesView()),
                ["library"] = (PanelRegion.Left, "Library", () => new LibraryView()),
                ["effects"] = (PanelRegion.Right, "Effects Mixer", () => new EffectsMixerView()),
                ["train"] = (PanelRegion.Left, "Training", () => new TrainingView()),
                ["analyze"] = (PanelRegion.Right, "Analyzer", () => new AnalyzerView()),
                ["settings"] = (PanelRegion.Right, "Settings", () => new SettingsView()),
                ["logs"] = (PanelRegion.Bottom, "Diagnostics", () => new DiagnosticsView()),
                ["synthesis"] = (PanelRegion.Center, "Voice Synthesis", () => new VoiceSynthesisView()),
                ["home"] = (PanelRegion.Center, "Timeline", () => new TimelineView()),
            };
        }

        /// <summary>
        /// Initializes the bridge with callbacks from MainWindow.
        /// </summary>
        /// <param name="switchPanelCallback">Callback to switch panels</param>
        /// <param name="setActiveNavButtonCallback">Callback to update nav button state</param>
        public void Initialize(
            Action<PanelRegion, string, Func<UserControl>> switchPanelCallback,
            Action<string> setActiveNavButtonCallback)
        {
            _switchPanelCallback = switchPanelCallback ?? throw new ArgumentNullException(nameof(switchPanelCallback));
            _setActiveNavButtonCallback = setActiveNavButtonCallback ?? throw new ArgumentNullException(nameof(setActiveNavButtonCallback));
        }

        public Task NavigateToPanelAsync(string panelId, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default)
        {
            if (!_panelRegistry.TryGetValue(panelId, out var panelInfo))
            {
                Debug.WriteLine($"[NavigationBridge] Unknown panel ID: {panelId}");
                return Task.CompletedTask;
            }

            var previousPanelId = _currentPanelId;

            // Push current to backstack
            if (!string.IsNullOrEmpty(_currentPanelId))
            {
                _backStack.Add(new NavigationEntry
                {
                    PanelId = _currentPanelId,
                    Parameters = new Dictionary<string, object>(),
                    Timestamp = DateTime.UtcNow
                });
                BackStackChanged?.Invoke(this, EventArgs.Empty);
            }

            // Clear forward stack when navigating to a new panel (new branch)
            _forwardStack.Clear();

            _currentPanelId = panelId;

            // Invoke the MainWindow panel switch
            _switchPanelCallback?.Invoke(panelInfo.DefaultRegion, panelInfo.Title, panelInfo.Factory);

            // Update nav button state
            var navButtonName = GetNavButtonName(panelId);
            if (!string.IsNullOrEmpty(navButtonName))
            {
                _setActiveNavButtonCallback?.Invoke(navButtonName);
            }

            // Raise navigation changed event
            NavigationChanged?.Invoke(this, new NavigationEventArgs
            {
                PreviousPanelId = previousPanelId,
                NewPanelId = panelId,
                Parameters = parameters ?? new Dictionary<string, object>(),
                IsBackNavigation = false
            });

            Debug.WriteLine($"[NavigationBridge] Navigated to: {panelId}");
            return Task.CompletedTask;
        }

        public Task NavigateBackAsync(CancellationToken cancellationToken = default)
        {
            if (_backStack.Count == 0)
            {
                return Task.CompletedTask;
            }

            var previousPanelId = _currentPanelId;
            var lastEntry = _backStack[_backStack.Count - 1];
            _backStack.RemoveAt(_backStack.Count - 1);
            BackStackChanged?.Invoke(this, EventArgs.Empty);

            // Push current to forward stack before going back
            if (!string.IsNullOrEmpty(_currentPanelId))
            {
                _forwardStack.Add(new NavigationEntry
                {
                    PanelId = _currentPanelId,
                    Parameters = new Dictionary<string, object>(),
                    Timestamp = DateTime.UtcNow
                });
            }

            _currentPanelId = lastEntry.PanelId;

            if (_panelRegistry.TryGetValue(lastEntry.PanelId, out var panelInfo))
            {
                _switchPanelCallback?.Invoke(panelInfo.DefaultRegion, panelInfo.Title, panelInfo.Factory);

                var navButtonName = GetNavButtonName(lastEntry.PanelId);
                if (!string.IsNullOrEmpty(navButtonName))
                {
                    _setActiveNavButtonCallback?.Invoke(navButtonName);
                }
            }

            // Raise navigation changed event
            NavigationChanged?.Invoke(this, new NavigationEventArgs
            {
                PreviousPanelId = previousPanelId,
                NewPanelId = lastEntry.PanelId,
                Parameters = lastEntry.Parameters,
                IsBackNavigation = true
            });

            Debug.WriteLine($"[NavigationBridge] Navigated back to: {lastEntry.PanelId}");
            return Task.CompletedTask;
        }

        public bool CanNavigateBack()
        {
            return _backStack.Count > 0;
        }

        /// <inheritdoc />
        public Task NavigateForwardAsync(CancellationToken cancellationToken = default)
        {
            if (_forwardStack.Count == 0)
            {
                return Task.CompletedTask;
            }

            var previousPanelId = _currentPanelId;
            var nextEntry = _forwardStack[_forwardStack.Count - 1];
            _forwardStack.RemoveAt(_forwardStack.Count - 1);

            // Push current to back stack before going forward
            if (!string.IsNullOrEmpty(_currentPanelId))
            {
                _backStack.Add(new NavigationEntry
                {
                    PanelId = _currentPanelId,
                    Parameters = new Dictionary<string, object>(),
                    Timestamp = DateTime.UtcNow
                });
                BackStackChanged?.Invoke(this, EventArgs.Empty);
            }

            _currentPanelId = nextEntry.PanelId;

            if (_panelRegistry.TryGetValue(nextEntry.PanelId, out var panelInfo))
            {
                _switchPanelCallback?.Invoke(panelInfo.DefaultRegion, panelInfo.Title, panelInfo.Factory);

                var navButtonName = GetNavButtonName(nextEntry.PanelId);
                if (!string.IsNullOrEmpty(navButtonName))
                {
                    _setActiveNavButtonCallback?.Invoke(navButtonName);
                }
            }

            // Raise navigation changed event
            NavigationChanged?.Invoke(this, new NavigationEventArgs
            {
                PreviousPanelId = previousPanelId,
                NewPanelId = nextEntry.PanelId,
                Parameters = nextEntry.Parameters,
                IsBackNavigation = false // This is forward navigation
            });

            Debug.WriteLine($"[NavigationBridge] Navigated forward to: {nextEntry.PanelId}");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public bool CanNavigateForward()
        {
            return _forwardStack.Count > 0;
        }

        public string? GetCurrentPanelId()
        {
            return _currentPanelId;
        }

        public IReadOnlyList<NavigationEntry> GetBackStack()
        {
            return _backStack.AsReadOnly();
        }

        /// <inheritdoc />
        public void ClearBackStack()
        {
            _backStack.Clear();
            BackStackChanged?.Invoke(this, EventArgs.Empty);
        }

        private static string? GetNavButtonName(string panelId)
        {
            return panelId.ToLowerInvariant() switch
            {
                "studio" or "timeline" or "home" => "NavStudio",
                "profiles" => "NavProfiles",
                "library" => "NavLibrary",
                "effects" => "NavEffects",
                "train" => "NavTrain",
                "analyze" => "NavAnalyze",
                "settings" => "NavSettings",
                "logs" => "NavLogs",
                _ => null
            };
        }
    }
}
