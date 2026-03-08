using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Bridges the NavigationHandler command system with MainWindow's panel switching.
    /// All navigation routes through OpenPanelById — no direct view instantiation.
    /// </summary>
    public sealed class NavigationBridge : INavigationService
    {
        private readonly List<NavigationEntry> _backStack = new();
        private readonly List<NavigationEntry> _forwardStack = new();
        private string? _currentPanelId;
        private Action<string, PanelRegion?>? _openPanelByIdCallback;
        private Action<string>? _setActiveNavButtonCallback;

        /// <inheritdoc />
        public event EventHandler<NavigationEventArgs>? NavigationChanged;

        /// <inheritdoc />
        public event EventHandler? BackStackChanged;

        /// <summary>
        /// Initializes the bridge with callbacks from MainWindow.
        /// </summary>
        /// <param name="openPanelByIdCallback">Callback matching OpenPanelById(panelId, overrideRegion)</param>
        /// <param name="setActiveNavButtonCallback">Callback to update nav button state</param>
        public void Initialize(
            Action<string, PanelRegion?> openPanelByIdCallback,
            Action<string> setActiveNavButtonCallback)
        {
            _openPanelByIdCallback = openPanelByIdCallback ?? throw new ArgumentNullException(nameof(openPanelByIdCallback));
            _setActiveNavButtonCallback = setActiveNavButtonCallback ?? throw new ArgumentNullException(nameof(setActiveNavButtonCallback));
        }

        public Task NavigateToPanelAsync(string panelId, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default)
        {
            var canonicalId = NormalizeToCanonicalId(panelId);
            var previousPanelId = _currentPanelId;

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

            _forwardStack.Clear();
            _currentPanelId = canonicalId;

            _openPanelByIdCallback?.Invoke(canonicalId, null);

            var navButtonName = GetNavButtonName(panelId);
            if (!string.IsNullOrEmpty(navButtonName))
                _setActiveNavButtonCallback?.Invoke(navButtonName);

            NavigationChanged?.Invoke(this, new NavigationEventArgs
            {
                PreviousPanelId = previousPanelId,
                NewPanelId = canonicalId,
                Parameters = parameters ?? new Dictionary<string, object>(),
                IsBackNavigation = false
            });

            Debug.WriteLine($"[NavigationBridge] Navigated to: {canonicalId} (input: {panelId})");
            return Task.CompletedTask;
        }

        public Task NavigateBackAsync(CancellationToken cancellationToken = default)
        {
            if (_backStack.Count == 0)
                return Task.CompletedTask;

            var previousPanelId = _currentPanelId;
            var lastEntry = _backStack[_backStack.Count - 1];
            _backStack.RemoveAt(_backStack.Count - 1);
            BackStackChanged?.Invoke(this, EventArgs.Empty);

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

            _openPanelByIdCallback?.Invoke(lastEntry.PanelId, null);

            var navButtonName = GetNavButtonName(lastEntry.PanelId);
            if (!string.IsNullOrEmpty(navButtonName))
                _setActiveNavButtonCallback?.Invoke(navButtonName);

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
                return Task.CompletedTask;

            var previousPanelId = _currentPanelId;
            var nextEntry = _forwardStack[_forwardStack.Count - 1];
            _forwardStack.RemoveAt(_forwardStack.Count - 1);

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

            _openPanelByIdCallback?.Invoke(nextEntry.PanelId, null);

            var navButtonName = GetNavButtonName(nextEntry.PanelId);
            if (!string.IsNullOrEmpty(navButtonName))
                _setActiveNavButtonCallback?.Invoke(navButtonName);

            NavigationChanged?.Invoke(this, new NavigationEventArgs
            {
                PreviousPanelId = previousPanelId,
                NewPanelId = nextEntry.PanelId,
                Parameters = nextEntry.Parameters,
                IsBackNavigation = false
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

        private static string NormalizeToCanonicalId(string panelId) => panelId.ToLowerInvariant() switch
        {
            "studio" or "home" => "Timeline",
            "timeline" => "Timeline",
            "effects" => "EffectsMixer",
            "train" => "Training",
            "analyze" => "Analyzer",
            "logs" => "Diagnostics",
            "synthesis" => "VoiceSynthesis",
            "settings" => "Settings",
            "profiles" => "Profiles",
            "library" => "Library",
            _ => panelId
        };

        private static string? GetNavButtonName(string panelId)
        {
            return panelId.ToLowerInvariant() switch
            {
                "studio" or "timeline" or "home" => "NavStudio",
                "profiles" => "NavProfiles",
                "library" => "NavLibrary",
                "effects" or "effectsmixer" => "NavEffects",
                "train" or "training" => "NavTrain",
                "analyze" or "analyzer" => "NavAnalyze",
                "settings" => "NavSettings",
                "logs" or "diagnostics" => "NavLogs",
                _ => null
            };
        }
    }
}
