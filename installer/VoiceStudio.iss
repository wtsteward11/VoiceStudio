; VoiceStudio Quantum+ Inno Setup Installer Script
; Professional Voice Cloning Studio Installer

#define MyAppName "VoiceStudio Quantum+"

; Allow build scripts to override version/exe name (e.g., ISCC.exe /DMyAppVersion=1.2.3)
#ifndef MyAppVersion
#define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "VoiceStudio"
#define MyAppURL "https://voicestudio.example"

#ifndef MyAppExeName
#define MyAppExeName "VoiceStudio.App.exe"
#endif

; Gate C publish output is the canonical frontend artifact (unpackaged apphost EXE).
; Override when needed (e.g., ISCC.exe /DMyAppSourceDir="..\path\to\publish").
#ifndef MyAppSourceDir
#define MyAppSourceDir "..\.buildlogs\x64\Release\gatec-publish"
#endif
#define MyAppId "A1B2C3D4-E5F6-4A5B-8C9D-0E1F2A3B4C5D"

; Include shared prerequisite detection functions
#include "prerequisites.iss"

[Setup]
; Basic Information
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\VoiceStudio
DefaultGroupName=VoiceStudio
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=VoiceStudio-Setup-v{#MyAppVersion}
; Optional setup icon (only if present). Inno Setup requires an .ico file.
#ifexist "..\src\VoiceStudio.App\Assets\icon.ico"
SetupIconFile=..\src\VoiceStudio.App\Assets\icon.ico
#endif
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.18362

; Uninstall
UninstallDisplayIcon={app}\App\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
; Frontend Application
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs

; Backend Files
Source: "..\backend\api\*.py"; DestDir: "{app}\Backend\api"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\backend\api\routes\*.py"; DestDir: "{app}\Backend\api\routes"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\backend\api\ws\*.py"; DestDir: "{app}\Backend\api\ws"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\backend\requirements.txt"; DestDir: "{app}\Backend"; Flags: ignoreversion

; Core Engine Files
Source: "..\app\core\engines\*.py"; DestDir: "{app}\Core\engines"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\app\core\audio\*.py"; DestDir: "{app}\Core\audio"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\app\core\runtime\*.py"; DestDir: "{app}\Core\runtime"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\app\core\training\*.py"; DestDir: "{app}\Core\training"; Flags: ignoreversion recursesubdirs createallsubdirs

; Engine Manifests
; NOTE: Inno Setup does not support wildcards in the directory portion of Source,
; so we include each category root and recurse.
Source: "..\engines\audio\*"; DestDir: "{app}\Engines\audio"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\engines\image\*"; DestDir: "{app}\Engines\image"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\engines\video\*"; DestDir: "{app}\Engines\video"; Flags: ignoreversion recursesubdirs createallsubdirs

; Documentation
Source: "..\docs\user\*.md"; DestDir: "{app}\Docs\user"; Flags: ignoreversion
Source: "..\docs\api\*.md"; DestDir: "{app}\Docs\api"; Flags: ignoreversion
Source: "..\docs\developer\*.md"; DestDir: "{app}\Docs\developer"; Flags: ignoreversion

; Backend Services and Data layer
Source: "..\backend\services\*.py"; DestDir: "{app}\Backend\services"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\backend\data\*.py"; DestDir: "{app}\Backend\data"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\backend\integrations\*.py"; DestDir: "{app}\Backend\integrations"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\backend\security\*.py"; DestDir: "{app}\Backend\security"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\backend\monitoring\*.py"; DestDir: "{app}\Backend\monitoring"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\backend\settings.py"; DestDir: "{app}\Backend"; Flags: ignoreversion
Source: "..\backend\__init__.py"; DestDir: "{app}\Backend"; Flags: ignoreversion

; App core utilities
Source: "..\app\core\utils\*.py"; DestDir: "{app}\Core\utils"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\app\core\tasks\*.py"; DestDir: "{app}\Core\tasks"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\app\__init__.py"; DestDir: "{app}\app"; Flags: ignoreversion
Source: "..\app\core\__init__.py"; DestDir: "{app}\Core"; Flags: ignoreversion

; Shared schemas
Source: "..\shared\*"; DestDir: "{app}\Shared"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: DirExists(ExpandConstant('{src}\..\shared'))

; LLM engine manifests
Source: "..\engines\llm\*"; DestDir: "{app}\Engines\llm"; Flags: ignoreversion recursesubdirs createallsubdirs

; Bundled Python Runtime (if prepared by build script)
Source: "..\installer\runtime\python\*"; DestDir: "{app}\Runtime\python"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: DirExists(ExpandConstant('{src}\..\runtime\python'))

; Bundled FFmpeg (if prepared by build script)
Source: "..\installer\runtime\ffmpeg\*"; DestDir: "{app}\Runtime\ffmpeg"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: DirExists(ExpandConstant('{src}\..\runtime\ffmpeg'))

; Requirements files (for manual pip install)
Source: "..\requirements.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\requirements_engines.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\App\{#MyAppExeName}"
Name: "{group}\User Manual"; Filename: "{app}\Docs\user\USER_MANUAL.md"
Name: "{group}\Getting Started"; Filename: "{app}\Docs\user\GETTING_STARTED.md"
Name: "{group}\API Documentation"; Filename: "{app}\Docs\api\API_REFERENCE.md"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\App\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\App\{#MyAppExeName}"; Tasks: quicklaunchicon

[Registry]
; File Associations
Root: HKCR; Subkey: ".voiceproj"; ValueType: string; ValueName: ""; ValueData: "VoiceStudio.Project"; Flags: uninsdeletevalue
Root: HKCR; Subkey: "VoiceStudio.Project"; ValueType: string; ValueName: ""; ValueData: "VoiceStudio Project File"; Flags: uninsdeletekey
Root: HKCR; Subkey: "VoiceStudio.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\App\{#MyAppExeName},0"
Root: HKCR; Subkey: "VoiceStudio.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\App\{#MyAppExeName}"" ""%1"""

Root: HKCR; Subkey: ".vprofile"; ValueType: string; ValueName: ""; ValueData: "VoiceStudio.Profile"; Flags: uninsdeletevalue
Root: HKCR; Subkey: "VoiceStudio.Profile"; ValueType: string; ValueName: ""; ValueData: "VoiceStudio Voice Profile"; Flags: uninsdeletekey
Root: HKCR; Subkey: "VoiceStudio.Profile\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\App\{#MyAppExeName},1"
Root: HKCR; Subkey: "VoiceStudio.Profile\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\App\{#MyAppExeName}"" ""%1"""

; Application Registry
Root: HKLM; Subkey: "SOFTWARE\VoiceStudio"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\VoiceStudio"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\VoiceStudio"; ValueType: string; ValueName: "DataPath"; ValueData: "{commonappdata}\VoiceStudio"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\VoiceStudio"; ValueType: string; ValueName: "ModelsPath"; ValueData: "{commonappdata}\VoiceStudio\models"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\VoiceStudio"; ValueType: string; ValueName: "CachePath"; ValueData: "{commonappdata}\VoiceStudio\cache"; Flags: uninsdeletekey

; Environment Variables (user-level so they persist without admin)
Root: HKCU; Subkey: "Environment"; ValueType: string; ValueName: "VOICESTUDIO_MODELS_PATH"; ValueData: "{commonappdata}\VoiceStudio\models"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Environment"; ValueType: string; ValueName: "VOICESTUDIO_FFMPEG_PATH"; ValueData: "{app}\Runtime\ffmpeg\ffmpeg.exe"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Environment"; ValueType: string; ValueName: "VOICESTUDIO_CACHE_PATH"; ValueData: "{commonappdata}\VoiceStudio\cache"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Environment"; ValueType: string; ValueName: "VOICESTUDIO_LOGS_PATH"; ValueData: "{commonappdata}\VoiceStudio\logs"; Flags: uninsdeletevalue

[Run]
; Install Python Packages (prefer bundled runtime, fall back to system Python)
Filename: "{app}\Runtime\python\python.exe"; Parameters: "-m pip install -r ""{app}\Backend\requirements.txt"" --quiet"; StatusMsg: "Installing Python packages (bundled runtime)..."; Check: FileExists(ExpandConstant('{app}\Runtime\python\python.exe')); Flags: runhidden
Filename: "python"; Parameters: "-m pip install -r ""{app}\Backend\requirements.txt"" --quiet"; StatusMsg: "Installing Python packages (system Python)..."; Check: (not FileExists(ExpandConstant('{app}\Runtime\python\python.exe'))) and IsPythonInstalled; Flags: runhidden

; Create User Data Directories
Filename: "powershell"; Parameters: "-Command ""New-Item -ItemType Directory -Force -Path $env:APPDATA\VoiceStudio"""; StatusMsg: "Creating user data directories..."; Flags: runhidden

[Dirs]
; Create data directories with appropriate permissions
Name: "{commonappdata}\VoiceStudio"; Permissions: users-modify
Name: "{commonappdata}\VoiceStudio\models"; Permissions: users-modify
Name: "{commonappdata}\VoiceStudio\cache"; Permissions: users-modify
Name: "{commonappdata}\VoiceStudio\logs"; Permissions: users-modify

[UninstallDelete]
; Cleanup cache and log directories on uninstall (user data in %APPDATA% preserved)
Type: filesandordirs; Name: "{commonappdata}\VoiceStudio\cache"
Type: filesandordirs; Name: "{commonappdata}\VoiceStudio\logs"
Type: filesandordirs; Name: "{commonappdata}\VoiceStudio\temp"

[Code]
// ============================================================================
// Version Detection and Upgrade Path Functions
// ============================================================================

function GetPreviousVersion: String;
var
  Version: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM64, 'SOFTWARE\VoiceStudio', 'Version', Version) then
    Result := Version;
end;

function GetPreviousInstallPath: String;
var
  InstallPath: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM64, 'SOFTWARE\VoiceStudio', 'InstallPath', InstallPath) then
    Result := InstallPath;
