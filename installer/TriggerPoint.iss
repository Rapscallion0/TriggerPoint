; =====================================================================
; TriggerPoint Inno Setup Script
; Precision shortcuts. Instant menus. Zero bloat.
; =====================================================================

#ifndef AppVersion
#define AppVersion "2.0.8"
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
UsePreviousPrivileges=yes
UsePreviousAppDir=yes
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
  ExistingVersion: string;
  MaintenancePage: TInputOptionWizardPage;
  IsUpgradeMode: Boolean;
  IsSameVersionMode: Boolean;
  IsDowngradeMode: Boolean;

function GetNextVersionPart(var V: string): Integer;
var
  DotPos: Integer;
  PartStr: string;
begin
  DotPos := Pos('.', V);
  if DotPos > 0 then
  begin
    PartStr := Copy(V, 1, DotPos - 1);
    Delete(V, 1, DotPos);
  end
  else
  begin
    PartStr := V;
    V := '';
  end;
  Result := StrToIntDef(PartStr, 0);
end;

function CompareSemVer(V1, V2: string): Integer;
var
  Part1, Part2: Integer;
begin
  if Pos('+', V1) > 0 then V1 := Copy(V1, 1, Pos('+', V1) - 1);
  if Pos('+', V2) > 0 then V2 := Copy(V2, 1, Pos('+', V2) - 1);
  V1 := Trim(V1);
  V2 := Trim(V2);

  while (Length(V1) > 0) or (Length(V2) > 0) do
  begin
    Part1 := GetNextVersionPart(V1);
    Part2 := GetNextVersionPart(V2);
    if Part1 > Part2 then
    begin
      Result := 1;
      Exit;
    end
    else if Part1 < Part2 then
    begin
      Result := -1;
      Exit;
    end;
  end;
  Result := 0;
end;

function DetectExistingInstallation(): Integer;
var
  SubKey: string;
  Loc, Uninst, Ver: string;
begin
  SubKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + AppGuidStr;
  Loc := '';
  Uninst := '';
  Ver := '';

  // Check HKLM (All Users) first
  if RegQueryStringValue(HKLM, SubKey, 'InstallLocation', Loc) or
     RegQueryStringValue(HKLM, SubKey, 'UninstallString', Uninst) then
  begin
    if Loc = '' then RegQueryStringValue(HKLM, SubKey, 'Inno Setup: App Path', Loc);
    if Uninst = '' then RegQueryStringValue(HKLM, SubKey, 'UninstallString', Uninst);
    RegQueryStringValue(HKLM, SubKey, 'DisplayVersion', Ver);
    ExistingInstallLocation := RemoveBackslashUnlessRoot(Loc);
    ExistingUninstallString := Uninst;
    ExistingVersion := Ver;
    Result := 2; // All Users (HKLM)
    Exit;
  end;

  // Check HKCU (Per-user)
  if RegQueryStringValue(HKCU, SubKey, 'InstallLocation', Loc) or
     RegQueryStringValue(HKCU, SubKey, 'UninstallString', Uninst) then
  begin
    if Loc = '' then RegQueryStringValue(HKCU, SubKey, 'Inno Setup: App Path', Loc);
    if Uninst = '' then RegQueryStringValue(HKCU, SubKey, 'UninstallString', Uninst);
    RegQueryStringValue(HKCU, SubKey, 'DisplayVersion', Ver);
    ExistingInstallLocation := RemoveBackslashUnlessRoot(Loc);
    ExistingUninstallString := Uninst;
    ExistingVersion := Ver;
    Result := 1; // Per-User (HKCU)
    Exit;
  end;

  Result := 0; // Fresh install
end;

procedure CheckScopeCompatibility();
var
  Msg: string;
begin
  if (ExistingInstallMode = 2) and (not IsAdminInstallMode) then
  begin
    Msg := 'TriggerPoint is currently installed for All Users at:' + #13#10 +
           '  ' + ExistingInstallLocation + #13#10#13#10 +
           'Updates and repairs must use the same installation type.' + #13#10 +
           'Please re-run Setup as Administrator, or uninstall the existing version before switching to a per-user installation.';
    MsgBox(Msg, mbError, MB_OK);
    ExitProcess(0);
  end;

  if (ExistingInstallMode = 1) and IsAdminInstallMode then
  begin
    Msg := 'TriggerPoint is currently installed for the current user at:' + #13#10 +
           '  ' + ExistingInstallLocation + #13#10#13#10 +
           'Updates and repairs must use the same installation type.' + #13#10 +
           'Please run Setup without administrative elevation, or uninstall the existing version before switching to an all-users installation.';
    MsgBox(Msg, mbError, MB_OK);
    ExitProcess(0);
  end;
end;

procedure InitializeWizard();
var
  ModeDesc, ScopeName, RunVal, DesktopLnk: string;
  VerComp: Integer;
