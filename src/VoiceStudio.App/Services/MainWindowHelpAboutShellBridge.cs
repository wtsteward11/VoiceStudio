// VoiceStudio — GAP-008 Slice 28: MainWindow Help menu — documentation folder + About dialog (bounded).

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;

namespace VoiceStudio.App.Services;

/// <summary>
/// Help menu: open local <c>docs</c> in Explorer; About dialog with version and third-party license link.
/// </summary>
public sealed class MainWindowHelpAboutShellBridge
{
    public void OpenDocumentationFolder(
        Func<string?> getVoicestudioRepoRoot,
        string appBaseDirectory,
        Action<string, string> showWarning,
        Action<Exception, string> logError,
        Action<string, string> showError)
    {
        ArgumentNullException.ThrowIfNull(getVoicestudioRepoRoot);
        ArgumentNullException.ThrowIfNull(appBaseDirectory);
        ArgumentNullException.ThrowIfNull(showWarning);
        ArgumentNullException.ThrowIfNull(logError);
        ArgumentNullException.ThrowIfNull(showError);

        var repoRoot = getVoicestudioRepoRoot();
        var docsPath = repoRoot != null
            ? Path.Combine(repoRoot, "docs")
            : Path.Combine(appBaseDirectory, "docs");
        try
        {
            if (!Directory.Exists(docsPath))
            {
                showWarning(
                    $"Docs folder not found: {docsPath}",
                    "Documentation");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{docsPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            logError(ex, "OpenDocumentationFolder");
            showError(
                "Unable to open documentation folder.",
                "Documentation");
        }
    }

    public async Task ShowAboutDialogAsync(
        Func<XamlRoot?> getXamlRoot,
        Action<Exception, string> logError,
        Action<string, string> showError)
    {
        ArgumentNullException.ThrowIfNull(getXamlRoot);
        ArgumentNullException.ThrowIfNull(logError);
        ArgumentNullException.ThrowIfNull(showError);

        try
        {
            var xamlRoot = getXamlRoot();
            if (xamlRoot == null)
            {
                return;
            }

            var version = Package.Current.Id.Version;
            var versionText = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            var aboutPanel = new StackPanel { Spacing = 8 };
            aboutPanel.Children.Add(new TextBlock { Text = $"Version {versionText}" });
            aboutPanel.Children.Add(new TextBlock { Text = "Local-first voice production studio", Opacity = 0.7 });

            var licenseLink = new HyperlinkButton
            {
                Content = "View Third-Party Licenses",
                NavigateUri = new Uri("https://github.com/wtsteward11/VoiceStudio/blob/main/THIRD_PARTY_LICENSES.md")
            };
            aboutPanel.Children.Add(licenseLink);

            aboutPanel.Children.Add(new TextBlock
            {
                Text = "License file: THIRD_PARTY_LICENSES.md (repo root)",
                Opacity = 0.5,
                FontSize = 11
            });

            var dialog = new ContentDialog
            {
                Title = "VoiceStudio Quantum+",
                Content = aboutPanel,
                CloseButtonText = "Close",
                XamlRoot = xamlRoot
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            logError(ex, "ShowAboutDialog");
            showError(
                "Unable to show About dialog.",
                "About");
        }
    }
}
