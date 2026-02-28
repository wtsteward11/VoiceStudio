# Working Build Configuration Reference

**Golden Commit**: `b661e694` (Feb 13, 2026)  
**Working Location**: `E:\VoiceStudio-feb13`  
**Built EXE**: `E:\VoiceStudio-feb13\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe`

---

## 1. global.json

```json
{
  "sdk": {
    "version": "8.0.417"
  }
}
```

**Critical**: SDK version `8.0.417`. Do not change to 9.0.x.

---

## 2. Directory.Build.props — Complete File

```xml
<Project>
  <PropertyGroup Condition="'$(MSBuildProjectName)' == 'VoiceStudio.App'">
    <UseXamlCompilerExecutable>true</UseXamlCompilerExecutable>
  </PropertyGroup>

  <PropertyGroup>
    <MicrosoftWindowsAppSDKVersion Condition="'$(WinAppSdkVersionOverride)' != ''">
      $(WinAppSdkVersionOverride)</MicrosoftWindowsAppSDKVersion>
    <MicrosoftWindowsAppSDKVersion Condition="'$(WinAppSdkVersionOverride)' == ''">1.8.251106002</MicrosoftWindowsAppSDKVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Microsoft.WindowsAppSDK" Version="$(MicrosoftWindowsAppSDKVersion)" />
    <PackageVersion Include="Microsoft.WindowsAppSDK.WinUI"
      Version="$(MicrosoftWindowsAppSDKVersion)" />
    <PackageDownload Include="Microsoft.WindowsAppSDK.Runtime"
      Version="[$(MicrosoftWindowsAppSDKVersion)]" />
    <PackageVersion Include="CommunityToolkit.WinUI.UI.Controls" Version="7.1.2" />
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.2.2" />
    <PackageVersion Include="NAudio" Version="2.2.1" />
    <PackageVersion Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.4654" />
  </ItemGroup>
</Project>
```

**Critical Settings**:
- `UseXamlCompilerExecutable` = `true` (forces external compiler process)
- `MicrosoftWindowsAppSDKVersion` = `1.8.251106002`
- **NO `XamlCompilerExePath`** — lets NuGet targets resolve to real compiler, NOT the wrapper

---

## 3. Directory.Build.targets — Key Properties

```xml
<PropertyGroup Condition="'$(MSBuildProjectName)' == 'VoiceStudio.App'">
  <UseXamlCompilerExecutable>true</UseXamlCompilerExecutable>
  <XAMLFingerprint>false</XAMLFingerprint>
  <EnableWin32Codegen>false</EnableWin32Codegen>
  <UseVCMetaManaged>false</UseVCMetaManaged>
  <MarkupCompilePass1DependsOn>ResolveReferences;$(MarkupCompilePass1DependsOn)</MarkupCompilePass1DependsOn>
</PropertyGroup>
```

**Critical**:
- **NO `GenXbfPath` override** — default resolves correctly
- **NO `EnableTypeInfoReflection` override** — NuGet targets handle it
- Contains `EnsureXamlInObj`, `EnsureXamlInObjBeforeCopyToOutput`, `ValidateXamlFilesPresent`, `DetectNestedViewsXaml` targets

---

## 4. VoiceStudio.App.csproj — Key Properties

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
  <UseWinUI>true</UseWinUI>
  <WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
  <EnableDefaultRuntimeIdentifier>true</EnableDefaultRuntimeIdentifier>
  <!-- DisableXbfGeneration is COMMENTED OUT = XBF ENABLED -->
  <!-- <DisableXbfGeneration>true</DisableXbfGeneration> -->
  <UseXamlCompilerExecutable>false</UseXamlCompilerExecutable>
  <EnableWin32Codegen>false</EnableWin32Codegen>
  <UseVCMetaManaged>false</UseVCMetaManaged>
  <DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>
</PropertyGroup>
```

**RuntimeIdentifier Target** (Release only):
```xml
<Target Name="EnsureRuntimeIdentifierForWin2D"
  BeforeTargets="ProcessFrameworkReferences;ResolvePackageAssets"
  Condition="'$(RuntimeIdentifier)' == '' And '$(Configuration)' == 'Release'">
  <PropertyGroup>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
  </PropertyGroup>
</Target>
```

**NuGet Packages**:
```xml
<PackageReference Include="JsonSchema.Net" Version="8.0.5" />
<PackageReference Include="MessagePack" Version="3.1.4" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.2" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
<PackageReference Include="Microsoft.Graphics.Win2D" Version="1.3.2" />
<PackageReference Include="Microsoft.WindowsAppSDK" Version="$(MicrosoftWindowsAppSDKVersion)" />
<PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.4654" />
<PackageReference Include="System.Security.Permissions" Version="6.0.0" />
<PackageReference Include="CommunityToolkit.WinUI.UI.Controls" Version="7.1.2" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
<PackageReference Include="NAudio" Version="2.2.1" />
<PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.1" />
<PackageReference Include="Roslynator.Analyzers" Version="4.11.0" />
```

**PACKAGES THAT MUST NOT BE ADDED** (crash XAML compiler):
- `Microsoft.Extensions.Hosting` (any version ≥9.0.0)
- `Microsoft.Extensions.Configuration.Json` (any version ≥9.0.0)
- `Microsoft.Extensions.Logging` (any version ≥9.0.0)
- Any package that transitively pulls in `Microsoft.Extensions.Configuration.Abstractions` ≥9.0.0

---

## 5. VoiceStudio.Core.csproj — Complete File

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>VoiceStudio.Core</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="VoiceStudio.App" />
    <InternalsVisibleTo Include="VoiceStudio.App.Tests" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
  </ItemGroup>
</Project>
```

---

## 6. Build Command

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
```

Expected result: `Build succeeded. 0 Error(s)`

---

## 7. Launch Command

```powershell
$exe = "E:\VoiceStudio-feb13\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"
Start-Process $exe -WorkingDirectory (Split-Path $exe)
```

---

## 8. Property Override Chain (Evaluation Order)

```
Directory.Build.props          → UseXamlCompilerExecutable = true
VoiceStudio.App.csproj         → UseXamlCompilerExecutable = false (OVERRIDDEN below)
NuGet interop.targets          → (various defaults)
Directory.Build.targets        → UseXamlCompilerExecutable = true (FINAL VALUE)
                               → XAMLFingerprint = false
                               → EnableWin32Codegen = false
                               → UseVCMetaManaged = false
```

The csproj `false` is overridden by targets `true`. This is intentional — forces external compiler process.
