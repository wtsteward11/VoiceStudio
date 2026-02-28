using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Commands
{
    /// <summary>
    /// Handles all navigation-related commands: panel switching, view navigation, etc.
    /// </summary>
    public sealed class NavigationHandler
    {
        private readonly IUnifiedCommandRegistry _registry;
        private readonly INavigationService _navigationService;
        private readonly ToastNotificationService? _toastService;

        public event EventHandler<string>? NavigationRequested;

        public NavigationHandler(
            IUnifiedCommandRegistry registry,
            INavigationService navigationService,
            ToastNotificationService? toastService = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _toastService = toastService;

            RegisterCommands();
        }

        private void RegisterCommands()
        {
            // nav.studio
            RegisterNavCommand("nav.studio", "Studio", "Navigate to the main studio view", "🎙️", "studio", "Ctrl+1");

            // nav.profiles
            RegisterNavCommand("nav.profiles", "Voice Profiles", "Navigate to voice profiles panel", "👤", "profiles", "Ctrl+3");

            // nav.library
            RegisterNavCommand("nav.library", "Library", "Navigate to the audio library", "📚", "library", "Ctrl+2");

            // nav.effects
            RegisterNavCommand("nav.effects", "Effects", "Navigate to effects mixer", "🎛️", "effects", "Ctrl+4");

            // nav.train
            RegisterNavCommand("nav.train", "Training", "Navigate to voice training", "🎓", "train");

            // nav.analyze
            RegisterNavCommand("nav.analyze", "Analysis", "Navigate to audio analysis", "📊", "analyze");

            // nav.settings
            RegisterNavCommand("nav.settings", "Settings", "Navigate to application settings", "⚙️", "settings", "Ctrl+,");

            // nav.logs
            RegisterNavCommand("nav.logs", "Logs", "Navigate to diagnostics and logs", "📋", "logs");

            // nav.synthesis
            RegisterNavCommand("nav.synthesis", "Synthesis", "Navigate to voice synthesis panel", "🎤", "synthesis");

            // nav.timeline
            RegisterNavCommand("nav.timeline", "Timeline", "Navigate to the timeline view", "📏", "timeline");

            // nav.back
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "nav.back",
                    Title = "Go Back",
                    Description = "Navigate to the previous panel",
                    Category = "Navigation",
                    Icon = "⬅️",
                    KeyboardShortcut = "Alt+Left"
                },
                async (param, ct) => await NavigateBackAsync(ct),
                _ => _navigationService.CanNavigateBack()
            );

            // nav.forward - Phase 5.2.6: Forward navigation implemented
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "nav.forward",
                    Title = "Go Forward",
                    Description = "Navigate forward to the next panel",
                    Category = "Navigation",
                    Icon = "➡️",
                    KeyboardShortcut = "Alt+Right"
                },
                async (param, ct) => await NavigateForwardAsync(ct),
                _ => _navigationService.CanNavigateForward()
            );

            // nav.home
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "nav.home",
                    Title = "Home",
                    Description = "Navigate to home/dashboard",
                    Category = "Navigation",
                    Icon = "🏠",
                    KeyboardShortcut = "Alt+Home"
                },
                async (param, ct) => await NavigateToPanelAsync("studio", null, ct),
                _ => true
            );

            Debug.WriteLine("[NavigationHandler] Registered 13 navigation commands");
        }

        private void RegisterNavCommand(string commandId, string title, string description, string icon, string panelId, string? shortcut = null)
        {
            _registry.Register(
                new CommandDescriptor
                {
                    Id = commandId,
                    Title = title,
                    Description = description,
                    Category = "Navigation",
                    Icon = icon,
                    KeyboardShortcut = shortcut
                },
                async (param, ct) => await NavigateToPanelAsync(panelId, param as Dictionary<string, object>, ct),
                _ => true
            );
        }

        public async Task NavigateToPanelAsync(string panelId, Dictionary<string, object>? parameters = null, CancellationToken ct = default)
        {
            try
            {
                await _navigationService.NavigateToPanelAsync(panelId, parameters, ct);
                NavigationRequested?.Invoke(this, panelId);
                Debug.WriteLine($"[NavigationHandler] Navigated to: {panelId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NavigationHandler] Navigation to '{panelId}' failed: {ex.Message}");
                throw;
            }
        }

        public async Task NavigateBackAsync(CancellationToken ct = default)
        {
            try
            {
                if (_navigationService.CanNavigateBack())
                {
                    await _navigationService.NavigateBackAsync(ct);
                    var currentPanel = _navigationService.GetCurrentPanelId();
                    NavigationRequested?.Invoke(this, currentPanel ?? "home");
                    Debug.WriteLine($"[NavigationHandler] Navigated back to: {currentPanel}");
                }
                else
                {
                    Debug.WriteLine("[NavigationHandler] Cannot navigate back - empty backstack");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NavigationHandler] Navigate back failed: {ex.Message}");
                throw;
            }
        }

        public string? GetCurrentPanelId()
        {
            return _navigationService.GetCurrentPanelId();
        }

        public bool CanNavigateBack()
        {
            return _navigationService.CanNavigateBack();
        }

        public bool CanNavigateForward()
        {
            return _navigationService.CanNavigateForward();
        }

        public async Task NavigateForwardAsync(CancellationToken ct = default)
        {
            try
            {
                if (_navigationService.CanNavigateForward())
                {
                    await _navigationService.NavigateForwardAsync(ct);
                    var currentPanel = _navigationService.GetCurrentPanelId();
                    NavigationRequested?.Invoke(this, currentPanel ?? "home");
                    Debug.WriteLine($"[NavigationHandler] Navigated forward to: {currentPanel}");
                }
                else
                {
                    Debug.WriteLine("[NavigationHandler] Cannot navigate forward - empty forward stack");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NavigationHandler] Navigate forward failed: {ex.Message}");
                throw;
            }
        }
    }
}