end;

function IsUpgrade: Boolean;
begin
  Result := GetPreviousVersion <> '';
end;

function CompareVersions(V1, V2: String): Integer;
var
  V1Parts, V2Parts: array of String;
  V1Num, V2Num, I: Integer;
begin
  // Simple version comparison: returns -1 if V1 < V2, 0 if equal, 1 if V1 > V2
  // Format expected: X.Y.Z (major.minor.patch)
  Result := 0;
  
  // Parse V1
  SetArrayLength(V1Parts, 3);
  V1Parts[0] := Copy(V1, 1, Pos('.', V1) - 1);
  V1 := Copy(V1, Pos('.', V1) + 1, Length(V1));
  if Pos('.', V1) > 0 then
  begin
    V1Parts[1] := Copy(V1, 1, Pos('.', V1) - 1);
    V1Parts[2] := Copy(V1, Pos('.', V1) + 1, Length(V1));
  end
  else
  begin
    V1Parts[1] := V1;
    V1Parts[2] := '0';
  end;
  
  // Parse V2
  SetArrayLength(V2Parts, 3);
  V2Parts[0] := Copy(V2, 1, Pos('.', V2) - 1);
  V2 := Copy(V2, Pos('.', V2) + 1, Length(V2));
  if Pos('.', V2) > 0 then
  begin
    V2Parts[1] := Copy(V2, 1, Pos('.', V2) - 1);
    V2Parts[2] := Copy(V2, Pos('.', V2) + 1, Length(V2));
  end
  else
  begin
    V2Parts[1] := V2;
    V2Parts[2] := '0';
  end;
  
  // Compare each part
  for I := 0 to 2 do
  begin
    V1Num := StrToIntDef(V1Parts[I], 0);
    V2Num := StrToIntDef(V2Parts[I], 0);
    if V1Num < V2Num then
    begin
      Result := -1;
      Exit;
    end
    else if V1Num > V2Num then
    begin
      Result := 1;
      Exit;
    end;
  end;
