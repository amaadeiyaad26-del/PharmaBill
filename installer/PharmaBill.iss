; PharmaBill installer. Build with: ISCC.exe installer\PharmaBill.iss /DAppVersion=1.0.0
; (build-installer.ps1 does this for you).
;
; User data lives in %LocalAppData%\PharmaBill (database, key, attachments, logs, settings, backups),
; which is outside the install folder. The installer never writes to, overwrites or removes it,
; and the uninstaller leaves it in place.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
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
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile=..\PharmaBill.App\Assets\Icons\app.ico
OutputDir={#OutputDir}
OutputBaseFilename=PharmaBill_Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 or later.
MinVersion=10.0
; Per-user install ({localappdata}\Programs\PharmaBill) by default — no UAC elevation.
; PrivilegesRequiredOverridesAllowed=dialog lets the user opt into all-users (Program Files).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
; Partner Center / silent automation: never block waiting on running app instances.
CloseApplications=no
RestartApplications=no
UsePreviousAppDir=yes
AllowNoIcons=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: checkedonce

[Files]
; Application files only. Never list anything under {localappdata}\PharmaBill here.
; Uninstall must NEVER delete {localappdata}\PharmaBill (chemist DB / bills / stock).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch PharmaBill"; Flags: nowait postinstall skipifsilent

; Intentionally no [UninstallDelete] for {localappdata}\PharmaBill — data survives uninstall/update.
