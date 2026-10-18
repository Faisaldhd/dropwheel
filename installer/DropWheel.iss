; Inno Setup script – per-user install, no admin rights needed for DropWheel itself.
;
; DropWheel is published framework-dependent (.NET 10), so Setup also checks for the
; .NET 10 Desktop Runtime (x64) and, when it is missing, downloads the official Microsoft
; installer, verifies its Authenticode signature and runs it with UAC (the runtime install
; needs administrator permission; the app itself stays a per-user install).
;
; Build: iscc /DAppVersion=1.2.3 installer\DropWheel.iss
; (release.cmd publishes the app to ..\..\release\DropWheel first)

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
; DropWheel targets Windows 10 1809+ (SupportedOSPlatformVersion in FileAction.App.csproj), and so
; does the .NET 10 Desktop Runtime. Refuse to install on older Windows instead of failing to start.
MinVersion=10.0.17763
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
; A failed .NET runtime install is the case where the owner needs a log; %TEMP%\Setup Log*.txt.
SetupLogging=yes
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
; Shown when the .NET 10 Desktop Runtime is missing.
DotNetNeeded=DropWheel needs the free Microsoft .NET 10 Desktop Runtime (about 55 MB, installed once) and it is not installed on this PC.
DotNetWillInstall=Setup will download it from Microsoft, check Microsoft's digital signature and install it now. Windows will ask for administrator permission for the runtime installer only; DropWheel itself still installs for the current user.
DotNetDownloadTitle=Microsoft .NET 10 Desktop Runtime
DotNetDownloading=DropWheel needs the free Microsoft .NET 10 Desktop Runtime (about 55 MB, installed once). Setup is downloading it from Microsoft.
DotNetInstalling=Installing the Microsoft .NET 10 Desktop Runtime (about 55 MB). Windows may ask for administrator permission; the DropWheel wizard continues when the runtime setup has finished.
DotNetFailed=The Microsoft .NET 10 Desktop Runtime could not be installed. %1
DotNetManual=Setup will open the Microsoft download page in your browser. Install the .NET 10 Desktop Runtime there, then run the DropWheel setup again: DropWheel cannot start without it.

[Files]
Source: "..\..\release\DropWheel\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: recursesubdirs createallsubdirs ignoreversion
; Signature check for the downloaded .NET runtime installer; extracted to {tmp} on demand.
Source: "..\scripts\verify-authenticode.ps1"; DestDir: "{tmp}"; Flags: dontcopy

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

[Code]
const
  DotNetDownloadUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  DotNetDownloadPageUrl = 'https://dotnet.microsoft.com/download/dotnet/10.0';
  DotNetRuntimeName = 'Microsoft.WindowsDesktop.App';
  DotNetRegistryKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  DotNetInstallerName = 'windowsdesktop-runtime-win-x64.exe';
  DotNetMinimumMajor = 10;

var
  DownloadPage: TDownloadWizardPage;
  InstallPage: TOutputProgressWizardPage;
  RuntimeInstallerExitCode: Integer;

function MajorVersionOf(const Version: String): Integer;
var
  P: Integer;
begin
  P := Pos('.', Version);
  if P > 1 then
    Result := StrToIntDef(Copy(Version, 1, P - 1), 0)
  else
    Result := StrToIntDef(Version, 0);
end;

