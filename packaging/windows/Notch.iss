#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PublishDirectory
  #error PublishDirectory is required
#endif
#ifndef ReleaseDirectory
  #error ReleaseDirectory is required
#endif

[Setup]
AppId={{53529908-CB73-4B48-936A-74B9A6D47B81}
AppName=Notchling
AppVersion={#AppVersion}
AppPublisher=SuryaK999
AppPublisherURL=https://github.com/SuryaK999/Notch-win-linux
AppSupportURL=https://github.com/SuryaK999/Notch-win-linux/issues
AppUpdatesURL=https://github.com/SuryaK999/Notch-win-linux/releases
DefaultDirName={localappdata}\Programs\Notch
DefaultGroupName=Notchling
UsePreviousGroup=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19045
WizardStyle=modern
SetupIconFile=..\..\src\Notch.Windows\Assets\Notchling.ico
UninstallDisplayIcon={app}\app\{#AppVersion}\Notchling.Windows.exe
UninstallDisplayName=Notchling
LicenseFile=..\..\docs\product-terms.md
InfoBeforeFile=..\..\docs\privacy.md
OutputDir={#ReleaseDirectory}
OutputBaseFilename=Notchling-{#AppVersion}-windows-x64-setup
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
SignTool=notch
SignedUninstaller=yes
Uninstallable=yes
; Keep the established installation directory and AppId for upgrade continuity.
UsePreviousAppDir=yes
AppMutex=Notch.Desktop.Running

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}\app\{#AppVersion}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Notchling"; Filename: "{app}\app\{#AppVersion}\Notchling.Windows.exe"; WorkingDir: "{app}\app\{#AppVersion}"
Name: "{group}\Uninstall Notchling"; Filename: "{uninstallexe}"

[InstallDelete]
; Remove only obsolete shortcuts created by the pre-branding installer.
Type: files; Name: "{userprograms}\Notch\Notch.lnk"
Type: files; Name: "{userprograms}\Notch\Uninstall Notch.lnk"
Type: dirifempty; Name: "{userprograms}\Notch"

[Run]
Filename: "{app}\app\{#AppVersion}\Notchling.Windows.exe"; Description: "Open Notchling"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CheckForMutexes('Notch.Desktop.Running') then
    Result := 'Save your work and quit Notchling from the tray before installing. Setup does not forcibly terminate Notchling.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := not CheckForMutexes('Notch.Desktop.Running');
  if not Result then
    MsgBox('Save your work and quit Notchling from the tray before uninstalling. Your workspace and vault credentials are retained.', mbInformation, MB_OK);
end;

// No Delete entries target %LOCALAPPDATA%\Notch. Upgrades install into a new
// version directory; the preceding executable remains available for recovery.
// Inno Setup restores files on a cancelled/failed install. The same AppId keeps
// one uninstall registration and merges tracked application files across upgrades.
