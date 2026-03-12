# WinUI 3 Migration Guide

This guide documents WPF-to-WinUI differences and lessons learned during VoiceStudio development. Use this when migrating code or troubleshooting WinUI-specific issues.

## Quick Reference: Common Migration Pitfalls

| WPF Pattern | WinUI 3 Equivalent | Notes |
|-------------|-------------------|-------|
| `StringFormat="{0:P0}"` | Use `IValueConverter` | WinUI bindings don't support StringFormat |
| `StaticResource` | `ThemeResource` (preferred) | ThemeResource responds to theme changes |
| `Dispatcher.Invoke()` | `DispatcherQueue.TryEnqueue()` | Different threading model |
| `Window.Current` | `App.Window` or constructor injection | No global Window.Current in WinUI |
| `ContentDialog.ShowAsync()` | Set `XamlRoot` first | Requires explicit XamlRoot assignment |
| `Application.Resources["Key"]` | Same, but merge order matters | Check ResourceDictionary merge order |

---

## 1. XAML Binding Differences

### StringFormat is NOT Supported

**Problem**: WinUI XAML compiler silently crashes on `StringFormat` in bindings.

```xml
<!-- ❌ INVALID - Causes silent XAML compiler crash -->
<TextBlock Text="{Binding Percentage, StringFormat='{0:P0}'}" />
<TextBlock AutomationProperties.Name="{Binding Name, StringFormat='Selected: {0}'}" />
```

**Solution**: Use a converter or format in the ViewModel.

```xml
<!-- ✅ Option 1: Use a converter -->
<TextBlock Text="{Binding Percentage, Converter={StaticResource PercentageConverter}}" />

<!-- ✅ Option 2: Use x:Bind with a method -->
<TextBlock Text="{x:Bind FormatPercentage(ViewModel.Percentage), Mode=OneWay}" />
```

**ViewModel approach**:
```csharp
// In ViewModel
public string PercentageDisplay => $"{Percentage:P0}";
```

### x:Bind vs Binding

| Feature | `{Binding}` | `{x:Bind}` |
|---------|-------------|------------|
| Compile-time validation | No | Yes |
| Default mode | OneWay | OneTime |
| Performance | Slower | Faster |
| Function binding | No | Yes |
| DataContext required | Yes | No (uses code-behind) |

**Recommendation**: Use `{x:Bind}` for all new code where possible.

```xml
<!-- Set x:DataType for compile-time validation -->
<Page x:DataType="viewmodels:MyViewModel">
    <TextBlock Text="{x:Bind ViewModel.Title, Mode=OneWay}" />
</Page>
```

---

## 2. ResourceDictionary Classification

### Problem: ResourceDictionaries Compiled as Pages

**Symptom**: XAML compiler crashes with no error message.

**Cause**: Files in `Resources/` folders may be auto-classified as `Page` items.

**Solution**: Explicitly exclude ResourceDictionary files from Page compilation.

```xml
<!-- In .csproj -->
<ItemGroup>
  <!-- ResourceDictionaries must NOT be compiled as Pages -->
  <Page Remove="Resources\Styles\*.xaml" />
  <None Include="Resources\Styles\*.xaml">
    <SubType>Designer</SubType>
  </None>
</ItemGroup>
```

### Resource Merge Order

Resources must be merged in dependency order:

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <!-- 1. Design tokens (colors, spacing) -->
            <ResourceDictionary Source="ms-appx:///VoiceStudio.Common.UI/Themes/DesignTokens.xaml"/>
            <!-- 2. Base styles (depend on tokens) -->
            <ResourceDictionary Source="ms-appx:///VoiceStudio.Common.UI/Themes/Styles.xaml"/>
            <!-- 3. Component-specific (depend on base styles) -->
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

---

## 3. XamlRoot and Constructor Async

**Rule:** If code touches `XamlRoot`, Popup, ContentDialog, Flyout, or any compositor-hosted visual, it must run at or after `Loaded`. A constructor guard like `rootFE.XamlRoot != null` is always false during construction—dead code.

WinUI 3 populates `XamlRoot` only after the Window's content is in the live compositor tree (after `Loaded` fires). Fire-and-forget async from a Window or Page constructor that eventually creates Popup/ContentDialog will throw `COMException` 0x8000FFFF ("Catastrophic failure — XamlRoot must be explicitly set for unparented popup").

```csharp
// ❌ WRONG - XamlRoot is null in constructor
public MainWindow()
{
    _ = InitializePanelsAsync(...);  // Panels create popups → crash
}

// ✅ CORRECT - Defer to Loaded
if (this.Content is FrameworkElement contentFE)
{
    contentFE.Loaded += async (s, e) =>
    {
        ErrorDialogService.Root = contentFE.XamlRoot;  // Now non-null
        _ = InitializePanelsAsync(...);
    };
}
```