{ Reads one "sharedfx" registry key and accepts any 10.x or newer value name (the installed versions). }
function RuntimeVersionInRegistry(const RootKey: Integer; const SubKey: String): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(RootKey, SubKey, Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if MajorVersionOf(Names[I]) >= DotNetMinimumMajor then
      begin
        Log(Format('Found %s %s in the registry.', [DotNetRuntimeName, Names[I]]));
        Result := True;
        Break;
      end;
end;

{ x64 installs write the 64-bit registry view; per-user installs use HKCU. }
function RuntimeInRegistry: Boolean;
begin
  Result :=
    RuntimeVersionInRegistry(HKLM64, DotNetRegistryKey) or
    RuntimeVersionInRegistry(HKCU64, DotNetRegistryKey) or
    RuntimeVersionInRegistry(HKEY_LOCAL_MACHINE, DotNetRegistryKey) or
    RuntimeVersionInRegistry(HKEY_CURRENT_USER, DotNetRegistryKey);
end;

function DotNetExePath: String;
begin
  Result := '';
  if FileExists(ExpandConstant('{commonpf64}\dotnet\dotnet.exe')) then
    Result := ExpandConstant('{commonpf64}\dotnet\dotnet.exe')
  else if FileExists(ExpandConstant('{commonpf32}\dotnet\dotnet.exe')) then
    Result := ExpandConstant('{commonpf32}\dotnet\dotnet.exe')
  else if FileExists(ExpandConstant('{localappdata}\Microsoft\dotnet\dotnet.exe')) then
    Result := ExpandConstant('{localappdata}\Microsoft\dotnet\dotnet.exe');
end;

{ Fallback when the registry cannot be read: ask the dotnet host itself. }
function RuntimeInListRuntimes: Boolean;
var
  DotNet, Command, OutputFile, Line, Version: String;
  ResultCode, I, SpaceP: Integer;
  Lines: TArrayOfString;
begin
  Result := False;
  DotNet := DotNetExePath;
  if DotNet = '' then
  begin
    Log('dotnet --list-runtimes: dotnet.exe was not found.');
    Exit;
  end;
  OutputFile := ExpandConstant('{tmp}\dotnet-runtimes.txt');
  DeleteFile(OutputFile);
  Command := '/c ""' + DotNet + '" --list-runtimes > "' + OutputFile + '" 2>&1"';
  if not Exec(ExpandConstant('{cmd}'), Command, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Log('dotnet --list-runtimes: cmd.exe could not be started.');
    Exit;
  end;
  if not LoadStringsFromFile(OutputFile, Lines) then
  begin
    Log('dotnet --list-runtimes: no output.');
    Exit;
  end;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    Line := Lines[I];
    if Pos(DotNetRuntimeName + ' ', Line) = 1 then
    begin
      Version := Copy(Line, Length(DotNetRuntimeName) + 2, MaxInt);
      SpaceP := Pos(' ', Version);
      if SpaceP > 0 then
        Version := Copy(Version, 1, SpaceP - 1);
      if MajorVersionOf(Version) >= DotNetMinimumMajor then
      begin
        Log('dotnet --list-runtimes: found ' + DotNetRuntimeName + ' ' + Version + '.');
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function DotNetRuntimePresent: Boolean;
begin
  Result := RuntimeInRegistry or RuntimeInListRuntimes;
end;

{ Downloads windowsdesktop-runtime-win-x64.exe to the temporary folder. Shows the download page unless silent. }
function DownloadRuntimeInstaller(var Detail: String): Boolean;
begin
  Result := False;
  Detail := '';
  if WizardSilent then
  begin
    try
      DownloadTemporaryFile(DotNetDownloadUrl, DotNetInstallerName, '', nil);
      Result := True;
    except
      Detail := 'the download failed (' + GetExceptionMessage + ').';
    end;
    Exit;
  end;
  DownloadPage.Clear;
  DownloadPage.Add(DotNetDownloadUrl, DotNetInstallerName, '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      Result := True;
    except
      if DownloadPage.AbortedByUser then
        Detail := 'the download was cancelled.'
      else
        Detail := 'the download failed (' + GetExceptionMessage + ').';
    end;
  finally
    DownloadPage.Hide;
  end;
end;

{ Runs scripts\verify-authenticode.ps1 (Get-AuthenticodeSignature) and reads its report. }
function VerifyRuntimeInstallerSignature(var Detail: String): Boolean;
var
  ScriptPath, ReportPath, InstallerPath, Params: String;
  ResultCode, I: Integer;
  Lines: TArrayOfString;
  Status, Signer, ResultValue: String;
begin
  Result := False;
  Detail := '';
  InstallerPath := ExpandConstant('{tmp}\') + DotNetInstallerName;
  ScriptPath := ExpandConstant('{tmp}\verify-authenticode.ps1');
  ReportPath := ExpandConstant('{tmp}\verify-authenticode-report.txt');
  ExtractTemporaryFile('verify-authenticode.ps1');
  DeleteFile(ReportPath);
  { -File would refuse the unsigned helper script on machines with an AllSigned/RemoteSigned execution
    policy, so the embedded script is run as a script block instead (same file, no policy dependency). }
  Params := Format('-NoProfile -NonInteractive -Command "& ([scriptblock]::Create((Get-Content -Raw -LiteralPath ''%s''))) -Path ''%s'' -ReportPath ''%s''"', [StringChangeEx(ScriptPath, '''', '''''', True), StringChangeEx(InstallerPath, '''', '''''', True), StringChangeEx(ReportPath, '''', '''''', True)]);
  Log('Checking the Authenticode signature of the runtime installer...');
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params, '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Detail := 'Windows PowerShell could not be started to check the signature.';
    Exit;
  end;
  if not LoadStringsFromFile(ReportPath, Lines) then
  begin
    Detail := 'the signature check did not produce a result.';
    Exit;
  end;
  Status := '';
  Signer := '';
  ResultValue := '';
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    if Pos('STATUS=', Lines[I]) = 1 then
      Status := Copy(Lines[I], Length('STATUS=') + 1, MaxInt)
    else if Pos('SIGNER=', Lines[I]) = 1 then
      Signer := Copy(Lines[I], Length('SIGNER=') + 1, MaxInt)
    else if Pos('RESULT=', Lines[I]) = 1 then
      ResultValue := Copy(Lines[I], Length('RESULT=') + 1, MaxInt)
    else if Pos('ERROR=', Lines[I]) = 1 then
      Detail := Copy(Lines[I], Length('ERROR=') + 1, MaxInt);
  end;
  if ResultValue = 'OK' then
  begin
    Log('Signature is valid, signer: ' + Signer);
    Result := True;
  end
  else if Detail = '' then
    Detail := Format('signature status "%s" from signer "%s".', [Status, Signer]);
end;

{ Runs the runtime installer elevated (/install /quiet /norestart). 0 and 3010 both mean success. }
function RunRuntimeInstaller(var Detail: String): Boolean;
var
  InstallerPath: String;
  ResultCode: Integer;
begin
  Result := False;
  Detail := '';
  InstallerPath := ExpandConstant('{tmp}\') + DotNetInstallerName;
  InstallPage.SetText(ExpandConstant('{cm:DotNetInstalling}'), '');
  InstallPage.SetProgress(0, 0);
  if not WizardSilent then
  begin
    InstallPage.Show;
    { Let the waiting page paint before the blocking elevated installer starts. }
    Sleep(150);
  end;
  try
    Log('Starting the .NET Desktop Runtime installer elevated (UAC).');
    if not ShellExec('runas', InstallerPath, '/install /quiet /norestart',
        ExpandConstant('{tmp}'), SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
    begin
      Detail := 'Windows did not start the runtime installer (administrator permission was declined).';
      Exit;
    end;
    RuntimeInstallerExitCode := ResultCode;
    Log(Format('The .NET Desktop Runtime installer exited with code %d.', [ResultCode]));
    if (ResultCode <> 0) and (ResultCode <> 3010) then
    begin
      Detail := Format('the runtime installer exited with code %d.', [ResultCode]);
      Exit;
    end;
    Result := True;
  finally
    if not WizardSilent then
      InstallPage.Hide;
  end;
end;

{ One clear message, then the Microsoft download page in the browser, then a clean abort. }
function RuntimeFailure(const Detail: String): String;
var
  Message: String;
  ResultCode: Integer;
begin
  if Detail <> '' then
    Log('Runtime install failed: ' + Detail);
  Message := ExpandConstant('{cm:DotNetFailed}');
  if Detail <> '' then
    Message := FmtMessage(Message, [Detail]);
  Message := Message + #13#10#13#10 + ExpandConstant('{cm:DotNetManual}');
  SuppressibleMsgBox(Message, mbCriticalError, MB_OK, IDOK);
  if not WizardSilent then
    ShellExec('open', DotNetDownloadPageUrl, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
  Result := 'The Microsoft .NET 10 Desktop Runtime is required to run DropWheel. ' +
    'Install it from ' + DotNetDownloadPageUrl + ' and run this setup again.';
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(ExpandConstant('{cm:DotNetDownloadTitle}'),
    ExpandConstant('{cm:DotNetDownloading}'), nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
  InstallPage := CreateOutputProgressPage(ExpandConstant('{cm:DotNetDownloadTitle}'),
    ExpandConstant('{cm:DotNetInstalling}'));
  InstallPage.SetText(ExpandConstant('{cm:DotNetInstalling}'), '');
  InstallPage.SetProgress(0, 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Detail: String;
  Answer: Integer;
  ResultCode: Integer;
begin
  Result := '';
  NeedsRestart := False;
  RuntimeInstallerExitCode := 0;
  if DotNetRuntimePresent then
  begin
    Log('The .NET 10 Desktop Runtime is installed.');
    Exit;
  end;
  Log('The .NET 10 Desktop Runtime was not found; it will be installed.');
  if not WizardSilent then
  begin
    Answer := SuppressibleMsgBox(
      ExpandConstant('{cm:DotNetNeeded}') + #13#10#13#10 + ExpandConstant('{cm:DotNetWillInstall}'),
      mbConfirmation, MB_OKCANCEL, IDOK);
    if Answer <> IDOK then
    begin
      Log('The user declined the .NET Desktop Runtime installation.');
      ShellExec('open', DotNetDownloadPageUrl, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
      Result := 'DropWheel cannot be installed without the Microsoft .NET 10 Desktop Runtime.';
      Exit;
    end;
  end;
  if not DownloadRuntimeInstaller(Detail) then
  begin
    Result := RuntimeFailure(Detail);
    Exit;
  end;
  if not VerifyRuntimeInstallerSignature(Detail) then
  begin
    Result := RuntimeFailure(Detail);
    Exit;
  end;
  if not RunRuntimeInstaller(Detail) then
  begin
    Result := RuntimeFailure(Detail);
    Exit;
  end;
  if RuntimeInstallerExitCode = 3010 then
    NeedsRestart := True;
  if not DotNetRuntimePresent then
  begin
    Result := RuntimeFailure('the runtime installer reported success, but the runtime is still not detected.');
    Exit;
  end;
  Log('The .NET 10 Desktop Runtime was installed.');
end;
