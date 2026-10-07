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
#define AppExe "PharmaBill.App.exe"

[Setup]
; Keep this GUID constant forever: it is how upgrades find the previous install.
AppId={{6F0B9C1E-3A52-4D8B-9E47-5B1D2C7A8F31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=PharmaBill
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile=..\PharmaBill.App\Assets\PharmaBill.ico
OutputDir={#OutputDir}
OutputBaseFilename=PharmaBill-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 or later.
MinVersion=10.0
; Per-user install ({localappdata}\Programs\PharmaBill) by default; the user may choose "all users"
; (Program Files) in the install-mode dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
; Partner Center / silent automation: never block waiting on running app instances.
CloseApplications=no
RestartApplications=no
UsePreviousAppDir=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
; Application files only. Never list anything under {localappdata}\PharmaBill here.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

; No [UninstallDelete] entries for user data on purpose.
[UninstallRun]
; (none)