See [ADR-047: WinUI 3 XamlRoot Deferral Pattern](../architecture/decisions/ADR-047-winui-xamlroot-deferral-pattern.md).

---

## 4. Threading and Dispatcher

### DispatcherQueue vs Dispatcher

```csharp
// ❌ WPF pattern - doesn't work
Application.Current.Dispatcher.Invoke(() => { ... });

// ✅ WinUI pattern
DispatcherQueue.GetForCurrentThread().TryEnqueue(() => { ... });

// ✅ From a ViewModel with IViewModelContext
_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => 
{
    // UI updates here
});
```

### Async Considerations

```csharp
// ✅ Capture dispatcher before async operation
var dispatcher = DispatcherQueue.GetForCurrentThread();
await Task.Run(() => 
{
    // Background work
});
dispatcher.TryEnqueue(() => 
{
    // Update UI
});
```

---

## 5. Window Management

### No Window.Current

```csharp
// ❌ WPF pattern
var window = Window.Current;

// ✅ WinUI patterns
// Option 1: Store reference at startup
public static Window? MainWindowInstance { get; private set; }

// Option 2: Pass through constructor
public MyPage(Window parentWindow) { ... }

// Option 3: Use App-level property
var window = App.MainWindowInstance;
```

### Getting HWND (for P/Invoke)

```csharp
var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
```

---

## 6. ContentDialog Best Practices

### XamlRoot is Required

```csharp
// ❌ Will throw - no XamlRoot
var dialog = new ContentDialog { Title = "Error" };
await dialog.ShowAsync();

// ✅ Correct pattern
var dialog = new ContentDialog
{
    Title = "Error",
    Content = "Something went wrong",
    CloseButtonText = "OK",
    XamlRoot = this.XamlRoot  // Required!
};
await dialog.ShowAsync();
```

### Only One Dialog at a Time

```csharp
private static bool _dialogOpen = false;

public async Task ShowDialogAsync(string message)
{
    if (_dialogOpen) return;
    _dialogOpen = true;
    
    try
    {
        var dialog = new ContentDialog { ... };
        await dialog.ShowAsync();
    }
    finally
    {
        _dialogOpen = false;
    }
}
```

---

## 7. XAML Compiler Limits

### Page Count Threshold

The WinUI XAML compiler can struggle with large numbers of XAML pages (~150+). Signs of hitting this limit:

- Silent exit code 1 with no error message
- Build takes increasingly long
- Intermittent failures

**Mitigations**:

1. **Split into modules**: Move Views to separate assemblies
2. **Use wrapper script**: `tools/xaml-compiler-wrapper.cmd` for diagnostics
3. **Disable XBF generation** during development:
   ```xml
   <DisableXbfGeneration>true</DisableXbfGeneration>
   ```

### Debugging XAML Compiler Issues

```powershell
# Enable debug logging
$env:VSQ_XAML_DEBUG = "1"
$env:VSQ_XAML_RAW_LOG = "1"
dotnet build VoiceStudio.App.csproj

# Check generated logs
Get-Content "xaml_compiler_raw_*.log"
```

---

## 8. Control Differences

### Missing Controls

| WPF Control | WinUI Equivalent |
|-------------|------------------|
| `Expander` | Community Toolkit or custom |
| `NumericUpDown` | `NumberBox` |
| `Calendar` | `CalendarView` |
| `DataGrid` | Community Toolkit `DataGrid` |

### Behavior Changes

**ScrollViewer**: Different default virtualization settings
**ListView**: ItemClick vs SelectionChanged semantics differ

---

## 9. App Lifecycle

### Unpackaged App Bootstrap

For unpackaged WinUI apps (no MSIX):

```csharp
// In App constructor or Main
Microsoft.Windows.AppLifecycle.DecisionMaker.Initialize();
```

### Single Instance

```csharp
// Ensure single instance
var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("VoiceStudio");
if (!mainInstance.IsCurrent)
{
    // Redirect to existing instance
    mainInstance.RedirectActivationToAsync(args).AsTask().Wait();
    return;
}
```

---

## 10. Testing Considerations

### UI Testing

- Use `Microsoft.UI.Xaml.Testing` for unit tests
- Smoke tests require actual window creation
- Mock services via DI, not static accessors

### Binding Failure Detection

```csharp
// Enable in debug builds
#if DEBUG
DebugSettings.FailFastOnErrors = true;
DebugSettings.EnableFrameRateCounter = true;
#endif
```

---

## References

- [WinUI 3 Migration Guide (Microsoft)](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/migrate-to-windows-app-sdk-ovw)
- [Windows App SDK Samples](https://github.com/microsoft/WindowsAppSDK-Samples)
- [ADR-023: UI Assembly Split](../architecture/decisions/ADR-023-ui-assembly-split.md)
- [ISSUE-XAML-COMPILER-LIMIT](../issues/ISSUE-XAML-COMPILER-LIMIT.md)
