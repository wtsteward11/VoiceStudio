using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using VoiceStudio.Core.Plugins;
using VoiceStudio.Core.Plugins.Models;

namespace VoiceStudio.App.Views.Panels
{
    /// <summary>
    /// Detailed view for a single plugin.
    /// Shows full description, version history, and install/uninstall options.
    /// </summary>
    public sealed partial class PluginDetailView : UserControl
    {
        private PluginInfo? _plugin;
        private IPluginGateway? _gateway;

        /// <summary>
        /// Event raised when the user wants to navigate back to the gallery.
        /// </summary>
        public event EventHandler? NavigateBack;

        /// <summary>
        /// Event raised when a plugin is installed or uninstalled.
        /// </summary>
        public event EventHandler<PluginInfo>? PluginStateChanged;

        public PluginDetailView()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Initialize the detail view with plugin data.
        /// </summary>
        public void Initialize(PluginInfo plugin, IPluginGateway gateway)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));

            UpdateUI();
            _ = LoadVersionHistoryAsync();
        }

        private void UpdateUI()
        {
            if (_plugin == null) return;

            // Basic info
            PluginNameText.Text = _plugin.Name;
            AuthorText.Text = $"by {_plugin.Author}";
            DescriptionText.Text = _plugin.LongDescription ?? _plugin.Description;
            VersionText.Text = $"v{_plugin.Version}";
            RatingText.Text = _plugin.Rating.ToString("F1");
            RatingCountText.Text = $"({_plugin.RatingCount} ratings)";
            DownloadCountText.Text = $"{_plugin.DownloadCount:N0} downloads";

            // Additional info
            CategoryText.Text = _plugin.Category;
            LicenseText.Text = _plugin.License ?? "Unknown";
            MinVersionText.Text = _plugin.MinimumAppVersion ?? "1.0.0";
            LastUpdatedText.Text = _plugin.LastUpdated?.ToString("MMM d, yyyy") ?? "Unknown";

            // Verified badge
            VerifiedBadge.Visibility = _plugin.IsVerified 
                ? Visibility.Visible 
                : Visibility.Collapsed;

            // Tags
            TagsControl.ItemsSource = _plugin.Tags;

            // Screenshots
            if (_plugin.Screenshots?.Count > 0)
            {
                ScreenshotsSection.Visibility = Visibility.Visible;
                ScreenshotsGrid.ItemsSource = _plugin.Screenshots;
            }
            else
            {
                ScreenshotsSection.Visibility = Visibility.Collapsed;
            }

            // Links
            if (!string.IsNullOrEmpty(_plugin.HomepageUrl))
            {
                HomepageLink.NavigateUri = new Uri(_plugin.HomepageUrl);
            }

            // Install/Uninstall button state
            UpdateInstallButtonState();
        }

        private void UpdateInstallButtonState()
        {
            if (_plugin == null) return;

            if (_plugin.IsInstalled)
            {
                InstallButton.Visibility = _plugin.HasUpdate 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
                InstallButtonText.Text = _plugin.HasUpdate ? "Update" : "Installed";
                UninstallButton.Visibility = Visibility.Visible;
            }
            else
            {
                InstallButton.Visibility = Visibility.Visible;
                InstallButtonText.Text = "Install";
                UninstallButton.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadVersionHistoryAsync()
        {
            if (_plugin == null || _gateway == null) return;

            try
            {
                var versions = await _gateway.GetPluginVersionsAsync(_plugin.Id);
                VersionHistoryList.ItemsSource = versions;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PluginDetail] Failed to load versions: {ex.Message}");
            }
        }

        #region Event Handlers

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateBack?.Invoke(this, EventArgs.Empty);
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_plugin == null || _gateway == null) return;

            InstallButton.IsEnabled = false;
            InstallButtonText.Text = "Installing...";

            try
            {
                var progress = new Progress<PluginInstallProgress>(p =>
                {
                    InstallButtonText.Text = $"{p.ProgressPercent}%";
                });

                PluginInstallResult result;
                if (_plugin.HasUpdate)
                {
                    result = await _gateway.UpdatePluginAsync(_plugin.Id, progress);
                }
                else
                {
                    result = await _gateway.InstallPluginAsync(_plugin.Id, null, progress);
                }

                if (result.Success)
                {
                    _plugin.IsInstalled = true;
                    _plugin.InstalledVersion = _plugin.Version;
                    UpdateInstallButtonState();
                    PluginStateChanged?.Invoke(this, _plugin);

                    await ShowMessageAsync("Success", $"{_plugin.Name} has been installed successfully.");
                }
                else
                {
                    await ShowMessageAsync("Installation Failed", result.ErrorMessage ?? "Unknown error occurred.");
                }
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Error", ex.Message);
            }
            finally
            {
                InstallButton.IsEnabled = true;
                UpdateInstallButtonState();
            }
        }

        private async void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_plugin == null || _gateway == null) return;

            // Confirm uninstall
            var confirmDialog = new ContentDialog
            {
                Title = "Confirm Uninstall",
                Content = $"Are you sure you want to uninstall {_plugin.Name}?",
                PrimaryButtonText = "Uninstall",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            var confirmResult = await confirmDialog.ShowAsync();
            if (confirmResult != ContentDialogResult.Primary) return;

            UninstallButton.IsEnabled = false;

            try
            {
                var success = await _gateway.UninstallPluginAsync(_plugin.Id);

                if (success)
                {
                    _plugin.IsInstalled = false;
                    _plugin.InstalledVersion = null;
                    UpdateInstallButtonState();
                    PluginStateChanged?.Invoke(this, _plugin);

                    await ShowMessageAsync("Success", $"{_plugin.Name} has been uninstalled.");
                }
                else
                {
                    await ShowMessageAsync("Uninstall Failed", "Failed to uninstall the plugin.");
                }
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Error", ex.Message);
            }
            finally
            {
                UninstallButton.IsEnabled = true;
            }
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        #endregion
    }
}
