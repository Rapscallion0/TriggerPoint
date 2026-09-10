; =====================================================================
; TriggerPoint Inno Setup Script
; Precision shortcuts. Instant menus. Zero bloat.
; =====================================================================

#ifndef AppVersion
#define AppVersion "2.0.3"
#endif

#ifndef PublishDir
#define PublishDir "..\src\TriggerPoint.UI\bin\Release\net9.0-windows\win-x64\publish"
#endif

[Setup]
AppId={{C8E6814E-43B0-4A0B-9781-F0359871788E}
AppName=TriggerPoint
AppVersion={#AppVersion}
AppVerName=TriggerPoint {#AppVersion}
AppPublisher=TriggerPoint
DefaultDirName={autopf}\TriggerPoint
DefaultGroupName=TriggerPoint
DisableProgramGroupPage=yes
DisableDirPage=no
DisableWelcomePage=no

; Dual-mode installation: allows installing for all users (Program Files) or current user (LocalAppData)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=no
UsePreviousAppDir=no
ArchitecturesInstallIn64BitMode=x64compatible

; Mutex detection and clean process termination
AppMutex=Global\TriggerPoint_SingleInstance
CloseApplications=yes
RestartApplications=no

; Visual Branding
SetupIconFile=..\assets\TriggerPoint.ico
UninstallDisplayIcon={app}\TriggerPoint.exe
WizardStyle=modern

; Output settings
OutputDir=..\artifacts
OutputBaseFilename=TriggerPointSetup
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start TriggerPoint automatically on Windows login"; GroupDescription: "Windows Startup Options:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\assets\TriggerPoint.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\TriggerPoint\TriggerPoint"; Filename: "{app}\TriggerPoint.exe"; IconFilename: "{app}\TriggerPoint.ico"
Name: "{autoprograms}\TriggerPoint\Uninstall TriggerPoint"; Filename: "{uninstallexe}"
Name: "{autodesktop}\TriggerPoint"; Filename: "{app}\TriggerPoint.exe"; IconFilename: "{app}\TriggerPoint.ico"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TriggerPoint"; ValueData: """{app}\TriggerPoint.exe"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\TriggerPoint.exe"; Description: "{cm:LaunchProgram,TriggerPoint}"; Flags: nowait postinstall skipifsilent

[Code]
procedure ExitProcess(uExitCode: Integer); external 'ExitProcess@kernel32.dll stdcall';

const
  AppGuidStr = '{C8E6814E-43B0-4A0B-9781-F0359871788E}_is1';

var
  ExistingInstallMode: Integer; // 0 = None, 1 = Per-User (HKCU), 2 = All Users (HKLM)
  ExistingInstallLocation: string;
  ExistingUninstallString: string;
  MigrationConfirmed: Boolean;

function DetectExistingInstallation(): Integer;
var
  SubKey: string;
  Loc, Uninst: string;
begin
  SubKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + AppGuidStr;
  Loc := '';
  Uninst := '';

  // Check HKLM (System-wide) first
  if RegQueryStringValue(HKLM, SubKey, 'InstallLocation', Loc) or
     RegQueryStringValue(HKLM, SubKey, 'UninstallString', Uninst) then
  begin
    if Loc = '' then RegQueryStringValue(HKLM, SubKey, 'Inno Setup: App Path', Loc);
    if Uninst = '' then RegQueryStringValue(HKLM, SubKey, 'UninstallString', Uninst);
    ExistingInstallLocation := Loc;
    ExistingUninstallString := Uninst;
    Result := 2; // All Users (HKLM)
    Exit;
  end;

  // Check HKCU (Per-user)
  if RegQueryStringValue(HKCU, SubKey, 'InstallLocation', Loc) or
     RegQueryStringValue(HKCU, SubKey, 'UninstallString', Uninst) then
  begin
    if Loc = '' then RegQueryStringValue(HKCU, SubKey, 'Inno Setup: App Path', Loc);
    if Uninst = '' then RegQueryStringValue(HKCU, SubKey, 'UninstallString', Uninst);
    ExistingInstallLocation := Loc;
    ExistingUninstallString := Uninst;
    Result := 1; // Per-User (HKCU)
    Exit;
  end;

  Result := 0; // Fresh install
end;

function IsSwitchingInstallMode(): Boolean;
begin
  Result := ((ExistingInstallMode = 1) and IsAdminInstallMode) or
            ((ExistingInstallMode = 2) and (not IsAdminInstallMode));
end;

procedure InitializeWizard();
var
  ModeDesc: string;
begin
  ExistingInstallMode := DetectExistingInstallation();

  if ExistingInstallMode = 1 then
    ModeDesc := 'Current installation detected: Per-User (' + ExistingInstallLocation + ')'
  else if ExistingInstallMode = 2 then
    ModeDesc := 'Current installation detected: All Users (' + ExistingInstallLocation + ')'
  else
    ModeDesc := '';

  if (ModeDesc <> '') and (WizardForm.WelcomeLabel2 <> nil) then
  begin
    WizardForm.WelcomeLabel2.Caption := WizardForm.WelcomeLabel2.Caption + #13#10#13#10 +
      ModeDesc + #13#10 +
      'All custom shortcuts, actions, and settings in %APPDATA% will be preserved.';
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  OldTypeStr, NewTypeStr, Msg, OldTypeShort: string;
begin
  Result := True;
  if (CurPageID = wpWelcome) and IsSwitchingInstallMode() and (not MigrationConfirmed) then
  begin
    if ExistingInstallMode = 1 then
    begin
      OldTypeShort := 'Per-User (Me Only)';
      OldTypeStr := 'Per-User Mode (LocalAppData: ' + ExistingInstallLocation + ')';
      NewTypeStr := 'All Users Mode (Program Files)';
    end
    else
    begin
      OldTypeShort := 'All Users';
      OldTypeStr := 'All Users Mode (Program Files: ' + ExistingInstallLocation + ')';
      NewTypeStr := 'Per-User Mode (LocalAppData)';
    end;

    Msg := 'TriggerPoint is currently installed in:' + #13#10 +
           '  • ' + OldTypeStr + #13#10#13#10 +
           'You selected to install in:' + #13#10 +
           '  • ' + NewTypeStr + #13#10#13#10 +
           'Setup must install to the new location and cleanly remove the previous installation.' + #13#10 +
           'All your existing shortcuts, actions, and settings in %APPDATA% will be preserved.' + #13#10#13#10 +
           '• Click "Yes" to proceed with the migration.' + #13#10 +
           '• Click "No" to cancel Setup and keep your existing installation.';

    if MsgBox(Msg, mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDYES then
    begin
      MigrationConfirmed := True;
      Result := True;
    end
    else
    begin
      MsgBox('Setup will now exit to keep your existing installation unchanged.' + #13#10#13#10 +
             'If you wish to update or repair your existing installation, please re-run Setup and select "' + OldTypeShort + '".',
             mbInformation, MB_OK);
      ExitProcess(0);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  UninstExe: string;
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    if IsSwitchingInstallMode() and (ExistingUninstallString <> '') then
    begin
      UninstExe := RemoveQuotes(ExistingUninstallString);
      if FileExists(UninstExe) then
      begin
        WizardForm.StatusLabel.Caption := 'Removing previous TriggerPoint installation...';
        Exec(UninstExe, '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      end;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppDataDir := ExpandConstant('{userappdata}\TriggerPoint');
    if (not UninstallSilent) and DirExists(AppDataDir) then
    begin
      if MsgBox('Would you like to remove your TriggerPoint custom configurations and logs?' + #13#10 + #13#10 +
                '(Select "No" to keep your shortcuts and settings for future reinstallations)',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(AppDataDir, True, True, True);
      end;
    end;
  end;
end;
