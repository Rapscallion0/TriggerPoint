; =====================================================================
; TriggerPoint Inno Setup Script
; Precision shortcuts. Instant menus. Zero bloat.
; =====================================================================

#ifndef AppVersion
#define AppVersion "2.0.0"
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
DefaultDirName={localappdata}\Programs\TriggerPoint
DefaultGroupName=TriggerPoint
DisableProgramGroupPage=yes

; Non-administrative, per-user installation (Zero UAC elevation required)
PrivilegesRequired=lowest
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
Name: "{userprograms}\TriggerPoint\TriggerPoint"; Filename: "{app}\TriggerPoint.exe"; IconFilename: "{app}\TriggerPoint.ico"
Name: "{userprograms}\TriggerPoint\Uninstall TriggerPoint"; Filename: "{uninstallexe}"
Name: "{userdesktop}\TriggerPoint"; Filename: "{app}\TriggerPoint.exe"; IconFilename: "{app}\TriggerPoint.ico"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TriggerPoint"; ValueData: """{app}\TriggerPoint.exe"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\TriggerPoint.exe"; Description: "{cm:LaunchProgram,TriggerPoint}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppDataDir := ExpandConstant('{userappdata}\TriggerPoint');
    if DirExists(AppDataDir) then
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
