#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PublishDirectory
  #error PublishDirectory is required
#endif
#ifndef ReleaseDirectory
  #error ReleaseDirectory is required
#endif
#ifndef AppArchitecture
  #error AppArchitecture is required (x64, x86 or arm64)
#endif
#if AppArchitecture != "x64" && AppArchitecture != "x86" && AppArchitecture != "arm64"
  #error Unsupported AppArchitecture
#endif

#ifndef InstallerDocumentsDirectory
  #error InstallerDocumentsDirectory is required (generated terms and privacy RTF)
#endif

#ifndef NativeInteropPath
  #error NativeInteropPath is required (the precompiled AnyCPU Setup helper)
#endif

[Setup]
AppId={{53529908-CB73-4B48-936A-74B9A6D47B81}
AppName=Notchling
AppVersion={#AppVersion}
AppPublisher=SuryaK999
AppPublisherURL=https://github.com/Sury2797/Notchling
AppSupportURL=https://github.com/Sury2797/Notchling/issues
AppUpdatesURL=https://github.com/Sury2797/Notchling/releases
DefaultDirName={localappdata}\Programs\Notch
DefaultGroupName=Notchling
UsePreviousGroup=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
#if AppArchitecture == "x64"
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
#elif AppArchitecture == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x86compatible
#endif
MinVersion=10.0.19045
WizardStyle=modern dynamic windows11
WizardSizePercent=120,120
DisableWelcomePage=no
DisableStartupPrompt=yes
WizardImageFile=..\..\src\Notch.Windows\Assets\Notchling.png
WizardImageFileDynamicDark=..\..\src\Notch.Windows\Assets\Notchling.png
WizardSmallImageFile=..\..\src\Notch.Windows\Assets\Notchling.png
WizardSmallImageFileDynamicDark=..\..\src\Notch.Windows\Assets\Notchling.png
WizardImageBackColor=none
WizardImageBackColorDynamicDark=none
WizardSmallImageBackColor=none
WizardSmallImageBackColorDynamicDark=none
SetupIconFile=..\..\src\Notch.Windows\Assets\Notchling.ico
UninstallDisplayIcon={app}\app\{#AppVersion}\Notchling.Windows.exe
UninstallDisplayName=Notchling
LicenseFile={#InstallerDocumentsDirectory}\product-terms.rtf
InfoBeforeFile={#InstallerDocumentsDirectory}\privacy.rtf
OutputDir={#ReleaseDirectory}
#ifdef EvaluationBuild
OutputBaseFilename=Notchling-{#AppVersion}-windows-{#AppArchitecture}-evaluation-setup
#else
OutputBaseFilename=Notchling-{#AppVersion}-windows-{#AppArchitecture}-setup
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

[LangOptions]
DialogFontName=Segoe UI
DialogFontSize=10
WelcomeFontName=Segoe UI
WelcomeFontSize=22

[Messages]
SetupWindowTitle=Notchling Setup
WelcomeLabel1=Notchling
WelcomeLabel2=A dynamic island for your Windows desktop.%n%nMusic, focus, notes and everyday controls, close at hand.%n%nAll tools are free during public testing. No subscription or app account is needed for local tools.%n%nSetup checks shared Windows components only when needed. Your existing workspace stays in place.
WizardLicense=Terms of use
LicenseLabel=Public testing, your data and future paid editions.
LicenseLabel3=Review the complete terms below. Accept them to continue.
LicenseAccepted=I &accept the terms
LicenseNotAccepted=I &do not accept the terms
WizardInfoBefore=Privacy and your controls
InfoBeforeLabel=Understand what stays on your device and when connections are used.
InfoBeforeClickLabel=Review the complete privacy notice below. Select Next when ready.
WizardReady=Ready to set up Notchling
ReadyLabel1=Setup will install Notchling for your Windows account and keep your existing workspace.
FinishedHeadingLabel=Notchling is ready
FinishedLabel=Open Notchling to choose your tools and placement. Hover over the island to expand it; move away to collapse it.
FinishedLabelNoIcons=Open Notchling to choose your tools and placement. Hover over the island to expand it; move away to collapse it.
BeveledLabel=Notchling {#AppVersion}  |  {#AppArchitecture}

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}\app\{#AppVersion}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "install-prerequisites.ps1"; Flags: dontcopy
Source: "{#NativeInteropPath}"; DestName: "Notchling.Setup.Interop.dll"; Flags: dontcopy

[Icons]
Name: "{group}\Notchling"; Filename: "{app}\app\{#AppVersion}\Notchling.Windows.exe"; WorkingDir: "{app}\app\{#AppVersion}"
Name: "{group}\Uninstall Notchling"; Filename: "{uninstallexe}"

[InstallDelete]
; Remove only obsolete shortcuts created by the pre-branding installer.
Type: files; Name: "{userprograms}\Notch\Notch.lnk"
Type: files; Name: "{userprograms}\Notch\Uninstall Notch.lnk"
Type: dirifempty; Name: "{userprograms}\Notch"

[Run]
Filename: "{app}\app\{#AppVersion}\Notchling.Windows.exe"; WorkingDir: "{app}\app\{#AppVersion}"; Description: "Open Notchling"; Flags: nowait postinstall skipifsilent

[Code]
procedure InitializeWizard();
var
  BrandSize: Integer;
  BrandLeft: Integer;
begin
  // Keep the approved square icon square: the standard sidebar stretches an
  // image to a portrait rectangle. Both the welcome and finish pages share
  // this measured layout, while DPI scaling remains owned by Inno Setup.
  // The sidebar width already reflects font size, DPI and wizard sizing.
  // Center a measured square within that width instead of using unrelated
  // ScaleX/ScaleY dimensions that can distort the approved icon.
  BrandSize := ScaleX(116);
  BrandLeft := (WizardForm.WizardBitmapImage.Width - BrandSize) div 2;
  if BrandLeft < ScaleX(16) then
    BrandLeft := ScaleX(16);
  WizardForm.WizardBitmapImage.SetBounds(BrandLeft, ScaleY(36), BrandSize, BrandSize);
  WizardForm.WizardBitmapImage2.SetBounds(BrandLeft, ScaleY(36), BrandSize, BrandSize);
  WizardForm.WelcomeLabel1.Font.Size := 22;
  WizardForm.FinishedHeadingLabel.Font.Size := 20;
  WizardForm.PageNameLabel.Font.Size := 12;
  WizardForm.LicenseMemo.BorderStyle := bsNone;
  WizardForm.InfoBeforeMemo.BorderStyle := bsNone;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  ResultFile: String;
  ResultText: AnsiString;
  Parameters: String;
  PowerShellPath: String;
begin
  Result := '';
  if CheckForMutexes('Notch.Desktop.Running') then begin
    Result := 'Save your work and quit Notchling from the tray before installing. Setup does not forcibly terminate Notchling.';
    Exit;
  end;

  ExtractTemporaryFile('install-prerequisites.ps1');
  ExtractTemporaryFile('Notchling.Setup.Interop.dll');
  ResultFile := ExpandConstant('{tmp}\Notchling-prerequisites-result.txt');
  Parameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{tmp}\install-prerequisites.ps1') + '" -Architecture {#AppArchitecture} -NativeInteropPath "' +
    ExpandConstant('{tmp}\Notchling.Setup.Interop.dll') + '" -ResultPath "' + ResultFile + '"';
  if IsWin64 and not Is64BitInstallMode then
    PowerShellPath := ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe')
  else
    PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  WizardForm.PreparingLabel.Caption := 'Preparing shared Windows components, only if needed. A Microsoft permission prompt may appear.';
  Log('Checking Notchling shared runtime prerequisites.');
  if not Exec(PowerShellPath,
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
