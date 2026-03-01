// VoiceStudio Plugin Permission Dialog
// Phase 1: UI for requesting user consent for plugin permissions

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.Core.Plugins;
using Colors = Microsoft.UI.Colors;

namespace VoiceStudio.App.Views.Dialogs;

/// <summary>
/// Dialog for requesting plugin permission consent from the user.
/// </summary>
public sealed partial class PluginPermissionDialog : ContentDialog
{
    private readonly List<PermissionRequestItem> _permissionItems = new();

    /// <summary>
    /// Gets the plugin ID this dialog is for.
    /// </summary>
    public string PluginId { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether the user chose to remember their choice.
    /// </summary>
    public bool RememberChoice => RememberChoiceCheckBox.IsChecked ?? true;

    /// <summary>
    /// Gets the permissions that were granted by the user.
    /// </summary>
    public IReadOnlyList<string> GrantedPermissions =>
        _permissionItems.Where(p => p.IsGranted).Select(p => p.PermissionId).ToList();

    /// <summary>
    /// Gets the permissions that were denied by the user.
    /// </summary>
    public IReadOnlyList<string> DeniedPermissions =>
        _permissionItems.Where(p => !p.IsGranted).Select(p => p.PermissionId).ToList();

    /// <summary>
    /// Gets whether the user approved any permissions.
    /// </summary>
    public bool AnyGranted { get; private set; }

    /// <summary>
    /// Gets whether the user denied all permissions.
    /// </summary>
    public bool AllDenied { get; private set; }

    public PluginPermissionDialog()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// Initialize the dialog with plugin info and requested permissions.
    /// </summary>
    /// <param name="pluginId">Plugin identifier</param>
    /// <param name="pluginName">Display name of the plugin</param>
    /// <param name="requestedPermissions">List of permission IDs being requested</param>
    public void Initialize(string pluginId, string pluginName, IEnumerable<string> requestedPermissions)
    {
        PluginId = pluginId;
        PluginNameText.Text = pluginName;
        PluginIdText.Text = pluginId;

        _permissionItems.Clear();
        bool hasHighRisk = false;

        foreach (var permissionId in requestedPermissions)
        {
            var info = PluginPermissions.GetPermissionInfo(permissionId);
            var item = new PermissionRequestItem
            {
                PermissionId = permissionId,
                DisplayName = FormatPermissionName(permissionId),
                Description = info?.Description ?? "Unknown permission",
                RiskLevel = info?.Risk.ToString() ?? "Unknown",
                IsGranted = info?.Risk != PermissionRisk.High // Default grant low/medium, deny high
            };

            if (info != null)
            {
                item.SetRiskLevel(info.Risk);
                if (info.Risk == PermissionRisk.High)
                {
                    hasHighRisk = true;
                }
            }

            _permissionItems.Add(item);
        }

        PermissionsList.ItemsSource = _permissionItems;
        HighRiskWarning.Visibility = hasHighRisk ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatPermissionName(string permissionId)
    {
        // Convert "filesystem.read" to "File System Read"
        var parts = permissionId.Split('.');
        if (parts.Length >= 2)
        {
            var category = FormatWord(parts[0]);
            var action = FormatWord(parts[1]);
            return $"{category} - {action}";
        }
        return FormatWord(permissionId);
    }

    private static string FormatWord(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;

        // Insert spaces before capitals and capitalize first letter
        var result = string.Concat(word.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));

        return char.ToUpper(result[0]) + result[1..];
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        AnyGranted = _permissionItems.Any(p => p.IsGranted);
        AllDenied = false;
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Deny all - uncheck everything
        foreach (var item in _permissionItems)
        {
            item.IsGranted = false;
        }
        AnyGranted = false;
        AllDenied = true;
    }
}

/// <summary>
/// View model for a single permission request item.
/// </summary>
public sealed class PermissionRequestItem : INotifyPropertyChanged
{
    private bool _isGranted;

    public string PermissionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = "Low";

    public bool IsGranted
    {
        get => _isGranted;
        set
        {
            if (_isGranted != value)
            {
                _isGranted = value;
                OnPropertyChanged();
            }
        }
    }

    // Visual properties
    public Brush RiskColor { get; private set; } = new SolidColorBrush(Colors.Green);
    public Brush RiskBadgeBackground { get; private set; } = new SolidColorBrush(Colors.Green);
    public Brush RiskBadgeForeground { get; private set; } = new SolidColorBrush(Colors.White);

    public void SetRiskLevel(PermissionRisk risk)
    {
        RiskLevel = risk.ToString();

        switch (risk)
        {
            case PermissionRisk.Low:
                RiskColor = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80)); // Green
                RiskBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 76, 175, 80));
                RiskBadgeForeground = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));
                break;

            case PermissionRisk.Medium:
                RiskColor = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0)); // Orange
                RiskBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 255, 152, 0));
                RiskBadgeForeground = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0));
                break;

            case PermissionRisk.High:
                RiskColor = new SolidColorBrush(Color.FromArgb(255, 244, 67, 54)); // Red
                RiskBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 244, 67, 54));
                RiskBadgeForeground = new SolidColorBrush(Color.FromArgb(255, 244, 67, 54));
                break;
        }

        OnPropertyChanged(nameof(RiskColor));
        OnPropertyChanged(nameof(RiskBadgeBackground));
        OnPropertyChanged(nameof(RiskBadgeForeground));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
