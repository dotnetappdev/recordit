; RecordIt Avalonia Edition — Inno Setup installer script
; Build with:  iscc installers\RecordIt.Avalonia.iss
; The CI downloads FFmpeg into tools\ffmpeg\ before running this script.

#define AppName      "RecordIt (Avalonia)"
#define AppShortName "RecordIt"
#define AppVersion   "1.0.0"
#define AppPublisher "Alvonia"
#define AppURL       "https://github.com/dotnetappdev/recordit"
#define AppExeName   "RecordIt.Avalonia.exe"

; Path to the dotnet publish output — can be overridden on the CLI:
;   iscc /DPublishDir=path\to\publish RecordIt.Avalonia.iss
#ifndef PublishDir
  #define PublishDir "..\RecordIt.Avalonia\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
#endif

[Setup]
AppId={{E8A3F1C2-7B4D-4E9F-A012-3C5D6E7F8A9B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppShortName}
DefaultGroupName={#AppShortName}
AllowNoIcons=yes
; Self-contained publish — no .NET prerequisite needed
OutputDir=.\output
OutputBaseFilename=RecordIt-Avalonia-{#AppVersion}-Setup
SetupIconFile=..\RecordIt.Avalonia\Assets\app-icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start RecordIt with Windows"; GroupDescription: "Startup"; Flags: unchecked

[Files]
; Main application (self-contained .NET publish output)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppShortName}";       Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppShortName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppShortName}";   Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#AppShortName}";    Filename: "{app}\{#AppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
// Verify the publish directory exists before starting
function InitializeSetup(): Boolean;
begin
  if not DirExists(ExpandConstant('{#PublishDir}')) then
  begin
    MsgBox('Publish output not found at: {#PublishDir}'#13#10 +
           'Run "dotnet publish" for the Avalonia project first.', mbError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;
