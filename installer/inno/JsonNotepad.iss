; JsonNotepad - classic Windows installer (Inno Setup 6.3+)
;
; Normally compiled by build/package-inno.ps1, which publishes win-x64 and runs:
;   ISCC.exe /DAppVersion=1.2.3 /DSourceDir=<repo>\artifacts\publish\win-x64 /DOutputDir=<repo>\artifacts installer\inno\JsonNotepad.iss
;
; AppVersion is required and comes from VersionPrefix in Directory.Build.props (single source of truth).
; The installer supports both per-user (no admin, default) and per-machine installs; the user picks in a dialog,
; or pass /ALLUSERS or /CURRENTUSER on the setup.exe command line.

#ifndef AppVersion
  #error AppVersion is not defined. Compile with ISCC /DAppVersion=x.y.z (build/package-inno.ps1 does this for you).
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

; /DFrameworkDependent=1 builds the small installer for a framework-dependent publish.
; It checks for the .NET 9 Desktop Runtime (x64) and offers to open the download page.
#ifdef FrameworkDependent
  #define SetupSuffix "_x64_fd_setup"
#else
  #define SetupSuffix "_x64_setup"
#endif

#define AppName "JsonNotepad"
#define AppExeName "JsonNotepad.exe"
#define AppPublisher "JsonNotepad Contributors"
#define AppUrl "https://github.com/ismailian/JsonNotepad"
#define ProgId "JsonNotepad.json"

#if !FileExists(AddBackslash(SourceDir) + AppExeName)
  #error JsonNotepad.exe not found in SourceDir. Publish win-x64 first (build/package-inno.ps1 does this).
#endif

[Setup]
; AppId identifies the product for upgrades/uninstall. NEVER change it after the first release.
AppId={{7B8916C6-313D-436D-8E1B-2FD82DC93DD2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
; Per-user by default; offer per-machine (elevates) in a dialog or via /ALLUSERS.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}_{#AppVersion}{#SetupSuffix}
SetupIconFile=..\..\src\JsonNotepad.App\Resources\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
#if FileExists("..\..\LICENSE")
LicenseFile=..\..\LICENSE
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "fileassoc"; Description: "Register JsonNotepad for .json files (adds ""Open with JsonNotepad"")"; GroupDescription: "File associations:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "View and edit JSON documents"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; HKA = HKCU for per-user installs, HKLM for per-machine installs.
; ProgID describing a JsonNotepad-opened JSON document.
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "JSON Document"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "JSON Document"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"",0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open"; ValueType: string; ValueName: ""; ValueData: "Open with JsonNotepad"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: fileassoc
; Offer the ProgID for .json ("Open with" list / default-app picker). Windows 10+ lets the user choose the default.
Root: HKA; Subkey: "Software\Classes\.json\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
; Explicit "Open with JsonNotepad" context-menu verb on .json files, regardless of the current default app.
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.json\shell\OpenWithJsonNotepad"; ValueType: string; ValueName: ""; ValueData: "Open with JsonNotepad"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.json\shell\OpenWithJsonNotepad"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"",0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.json\shell\OpenWithJsonNotepad\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: fileassoc
; Application registration (friendly name in "Open with", supported types).
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".json"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".jsonc"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".geojson"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".har"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".webmanifest"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""
; "App Paths" lets Win+R / Start-Process find "jsonnotepad" without PATH changes.
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#AppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Files the app may create next to itself (logs, caches) - the folder is removed completely.
Type: filesandordirs; Name: "{app}"

[Code]
#ifdef FrameworkDependent
const
  DotNetDownloadUrl = 'https://dotnet.microsoft.com/download/dotnet/9.0';

// True when any 9.x version of Microsoft.WindowsDesktop.App (x64) is registered.
function IsDesktopRuntime9Installed(): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 2) = '9.' then
      begin
        Result := True;
        Exit;
      end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if IsDesktopRuntime9Installed() then
    Exit;
  if WizardSilent() then
  begin
    Log('.NET 9 Desktop Runtime (x64) not found; continuing silent install anyway.');
    Exit;
  end;
  case MsgBox('JsonNotepad needs the .NET 9 Desktop Runtime (x64), which is not installed on this PC.' + #13#10#13#10 +
              'Yes: open the download page and cancel setup (run setup again afterwards).' + #13#10 +
              'No: install JsonNotepad anyway.' + #13#10 +
              'Cancel: quit setup.', mbConfirmation, MB_YESNOCANCEL) of
    IDYES:
      begin
        ShellExec('open', DotNetDownloadUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
        Result := False;
      end;
    IDCANCEL:
      Result := False;
  end;
end;
#endif

// On uninstall, offer to remove per-user settings as well (skipped silently for /SILENT uninstalls).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RoamingDir, LocalDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  RoamingDir := ExpandConstant('{userappdata}\{#AppName}');
  LocalDir := ExpandConstant('{localappdata}\{#AppName}');
  if not DirExists(RoamingDir) and not DirExists(LocalDir) then
    Exit;

  if UninstallSilent then
    Exit;

  if MsgBox('Do you also want to delete your JsonNotepad settings (recent files, window layout)?',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    DelTree(RoamingDir, True, True, True);
    DelTree(LocalDir, True, True, True);
  end;
end;
