; Pharma Remit installer: the Local Market edition, downloaded from our website (or copied from USB).
; Build with build-installer.ps1, which publishes with -p:Edition=LocalMarket and passes AppName and AppVersion from
; PharmaBill.App\Editions\LocalMarket.props, so the installer, the program files and the About screen carry one version.
; The Microsoft Store edition is packaged separately by build-msstore-package.ps1; nothing here involves the Store.
;
; The pharmacy's data lives in %LocalAppData%\PharmaBill of the Windows user who runs the app (databases, key,
; attachments, settings, backups). It is outside the install folder. This installer never writes to, overwrites or
; removes it, and the uninstaller leaves it in place, so an uninstall and reinstall, or an upgrade, keeps every record.

#ifndef AppVersion
  #error AppVersion is required. Build with build-installer.ps1.
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\local-market"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif
#ifndef AppName
  #define AppName "Pharma Remit"
#endif
#define AppPublisher "S.A.E.R Soft"
#define AppExe "PharmaBill.App.exe"
; Permanent AppId. Never change it: Windows recognises every later version as an upgrade of the same program, and
; Defender / SmartScreen reputation carries over. It is the AppId every earlier release of this installer used.
#define AppIdGuid "6F0B9C1E-3A52-4D8B-9E47-5B1D2C7A8F31"

[Setup]
; "{{" is Inno's literal "{": the AppId is exactly {6F0B9C1E-3A52-4D8B-9E47-5B1D2C7A8F31}, as in every earlier release.
AppId={{{#AppIdGuid}}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=Copyright (C) 2026 {#AppPublisher}
; The setup EXE's own version resource: the same four-part version as the program.
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoCopyright=Copyright (C) 2026 {#AppPublisher}
VersionInfoProductName={#AppName}
; Program Files (64-bit), for every user of this PC. Installing there needs administrator approval (one UAC prompt).
PrivilegesRequired=admin
; IT staff may still choose a per-user install from the command line (/CURRENTUSER); a double-click always installs
; to Program Files.
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile=..\PharmaBill.App\app.ico
OutputDir={#OutputDir}
OutputBaseFilename=PharmaRemit_Setup_{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Windows 10 or later.
MinVersion=10.0
; Upgrading while Pharma Remit is open: Setup asks to close it (Windows Restart Manager) rather than failing on files
; in use, and does not start it again by itself.
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
; Application files only. Nothing under %LocalAppData%\PharmaBill (the pharmacy's records) is ever listed here.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Shortcuts left by earlier names of this program ("PharmaBill", "Pharma Remit Local Market"). Same AppId and the same
; data folder, so an existing install upgrades in place and keeps its records.
Type: files; Name: "{autoprograms}\PharmaBill.lnk"
Type: files; Name: "{autodesktop}\PharmaBill.lnk"
Type: files; Name: "{autoprograms}\Pharma Remit Local Market.lnk"
Type: files; Name: "{autodesktop}\Pharma Remit Local Market.lnk"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
; No firewall or antivirus changes. On the first LAN listen (TCP 5184) Windows shows its own "Allow Pharma Remit to
; communicate on private networks" prompt.
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

; Intentionally no [UninstallDelete]: %LocalAppData%\PharmaBill (databases, backups, settings) survives uninstall.

[Code]
// Releases up to 1.0.10 installed per user (in %LocalAppData%\Programs, registered under HKCU). This release installs
// to Program Files (registered under HKLM). Windows would list both and keep two copies, so before installing, the
// per-user copy of this same AppId is removed with its own uninstaller. That uninstaller removes program files and
// shortcuts only; the pharmacy's data in %LocalAppData%\PharmaBill is never part of it and stays exactly as it is.
// (It is found under the account that runs Setup: when an administrator approves the UAC prompt with a different
// account, that account's per-user install is the one looked for.)
// The key Inno Setup writes for this AppId: ...\Uninstall\{6F0B9C1E-...-5B1D2C7A8F31}_is1
function PerUserUninstallKey(): String;
begin
  Result := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + '{#AppIdGuid}' + '}_is1';
end;

function PerUserUninstaller(var Command: String): Boolean;
begin
  Result := False;
  if RegQueryStringValue(HKCU, PerUserUninstallKey(), 'UninstallString', Command) then
  begin
    Command := RemoveQuotes(Command);
    Result := (Command <> '') and FileExists(Command);
  end;
end;

procedure RemovePerUserInstall();
var
  Command: String;
  ResultCode: Integer;
begin
  if not IsAdminInstallMode() then
    Exit;
  if not PerUserUninstaller(Command) then
    Exit;
  Log('Removing the earlier per-user install: ' + Command);
  if not Exec(Command, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log('The earlier per-user uninstaller could not be started; continuing.')
  else
    Log('Earlier per-user uninstaller finished with code ' + IntToStr(ResultCode) + '.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    RemovePerUserInstall();
end;
