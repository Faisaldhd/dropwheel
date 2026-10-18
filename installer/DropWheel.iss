; Inno Setup script – per-user install, no admin rights needed.
; Build: iscc /DAppVersion=1.2.3 installer\DropWheel.iss  (expects the published app in ..\publish)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{1AB9CDBE-C50D-484B-96ED-5D575D0806D2}
AppName=DropWheel
AppVersion={#AppVersion}
AppVerName=DropWheel {#AppVersion}
AppPublisher=DropWheel
DefaultDirName={localappdata}\Programs\DropWheel
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\..\release
OutputBaseFilename=DropWheel-Setup-{#AppVersion}
SetupIconFile=..\src\FileAction.App\Assets\DropWheel.ico
UninstallDisplayIcon={app}\DropWheel.exe
UninstallDisplayName=DropWheel
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\..\release\DropWheel\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{userprograms}\DropWheel"; Filename: "{app}\DropWheel.exe"
Name: "{userprograms}\DropWheel Licenses"; Filename: "{app}\licenses"

[Run]
Filename: "{app}\DropWheel.exe"; Description: "{cm:LaunchProgram,DropWheel}"; Flags: nowait postinstall skipifsilent
; Silent install = in-app update: start the new version again.

[UninstallDelete]
; FFmpeg and Pandoc downloaded on demand by the app.
Type: filesandordirs; Name: "{localappdata}\DropWheel\tools"

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /im DropWheel.exe /f"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{app}\DropWheel.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "Unregister"
