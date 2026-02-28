using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Windows.Input;
using VoiceStudio.Core.Plugins.Models;

namespace VoiceStudio.App.Controls
{
    /// <summary>
    /// A card control for displaying plugin information.
    /// </summary>
    public sealed partial class PluginCard : UserControl
    {
        public PluginCard()
        {
            this.InitializeComponent();
        }

        #region Dependency Properties

        /// <summary>
        /// The plugin to display.
        /// </summary>
        public static readonly DependencyProperty PluginProperty =
            DependencyProperty.Register(
                nameof(Plugin),
                typeof(PluginInfo),
                typeof(PluginCard),
                new PropertyMetadata(null, OnPluginChanged));

        public PluginInfo? Plugin
        {
            get => (PluginInfo?)GetValue(PluginProperty);
            set => SetValue(PluginProperty, value);
        }

        /// <summary>
        /// Command to execute when the action button is clicked.
        /// </summary>
        public static readonly DependencyProperty ActionCommandProperty =
            DependencyProperty.Register(
                nameof(ActionCommand),
                typeof(ICommand),
                typeof(PluginCard),
                new PropertyMetadata(null));

        public ICommand? ActionCommand
        {
            get => (ICommand?)GetValue(ActionCommandProperty);
            set => SetValue(ActionCommandProperty, value);
        }

        /// <summary>
        /// Command to execute when the card is clicked.
        /// </summary>
        public static readonly DependencyProperty CardClickCommandProperty =
            DependencyProperty.Register(
                nameof(CardClickCommand),
                typeof(ICommand),
                typeof(PluginCard),
                new PropertyMetadata(null));

        public ICommand? CardClickCommand
        {
            get => (ICommand?)GetValue(CardClickCommandProperty);
            set => SetValue(CardClickCommandProperty, value);
        }

        /// <summary>
        /// Whether the card is in compact mode.
        /// </summary>
        public static readonly DependencyProperty IsCompactProperty =
            DependencyProperty.Register(
                nameof(IsCompact),
                typeof(bool),
                typeof(PluginCard),
                new PropertyMetadata(false));

        public bool IsCompact
        {
            get => (bool)GetValue(IsCompactProperty);
            set => SetValue(IsCompactProperty, value);
        }

        #endregion

        #region Property Changed Handlers

        private static void OnPluginChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PluginCard card && e.NewValue is PluginInfo plugin)
            {
                card.UpdateUI(plugin);
            }
        }

        private void UpdateUI(PluginInfo plugin)
        {
            NameText.Text = plugin.Name;
            AuthorText.Text = plugin.Author;
            DescriptionText.Text = plugin.Description;
            RatingText.Text = plugin.Rating.ToString("F1");
            DownloadsText.Text = FormatDownloadCount(plugin.DownloadCount);
            VersionText.Text = $"v{plugin.Version}";

            // Verified badge
            VerifiedBadge.Visibility = plugin.IsVerified 
                ? Visibility.Visible 
                : Visibility.Collapsed;

            // Action button text
            ActionButton.Content = GetActionText(plugin);
        }

        #endregion

        #region Event Handlers

        // GAP-B18: ActionButton_Click - Removed, now using Command binding in XAML
        // The command binding directly connects ActionCommand with Plugin as CommandParameter

        private void Card_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (CardClickCommand?.CanExecute(Plugin) == true)
            {
                CardClickCommand.Execute(Plugin);
            }
        }

        private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            VisualStateManager.GoToState(this, "PointerOver", true);
        }

        private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            VisualStateManager.GoToState(this, "Normal", true);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets the action button text based on install state.
        /// </summary>
        public static string GetActionText(PluginInfo? plugin)
        {
            if (plugin == null) return "Install";
            if (plugin.HasUpdate) return "Update";
            if (plugin.IsInstalled) return "Installed";
            return "Install";
        }

        /// <summary>
        /// Formats the download count for display.
        /// </summary>
        public static string FormatDownloadCount(int count)
        {
            if (count >= 1_000_000)
                return $"{count / 1_000_000.0:F1}M";
            if (count >= 1_000)
                return $"{count / 1_000.0:F1}K";
            return count.ToString("N0");
        }

        #endregion
    }
}
