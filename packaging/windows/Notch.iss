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
AppName=Notch
AppVersion={#AppVersion}
AppPublisher=SuryaK999
AppPublisherURL=https://github.com/SuryaK999/Notch-win-linux
AppSupportURL=https://github.com/SuryaK999/Notch-win-linux/issues
AppUpdatesURL=https://github.com/SuryaK999/Notch-win-linux/releases
DefaultDirName={localappdata}\Programs\Notch
DefaultGroupName=Notch
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19045
WizardStyle=modern
SetupIconFile=..\..\src\Notch.Windows\Assets\Notch.ico
UninstallDisplayIcon={app}\app\{#AppVersion}\Notch.Windows.exe
UninstallDisplayName=Notch
LicenseFile=..\..\docs\product-terms.md
InfoBeforeFile=..\..\docs\privacy.md
OutputDir={#ReleaseDirectory}
OutputBaseFilename=Notch-{#AppVersion}-windows-x64-setup
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
SignTool=notch
SignedUninstaller=yes
Uninstallable=yes
UsePreviousAppDir=yes
AppMutex=Notch.Desktop.Running

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}\app\{#AppVersion}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Notch"; Filename: "{app}\app\{#AppVersion}\Notch.Windows.exe"; WorkingDir: "{app}\app\{#AppVersion}"
Name: "{group}\Uninstall Notch"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\app\{#AppVersion}\Notch.Windows.exe"; Description: "Open Notch"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CheckForMutexes('Notch.Desktop.Running') then
    Result := 'Save your work and quit Notch from the tray before installing. Setup does not forcibly terminate Notch.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := not CheckForMutexes('Notch.Desktop.Running');
  if not Result then
    MsgBox('Save your work and quit Notch from the tray before uninstalling. Your workspace and vault credentials are retained.', mbInformation, MB_OK);
end;

// No Delete entries target %LOCALAPPDATA%\Notch. Upgrades install into a new
// version directory; the preceding executable remains available for recovery.
// Inno Setup restores files on a cancelled/failed install. The same AppId keeps
// one uninstall registration and merges tracked application files across upgrades.
