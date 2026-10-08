; =============================================================================
; PharmaBill — Inno Setup installer (project root)
; =============================================================================
; Prerequisites:
;   1) Publish the Win32 app (see command at bottom of this file).
;   2) Compile with Inno Setup 6+:  ISCC.exe installer.iss
;
; SAFE STORAGE GUARDRAIL:
;   Chemist data lives ONLY under  %LocalAppData%\PharmaBill\  (database, bills,
;   stock, licence anchors, logs, backups). This script NEVER installs into that
;   folder and NEVER lists it under [UninstallDelete]. Uninstall/update therefore
;   cannot wipe pharmacy records.
; =============================================================================

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

; Published output from `dotnet publish` (self-contained win-x64).
; Override: ISCC.exe installer.iss /DPublishDir=C:\path\to\publish
#ifndef PublishDir
  #define PublishDir "PharmaBill.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
#endif

#ifndef OutputDir
  #define OutputDir "artifacts\installer"
#endif

#define AppName "PharmaBill"
#define AppPublisher "SAER"
#define AppExe "PharmaBill.App.exe"
; Four-part PE version resource (Defender / SmartScreen metadata).
#define VersionInfoVersion AppVersion + ".0"

[Setup]
; Permanent AppId — never change. Keeps Windows uninstall identity and Defender
; reputation continuous across PharmaBill updates (do not regenerate this GUID).
AppId={{6F0B9C1E-3A52-4D8B-9E47-5B1D2C7A8F31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#VersionInfoVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=PharmaBill Pharmacy Billing and Inventory Setup
VersionInfoCopyright=Copyright (C) 2026 SAER
VersionInfoProductName={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=PharmaBill_Setup
SetupIconFile=PharmaBill.App\Assets\Icons\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; lowest = no UAC by default ({autopf} → %LocalAppData%\Programs when not elevated).
; OverridesAllowed=dialog lets the user choose an all-users (Program Files) install.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousAppDir=yes
CloseApplications=no
RestartApplications=no
; Do not touch user data outside {app}.
AllowNoIcons=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: checkedonce

[Files]
; Application binaries ONLY. Never source from or dest into {localappdata}\PharmaBill.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"
; Desktop (task checked by default via checkedonce)
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch PharmaBill"; Flags: nowait postinstall skipifsilent

; -----------------------------------------------------------------------------
; INTENTIONALLY OMITTED:
;   [UninstallDelete]  — must NEVER target {localappdata}\PharmaBill or any
;                        chemist database / bills / stock under that tree.
;   [Dirs] for {localappdata}\PharmaBill — the app creates data folders at runtime.
; -----------------------------------------------------------------------------

[Code]
// Defensive documentation for maintainers: uninstaller only removes files that
// were installed under {app}. %LocalAppData%\PharmaBill is outside that scope.
function InitializeUninstall(): Boolean;
begin
  // Return True to proceed. User data under
  // ExpandConstant('{localappdata}\PharmaBill') is left intact by design.
  Result := True;
end;
