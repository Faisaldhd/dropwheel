; DropWheel installer (Inno Setup 6.3 or newer).
;
; 2.2.0 ships a SELF-CONTAINED win-x64 build: the publish already contains .NET and every
; dependency, so this script installs no runtime, downloads nothing and checks nothing about
; the machine's .NET installation. scripts\build-installer.ps1 produces release\selfcontained
; (app + LICENSE + THIRD-PARTY-NOTICES.md + licenses\) and then compiles this script:
;
;   pwsh scripts\build-installer.ps1
;
; The AppId is unchanged since the first release, so a new setup upgrades the previous install in
; place and %APPDATA%\DropWheel\settings.json survives. The task choices of the previous install
; are presented again instead of being reset (UsePreviousTasks), and a silent upgrade never
; re-applies the two default tasks to an existing install (see ApplyInstallDefaults in [Code]).

#ifndef AppVersion
#define AppVersion "2.2.3"
#endif

[Setup]
; Same AppId as every DropWheel release: in-place upgrades, one entry in "Apps & features".
AppId={{1AB9CDBE-C50D-484B-96ED-5D575D0806D2}
AppName=DropWheel
AppVersion={#AppVersion}
AppVerName=DropWheel {#AppVersion}
AppPublisher=DropWheel
AppPublisherURL=https://github.com/Faisaldhd/dropwheel
AppSupportURL=https://github.com/Faisaldhd/dropwheel/issues
AppUpdatesURL=https://github.com/Faisaldhd/dropwheel/releases
VersionInfoVersion={#AppVersion}
VersionInfoCompany=DropWheel
VersionInfoDescription=DropWheel {#AppVersion} Setup
VersionInfoProductName=DropWheel
VersionInfoProductVersion={#AppVersion}

; {autopf} is %LOCALAPPDATA%\Programs for the default per-user install and C:\Program Files for
; an all-users install (chosen in the dialog, or with /ALLUSERS on the command line).
DefaultDirName={autopf}\DropWheel
DefaultGroupName=DropWheel
DisableProgramGroupPage=yes
DisableDirPage=auto

; No admin rights by default. The mode dialog lets the user pick "all users", the updater passes
; the mode of the running install as /CURRENTUSER or /ALLUSERS, and on upgrade the previous mode is
; re-used without asking (UsePreviousPrivileges, the default). "commandline" is listed explicitly
; even though "dialog" already implies it, so the updater's switches can never stop working.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
; Keep the task choices of a previous install (never reset the user's choices on upgrade).
UsePreviousTasks=yes

WizardStyle=modern
; Windows 10 1809 (build 17763) or newer, 64-bit only - the same floor as the app itself.
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir=..\release
OutputBaseFilename=DropWheel-{#AppVersion}-Setup
SetupIconFile=..\src\FileAction.App\Assets\DropWheel.ico
UninstallDisplayIcon={app}\DropWheel.exe
UninstallDisplayName=DropWheel
Compression=lzma2/ultra64
SolidCompression=yes
; Silent upgrades (the updater) close a running DropWheel but do not start it again: the updater
; launches the new version itself. RestartApplications=no keeps that deterministic. Setup keeps
; a log in %TEMP% because a failed install is the case where the log is needed.
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

; The violet wizard images (164x314 and 55x55) are generated from branding\DropWheel-1024.png by
; scripts\build-installer.ps1. If System.Drawing is unavailable the build script omits the
; defines and Inno Setup's own wizard images are used.
#ifdef CustomWizardImages
WizardImageFile={#WizardImageFile}
WizardSmallImageFile={#WizardSmallImageFile}
#endif

[Languages]
; English is built in. Arabic is vendored in installer\Arabic.isl (the Inno Setup 6.5+ Arabic
; translation) plus DropWheel-ar.isl, so the build does not depend on which languages the
; installed compiler ships. compiler:Default.isl comes first only as the fallback for a message
; a newer compiler might add; every option and message Arabic.isl defines overrides it (later
; files win) and RightToLeft=yes makes the whole wizard RTL.
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic"; MessagesFile: "compiler:Default.isl,Arabic.isl,DropWheel-ar.isl"

[CustomMessages]
; English side of the installer-only strings. The Arabic side lives in DropWheel-ar.isl; a
; language-prefixed entry applies to that language only, an unprefixed one to every language.
english.TasksGroup=Additional tasks:
english.TaskStartWithWindows=Start with &Windows
english.TaskWatchInput=&Watch mouse and keyboard
english.AppComment=DropWheel - click-free file conversion on a radial wheel
english.RemoveSettingsQuestion=Do you also want to remove your DropWheel settings and the helper programs it downloaded?%n%nChoose Yes to remove them, or No to keep them for a later install.

[Tasks]
; Checked on a first install; after an upgrade the choices of the previous install are used.
; checkedonce makes sure an upgrade from a version that had no tasks (2.1 and older) does not
; silently turn the two new defaults on: nothing the user configured is changed by an upgrade.
Name: "startup"; Description: "{cm:TaskStartWithWindows}"; GroupDescription: "{cm:TasksGroup}"; Flags: checkedonce
Name: "watch"; Description: "{cm:TaskWatchInput}"; GroupDescription: "{cm:TasksGroup}"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:TasksGroup}"; Flags: unchecked

[Files]
; Everything the self-contained publish produced. build-installer.ps1 has already copied
; LICENSE, THIRD-PARTY-NOTICES.md and licenses\ next to DropWheel.exe, so the app folder is
; self-describing and the portable layout and the installed layout are identical.
Source: "..\release\selfcontained\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
; Start-menu shortcut always (both install modes use the matching {auto*} folder).
Name: "{autoprograms}\DropWheel"; Filename: "{app}\DropWheel.exe"; WorkingDir: "{app}"; Comment: "{cm:AppComment}"
Name: "{autoprograms}\DropWheel Licenses"; Filename: "{app}\licenses"; Comment: "{cm:AppComment}"
; Desktop shortcut only when the (unchecked by default) task was selected.
Name: "{autodesktop}\DropWheel"; Filename: "{app}\DropWheel.exe"; WorkingDir: "{app}"; Comment: "{cm:AppComment}"; Tasks: desktopicon

[Registry]
; "Start with Windows": exactly the value the app writes itself in Settings (HKCU Run, value
; DropWheel = "<exe>" --background). The uninstaller removes it in [Code] (no uninsdeletevalue:
; the value is also app-owned and the Code path is the one that always runs).
; ApplyInstallDefaults: a silent update must not turn autostart back on for a user who turned it
; off in Settings after the previous install (see the Code section).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DropWheel"; ValueData: """{app}\DropWheel.exe"" --background"; Tasks: startup; Check: ApplyInstallDefaults

[Run]
; "Watch mouse and keyboard": the app applies the switch to its own settings.json. runhidden
; keeps the window hidden and nowait means a setup can never be blocked by the app; the switch
; writes one setting and exits (DropWheel.exe --set-watch).
Filename: "{app}\DropWheel.exe"; Parameters: "--set-watch"; Flags: runhidden nowait; Tasks: watch; Check: ApplyInstallDefaults
; Finish page: start DropWheel unless Setup is silent (the updater launches it separately).
Filename: "{app}\DropWheel.exe"; Description: "{cm:LaunchProgram,DropWheel}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; A running DropWheel would keep DropWheel.exe locked while the uninstaller deletes the files.
Filename: "{sys}\taskkill.exe"; Parameters: "/im DropWheel.exe /f"; Flags: runhidden; RunOnceId: "StopDropWheel"

[Code]
var
  RemoveUserData: Boolean;

{ True when no previous DropWheel is registered, i.e. this is a first installation rather than
  an upgrade. Checked in both hives so a change of install mode is still seen as an upgrade. The
  key name is AppId above (the doubled brace there only escapes it) plus "_is1"; the braces are
  part of the name and are not constants here. }
function IsFirstInstall: Boolean;
var
  UninstallKey: String;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{1AB9CDBE-C50D-484B-96ED-5D575D0806D2}_is1';
  Result := not (RegKeyExists(HKCU, UninstallKey) or RegKeyExists(HKLM, UninstallKey));
end;

{ The two installer defaults ("Start with Windows", "Watch mouse and keyboard") may change the
  app's own state. A first installation applies them; an interactive upgrade shows the boxes and
  the user decides. A SILENT upgrade - the updater runs Setup with /VERYSILENT - only replaces
  files: it must never turn an option back on that the user disabled in Settings, and it must
  never touch settings.json. }
function ApplyInstallDefaults: Boolean;
begin
  Result := (not WizardSilent) or IsFirstInstall;
end;

{ Removes the per-user shell entries the app creates (autostart, the two Explorer context-menu
  keys and the opt-in Send to shortcut). Done here rather than by running
  "DropWheel.exe --unregister" so an uninstall also cleans up when the exe is already gone,
  was never run, or is held open. }
procedure RemoveShellIntegration;
var
  RunValue: String;
  SendToShortcut: String;
begin
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DropWheel', RunValue) then
  begin
    if RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DropWheel') then
      Log('Removed the per-user Run value DropWheel.')
    else
      Log('The per-user Run value DropWheel could not be removed.');
  end
  else
    Log('No per-user Run value DropWheel to remove.');

  if RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\*\shell\DropWheel') then
    Log('Removed the file Explorer context-menu key.')
  else
    Log('No file Explorer context-menu key to remove.');

  if RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\DropWheel') then
    Log('Removed the folder Explorer context-menu key.')
  else
    Log('No folder Explorer context-menu key to remove.');

  SendToShortcut := ExpandConstant('{userappdata}\Microsoft\Windows\SendTo\DropWheel.lnk');
  if FileExists(SendToShortcut) then
  begin
    if DeleteFile(SendToShortcut) then
      Log('Removed the Send to shortcut.')
    else
      Log('The Send to shortcut could not be removed.');
  end
  else
    Log('No Send to shortcut to remove.');
end;

{ The uninstaller has no custom wizard pages: Inno Setup 6 cannot add a page to it, and
  CreateCustomForm changed its signature in 6.6 (the CI compiler is 6.7) so a form-based checkbox
  is not worth the compile risk. The one question is therefore a plain Yes/No confirmation whose
  default is "keep". Silent uninstalls - the updater, /VERYSILENT, management tools - never see it
  and always keep the settings (and the downloaded helper programs under the user's local app data). }
function AskRemoveUserData: Boolean;
var
  Answer: Integer;
begin
  Result := False;
  if UninstallSilent then
  begin
    Log('Silent uninstall: keeping the DropWheel settings.');
    Exit;
  end;
  Answer := SuppressibleMsgBox(ExpandConstant('{cm:RemoveSettingsQuestion}'),
    mbConfirmation, MB_YESNO, IDNO);
  Result := Answer = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  case CurUninstallStep of
    usUninstall:
      begin
        RemoveShellIntegration;
        RemoveUserData := AskRemoveUserData;
      end;
    usPostUninstall:
      begin
        if RemoveUserData then
        begin
          Log('Removing the DropWheel settings and downloaded helpers.');
          DelTree(ExpandConstant('{userappdata}\DropWheel'), True, True, True);
          DelTree(ExpandConstant('{localappdata}\DropWheel'), True, True, True);
        end;
      end;
  end;
end;
