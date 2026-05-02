; Inno Setup script for the civ6-async mod.
; Build: ISCC.exe installer\civ6-async.iss
; Output: dist\civ6-async-Setup-<version>.exe
;
; Behavior: double-click the .exe once to install. Double-click it again and it
; detects the existing install and prompts to uninstall.

#define MyAppName "civ6-async"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "arinrazi"
#define MyModFolder "civ6-async"
#define MyAppId "{2C975599-6C42-4D05-91C2-EC78A307680A}"

[Setup]
; AppId is what Windows uses to identify this installation for upgrade/uninstall.
; Keep it stable across versions; bump MyAppVersion instead.
AppId={{#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={userdocs}\My Games\Sid Meier's Civilization VI\Mods\{#MyModFolder}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename={#MyModFolder}-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName} (Civilization VI mod)
UninstallDisplayIcon={app}\civ6-async.modinfo
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\civ6-async.modinfo";      DestDir: "{app}"; Flags: ignoreversion
Source: "..\UI\PlayerChange.lua";     DestDir: "{app}\UI"; Flags: ignoreversion
Source: "..\UI\ActionPanel.lua";      DestDir: "{app}\UI"; Flags: ignoreversion
Source: "..\UI\ForceAutoEndTurn.lua"; DestDir: "{app}\UI"; Flags: ignoreversion
Source: "..\UI\ForceAutoEndTurn.xml"; DestDir: "{app}\UI"; Flags: ignoreversion

[Code]
const
  UninstallKeyPrefix = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\';

function GetExistingUninstallString(): string;
var
  Key, S: string;
begin
  Result := '';
  // Inno appends "_is1" to the AppId for the uninstall registry key.
  Key := UninstallKeyPrefix + '{#MyAppId}_is1';
  if RegQueryStringValue(HKCU, Key, 'UninstallString', S) then
    Result := S
  else if RegQueryStringValue(HKLM, Key, 'UninstallString', S) then
    Result := S;
end;

function InitializeSetup(): Boolean;
var
  ExistingUninstall, StrippedUninstall: string;
  ResultCode: Integer;
  ModsParent: string;
begin
  Result := True;

  // ----- Already installed? Offer to uninstall and exit -----
  ExistingUninstall := GetExistingUninstallString();
  if ExistingUninstall <> '' then
  begin
    if MsgBox(
        '{#MyAppName} is already installed.' #13#10#13#10 +
        'Do you want to uninstall it?',
        mbConfirmation, MB_YESNO) = IDYES then
    begin
      StrippedUninstall := RemoveQuotes(ExistingUninstall);
      Exec(StrippedUninstall, '/SILENT /NORESTART /SUPPRESSMSGBOXES',
           '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
      MsgBox('{#MyAppName} has been uninstalled.', mbInformation, MB_OK);
    end;
    Result := False;
    Exit;
  end;

  // ----- Fresh install: warn if Civ 6 user folder is missing -----
  ModsParent := ExpandConstant('{userdocs}\My Games\Sid Meier''s Civilization VI');
  if not DirExists(ModsParent) then
  begin
    if MsgBox(
        'The Civilization VI user folder was not found at:' #13#10 +
        ModsParent + #13#10#13#10 +
        'This usually means the game has not been launched on this account yet. ' +
        'You can continue — the mod will be installed and picked up the next time you launch the game.' #13#10#13#10 +
        'Continue?',
        mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
      Exit;
    end;
  end;
end;
