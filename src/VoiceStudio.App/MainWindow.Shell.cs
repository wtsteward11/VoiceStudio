using Microsoft.UI.Xaml;

namespace VoiceStudio.App;

public sealed partial class MainWindow
{
    private void ApplyMicaBackdrop() => _shellChromeShellBridge.ApplyMicaBackdrop();

    private void InitializeCustomTitleBar() => _shellChromeShellBridge.InitializeCustomTitleBar();

    private void UnsubscribeShellChromeEvents() => _shellChromeShellBridge.UnsubscribeShellChromeEvents();
}
