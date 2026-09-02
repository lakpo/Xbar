; Installateur Xbar (Inno Setup 6+)
; Installation par utilisateur : aucune elevation, aucun UAC.
; Compilation : ISCC.exe build\Xbar.iss  (voir build\publish.ps1)

#define AppName "Xbar"
#define AppExe "Xbar.exe"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Payload
  #define Payload "..\dist\install"
#endif

[Setup]
AppId={{9C0F3E4B-6E2E-4C55-9E7B-3C1E9C6B7A21}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Xbar
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\dist
OutputBaseFilename=Xbar-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Installation dans le profil utilisateur : pas de droits administrateur requis.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0.22000

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Démarrer Xbar avec Windows"; GroupDescription: "Démarrage"; Flags: checkedonce
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis"; Flags: unchecked

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "Xbar"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Lancer Xbar"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Ferme l'instance en cours avant la desinstallation.
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopXbar"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