begin
  ExistingInstallMode := DetectExistingInstallation();

  if ExistingInstallMode <> 0 then
  begin
    CheckScopeCompatibility();

    if ExistingInstallMode = 1 then
      ScopeName := 'Per-User'
    else
      ScopeName := 'All Users';

    if ExistingVersion <> '' then
      ModeDesc := 'Current installation detected: v' + ExistingVersion + ' (' + ScopeName + ' at ' + ExistingInstallLocation + ')'
    else
      ModeDesc := 'Current installation detected: ' + ScopeName + ' (' + ExistingInstallLocation + ')';

    if WizardForm.WelcomeLabel2 <> nil then
    begin
      WizardForm.WelcomeLabel2.Caption := WizardForm.WelcomeLabel2.Caption + #13#10#13#10 +
        ModeDesc + #13#10 +
        'All custom shortcuts, actions, and settings in %APPDATA% will be preserved.';
    end;

    // Lock destination directory to existing installation path
    if (ExistingInstallLocation <> '') and (WizardForm.DirEdit <> nil) then
      WizardForm.DirEdit.Text := ExistingInstallLocation;

    // Pre-populate tasks from existing installation state
    if RegQueryStringValue(HKA, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TriggerPoint', RunVal) or
       RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TriggerPoint', RunVal) or
       RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TriggerPoint', RunVal) then
    begin
      WizardSelectTasks('autostart');
    end;

    DesktopLnk := ExpandConstant('{autodesktop}\TriggerPoint.lnk');
    if FileExists(DesktopLnk) then
    begin
      WizardSelectTasks('desktopicon');
    end;

    // Create Maintenance Page
    MaintenancePage := CreateInputOptionPage(
      wpWelcome,
      'Maintenance Options',
      'TriggerPoint is already installed on this computer.',
      'Select whether you want to update, repair, or uninstall TriggerPoint, then click Next.',
      True,
      False
    );

    VerComp := CompareSemVer('{#AppVersion}', ExistingVersion);

    if (VerComp > 0) or (ExistingVersion = '') then
    begin
      IsUpgradeMode := True;
      MaintenancePage.Add('Upgrade TriggerPoint to version {#AppVersion} (Recommended)' + #13#10 +
        'Updates application files while preserving your custom shortcuts, actions, and settings.');
      MaintenancePage.Add('Repair or reinstall TriggerPoint files' + #13#10 +
        'Re-extracts application files and refreshes shortcuts.');
      MaintenancePage.Add('Uninstall TriggerPoint from this computer' + #13#10 +
        'Removes TriggerPoint program files from your system.');
      MaintenancePage.SelectedValueIndex := 0;
    end
    else if VerComp = 0 then
    begin
      IsSameVersionMode := True;
      MaintenancePage.Add('Repair / Reinstall TriggerPoint version {#AppVersion} (Recommended)' + #13#10 +
        'Re-copies application files, restores missing components, and refreshes shortcuts.');
      MaintenancePage.Add('Uninstall TriggerPoint from this computer' + #13#10 +
        'Removes TriggerPoint program files from your system.');
      MaintenancePage.SelectedValueIndex := 0;
    end
    else
    begin
      IsDowngradeMode := True;
      MaintenancePage.Add('Downgrade TriggerPoint to version {#AppVersion} (Warning)' + #13#10 +
        'Replaces newer version v' + ExistingVersion + ' with older version v{#AppVersion}.');
      MaintenancePage.Add('Uninstall TriggerPoint from this computer' + #13#10 +
        'Removes TriggerPoint program files from your system.');
      MaintenancePage.SelectedValueIndex := 0;
    end;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  // Skip maintenance page for fresh installations
  if (ExistingInstallMode = 0) and (MaintenancePage <> nil) and (PageID = MaintenancePage.ID) then
  begin
    Result := True;
    Exit;
  end;

  // Skip directory selection for existing installations to preserve the existing path
  if (ExistingInstallMode <> 0) and (PageID = wpSelectDir) then
  begin
    Result := True;
    Exit;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
  UninstExe: string;
begin
  Result := True;

  if (MaintenancePage <> nil) and (CurPageID = MaintenancePage.ID) then
  begin
    // Check if the user selected 'Uninstall'
    if ((IsUpgradeMode and (MaintenancePage.SelectedValueIndex = 2)) or
       ((not IsUpgradeMode) and (MaintenancePage.SelectedValueIndex = 1))) then
    begin
      if MsgBox('Are you sure you want to uninstall TriggerPoint?' + #13#10#13#10 +
                'Installation directory: ' + ExistingInstallLocation,
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        UninstExe := RemoveQuotes(ExistingUninstallString);
        if (UninstExe <> '') and FileExists(UninstExe) then
        begin
          Exec(UninstExe, '', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode);
          ExitProcess(0);
        end
        else
        begin
          MsgBox('Could not locate the uninstaller executable at:' + #13#10 +
                 ExistingUninstallString + #13#10#13#10 +
                 'Please uninstall TriggerPoint via Windows Settings -> Installed Apps.',
                 mbError, MB_OK);
          Result := False;
          Exit;
        end;
      end
      else
      begin
        Result := False;
        Exit;
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
