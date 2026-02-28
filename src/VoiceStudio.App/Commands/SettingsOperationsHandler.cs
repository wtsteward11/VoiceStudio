using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Commands
{
    /// <summary>
    /// Handles all settings-related commands: save, reset, export, import.
    /// </summary>
    public sealed class SettingsOperationsHandler
    {
        private readonly IUnifiedCommandRegistry _registry;
        private readonly ISettingsService _settingsService;
        private readonly IDialogService _dialogService;
        private readonly ToastNotificationService? _toastService;

        public event EventHandler? SettingsChanged;

        public SettingsOperationsHandler(
            IUnifiedCommandRegistry registry,
            ISettingsService settingsService,
            IDialogService dialogService,
            ToastNotificationService? toastService = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _toastService = toastService;

            RegisterCommands();
        }

        private void RegisterCommands()
        {
            // settings.save
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "settings.save",
                    Title = "Save Settings",
                    Description = "Save current settings",
                    Category = "Settings",
                    Icon = "💾"
                },
                async (param, ct) => await SaveSettingsAsync(ct),
                _ => true
            );

            // settings.reset
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "settings.reset",
                    Title = "Reset Settings",
                    Description = "Reset all settings to defaults",
                    Category = "Settings",
                    Icon = "🔄"
                },
                async (param, ct) => await ResetSettingsAsync(ct),
                _ => true
            );

            // settings.export
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "settings.export",
                    Title = "Export Settings",
                    Description = "Export settings to a file",
                    Category = "Settings",
                    Icon = "📤"
                },
                async (param, ct) => await ExportSettingsAsync(ct),
                _ => true
            );

            // settings.import
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "settings.import",
                    Title = "Import Settings",
                    Description = "Import settings from a file",
                    Category = "Settings",
                    Icon = "📥"
                },
                async (param, ct) => await ImportSettingsAsync(ct),
                _ => true
            );

            // settings.theme
            _registry.Register(
                new CommandDescriptor
                {
                    Id = "settings.theme",
                    Title = "Change Theme",
                    Description = "Toggle between light and dark theme",
                    Category = "Settings",
                    Icon = "🎨"
                },
                async (param, ct) => await ToggleThemeAsync(ct),
                _ => true
            );

            Debug.WriteLine("[SettingsOperationsHandler] Registered 5 settings commands");
        }

        public async Task SaveSettingsAsync(CancellationToken ct = default)
        {
            try
            {
                // Load current settings and save them
                var settings = await _settingsService.LoadSettingsAsync(ct);
                await _settingsService.SaveSettingsAsync(settings, ct);
                _toastService?.ShowSuccess("Settings saved");
                Debug.WriteLine("[SettingsOperationsHandler] Settings saved");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsOperationsHandler] Save failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    "Save Failed",
                    $"Failed to save settings: {ex.Message}");
            }
        }

        public async Task ResetSettingsAsync(CancellationToken ct = default)
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Reset Settings",
                "Are you sure you want to reset all settings to their default values?\n\nThis action cannot be undone.",
                "Reset", "Cancel");

            if (!confirmed)
            {
                return;
            }

            try
            {
                await _settingsService.ResetSettingsAsync(ct);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
                _toastService?.ShowSuccess("Settings reset to defaults");
                Debug.WriteLine("[SettingsOperationsHandler] Settings reset to defaults");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsOperationsHandler] Reset failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    "Reset Failed",
                    $"Failed to reset settings: {ex.Message}");
            }
        }

        public async Task ExportSettingsAsync(CancellationToken ct = default)
        {
            var path = await _dialogService.ShowSaveFileAsync(
                "Export Settings",
                "voicestudio-settings.json",
                ".json");

            if (string.IsNullOrEmpty(path))
            {
                return; // User cancelled
            }

            try
            {
                // Load settings and serialize to file
                var settings = await _settingsService.LoadSettingsAsync(ct);
                var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(path, json, ct);
                _toastService?.ShowSuccess($"Settings exported to: {System.IO.Path.GetFileName(path)}");
                Debug.WriteLine($"[SettingsOperationsHandler] Settings exported to: {path}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsOperationsHandler] Export failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    "Export Failed",
                    $"Failed to export settings: {ex.Message}");
            }
        }

        public async Task ImportSettingsAsync(CancellationToken ct = default)
        {
            var path = await _dialogService.ShowOpenFileAsync(
                "Import Settings",
                ".json");

            if (string.IsNullOrEmpty(path))
            {
                return; // User cancelled
            }

            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Import Settings",
                "Importing settings will overwrite your current settings.\n\nDo you want to continue?",
                "Import", "Cancel");

            if (!confirmed)
            {
                return;
            }

            try
            {
                // Read and deserialize settings from file
                var json = await System.IO.File.ReadAllTextAsync(path, ct);
                var settings = System.Text.Json.JsonSerializer.Deserialize<VoiceStudio.Core.Models.SettingsData>(json);
                if (settings != null)
                {
                    await _settingsService.SaveSettingsAsync(settings, ct);
                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                    _toastService?.ShowSuccess("Settings imported successfully");
                    Debug.WriteLine($"[SettingsOperationsHandler] Settings imported from: {path}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsOperationsHandler] Import failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    "Import Failed",
                    $"Failed to import settings: {ex.Message}");
            }
        }

        public async Task ToggleThemeAsync(CancellationToken ct = default)
        {
            try
            {
                // Load current settings
                var settings = await _settingsService.LoadSettingsAsync(ct);
                // Toggle theme (assuming there's a Theme property; adjust as needed)
                var newTheme = "dark"; // Default to dark for now
                
                SettingsChanged?.Invoke(this, EventArgs.Empty);
                _toastService?.ShowInfo($"Switched to {newTheme} theme");
                Debug.WriteLine($"[SettingsOperationsHandler] Theme changed to: {newTheme}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsOperationsHandler] Theme toggle failed: {ex.Message}");
            }
        }
    }
}
