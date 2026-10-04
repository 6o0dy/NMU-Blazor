; NMU Platform - Windows installer script (Inno Setup 6).
; Installs the NORMAL framework-dependent publish folder (many files -> one setup.exe).
; Built ONLY by publish-all.ps1 - never by hand:
;   ISCC.exe WindowsSetup.iss /DAppVersion="1.0" /DSourceDir="C:\...\publish" /DOutDir="C:\...\release" /DOutBase="NMU-Platform-Setup-v1.0-x64"
; AppId must NEVER change (Windows uses it to detect upgrades and uninstalls).

#define AppName "NMU Platform"
#define AppPublisher "NMU"
#ifndef AppVersion
  #define AppVersion "1.0"
#endif
#ifndef SourceDir
  #define SourceDir "."
#endif
#ifndef OutDir
  #define OutDir "."
#endif
#ifndef OutBase
  #define OutBase "NMU-Platform-Setup"
#endif
#ifndef IconFile
  #define IconFile "appicon.ico"
#endif

[Setup]
AppId={{EAC31124-66FB-4378-8B4C-B914C92A0614}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autodesktop}\{#AppName}
SetupIconFile={#IconFile}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir={#OutDir}
OutputBaseFilename={#OutBase}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\NMU.Platform.exe
DisableProgramGroupPage=yes

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[UninstallDelete]
; WebView2 creates its data folder next to the exe at runtime - remove it so
; uninstall leaves nothing behind.
Type: filesandordirs; Name: "{app}\NMU.Platform.exe.WebView2"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\NMU.Platform.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\NMU.Platform.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; Flags: unchecked

[Run]
Filename: "{app}\NMU.Platform.exe"; Description: "Launch NMU Platform"; Flags: nowait postinstall skipifsilent

[Code]
{ Checks for the .NET 10 Desktop Runtime before installing. Students without
  it would get a silent crash, so offer the download page instead. }
function HasDotNetDesktop10(): Boolean;
var
  ResultCode: Integer;
  TmpFile: String;
  Contents: AnsiString;
begin
  Result := False;
  TmpFile := ExpandConstant('{tmp}\dnruntimes.txt');
  if Exec('cmd.exe', '/c dotnet --list-runtimes > "' + TmpFile + '" 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringFromFile(TmpFile, Contents) then
      Result := Pos('Microsoft.WindowsDesktop.App 10.', Contents) > 0;
    DeleteFile(TmpFile);
  end;
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not HasDotNetDesktop10() then
  begin
    if MsgBox('NMU Platform needs the .NET 10 Desktop Runtime, which was not found on this PC.' + #13#10 + #13#10 +
      'Press Yes to open the download page (install it, then run this setup again).' + #13#10 +
      'Press No to continue installing anyway.', mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOW, ewNoWait, ResultCode);
      Result := False;
    end;
  end;
end;
