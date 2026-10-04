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
#ifdef EvaluationBuild
OutputBaseFilename=Notchling-{#AppVersion}-windows-x64-evaluation-setup
#else
OutputBaseFilename=Notchling-{#AppVersion}-windows-x64-setup
#endif
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
#ifndef EvaluationBuild
SignTool=notch
SignedUninstaller=yes
#endif
Uninstallable=yes
; Keep the established installation directory and AppId for upgrade continuity.
UsePreviousAppDir=yes
AppMutex=Notch.Desktop.Running

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}\app\{#AppVersion}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "install-prerequisites.ps1"; Flags: dontcopy

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
var
  ResultCode: Integer;
  ResultFile: String;
  ResultText: AnsiString;
  Parameters: String;
begin
  Result := '';
  if CheckForMutexes('Notch.Desktop.Running') then begin
    Result := 'Save your work and quit Notchling from the tray before installing. Setup does not forcibly terminate Notchling.';
    Exit;
  end;

  ExtractTemporaryFile('install-prerequisites.ps1');
  ResultFile := ExpandConstant('{tmp}\Notchling-prerequisites-result.txt');
  Parameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{tmp}\install-prerequisites.ps1') + '" -ResultPath "' + ResultFile + '"';
  WizardForm.PreparingLabel.Caption := 'Preparing shared Windows components, only if needed. A Microsoft permission prompt may appear.';
  Log('Checking Notchling shared runtime prerequisites.');
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then begin
    Result := 'Setup could not start the Windows component check. Restart Windows and try Setup again.';
    Exit;
  end;
  Log('Notchling prerequisite helper returned ' + IntToStr(ResultCode) + '.');
  if ResultCode = 3010 then begin
    NeedsRestart := True;
    Result := 'Restart Windows to finish preparing shared components, then run Notchling Setup again.';
    Exit;
  end;
  if ResultCode <> 0 then begin
    if LoadStringFromFile(ResultFile, ResultText) then
      Result := Trim(UTF8Decode(ResultText))
    else
      Result := 'Setup could not prepare the shared Windows components. Check your internet connection, then try Setup again. Details are in %LOCALAPPDATA%\Notchling\Setup\Logs\setup-prerequisites.log.';
  end;
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