end;

function IsDowngrade: Boolean;
var
  PrevVersion, NewVersion: String;
begin
  Result := False;
  PrevVersion := GetPreviousVersion;
  NewVersion := '{#MyAppVersion}';
  if PrevVersion <> '' then
    Result := CompareVersions(NewVersion, PrevVersion) < 0;
end;

procedure BackupUserSettings;
var
  BackupDir, SettingsPath: String;
  ResultCode: Integer;
begin
  // Create a backup of user settings before upgrade
  BackupDir := ExpandConstant('{commonappdata}\VoiceStudio\backup\pre-upgrade-{#MyAppVersion}');
  SettingsPath := ExpandConstant('{commonappdata}\VoiceStudio\settings');
  
  if DirExists(SettingsPath) then
  begin
    // Create backup directory
    ForceDirectories(BackupDir);
    // Copy settings using xcopy
    Exec('xcopy', '"' + SettingsPath + '" "' + BackupDir + '\settings" /E /I /H /Y', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

// IsPythonInstalled and IsDotNet8DesktopInstalled are now defined in prerequisites.iss

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  
  // Check Windows version
  if not IsWin64 then
  begin
    MsgBox('VoiceStudio Quantum+ requires 64-bit Windows.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // Check .NET 8 Desktop Runtime
  if not IsDotNet8DesktopInstalled then
  begin
    if MsgBox('VoiceStudio Quantum+ requires .NET 8 Desktop Runtime.'#13#10#13#10 +
              'The runtime was not found on this system.'#13#10#13#10 +
              'Would you like to download it from Microsoft?'#13#10#13#10 +
              '(Click Yes to open the download page, or No to cancel installation)',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/en-us/download/dotnet/8.0', '', '', SW_SHOW, ewNoWait, ErrorCode);
    end;
    Result := False;
    Exit;
  end;
  
  // Check Windows App SDK Runtime (required for WinUI 3 unpackaged)
  if not IsWindowsAppSDKInstalled then
  begin
    PromptWindowsAppSDKDownload;
    Result := False;
    Exit;
  end;
  
  // Upgrade path handling
  if IsUpgrade then
  begin
    Log('Upgrade detected from version: ' + GetPreviousVersion);
    
    // Warn about downgrade (skip in silent mode - Gate H lifecycle requires non-interactive rollback)
    if IsDowngrade and not WizardSilent() then
    begin
      if MsgBox('WARNING: You are about to install an older version ({#MyAppVersion}).'#13#10#13#10 +
                'Currently installed: ' + GetPreviousVersion + #13#10#13#10 +
                'Downgrading may cause compatibility issues with your existing projects.'#13#10#13#10 +
                'Are you sure you want to continue?',
                mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end;
    
    // Backup user settings before upgrade
    BackupUserSettings;
    Log('User settings backed up before upgrade.');
  end
  else
  begin
    Log('Fresh installation detected.');
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  
  // IMPORTANT: Gate H lifecycle proof runs the uninstaller in silent mode.
  // Do not show interactive prompts during uninstall; keep user data by default for determinism.
  //
  // NOTE: CmdLineParamExists is an Inno Setup Preprocessor helper (compile-time), not a Pascal Script API.
  // Calling it here breaks compilation with "Unknown identifier".
  //
  // If user data removal is ever needed, document it as a manual step outside Gate H automation.
end;

