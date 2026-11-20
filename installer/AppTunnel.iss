#ifndef AppVersion
  #error Specify AppVersion with /DAppVersion.
#endif
#ifndef PackageDir
  #define PackageDir "..\dist\win-x64"
#endif

[Setup]
AppId={{B7F7801F-C8D0-4C2F-AB67-8A346771F195}
AppName=AppTunnel
AppVersion={#AppVersion}
AppPublisher=AppTunnel
AppPublisherURL=https://github.com/F404X/app-tunnel
AppUpdatesURL=https://github.com/F404X/app-tunnel/releases
DefaultDirName={autopf}\AppTunnel
DisableDirPage=yes
UsePreviousAppDir=no
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\AppTunnel.exe
SetupIconFile=..\src\AppTunnel.Windows\AppTunnel.ico
PrivilegesRequired=admin
MinVersion=10.0.17763
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
WizardStyle=modern
SetupLogging=yes
SetupMutex=AppTunnel.Setup
Compression=lzma2
SolidCompression=yes
OutputBaseFilename=AppTunnel-Setup-x64
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
english.InstallWireSock=Install WireSock Secure Connect CLI (required for the VPN)
brazilianportuguese.InstallWireSock=Instalar o WireSock Secure Connect CLI (necessário para a VPN)
english.InstallWebView=Install Microsoft Edge WebView2 Runtime (for Proton sign-in)
brazilianportuguese.InstallWebView=Instalar o Microsoft Edge WebView2 Runtime (para entrar na Proton)
english.OpenAppTunnel=Open AppTunnel
brazilianportuguese.OpenAppTunnel=Abrir o AppTunnel
english.CloseAppTunnel=Close the Proton sign-in window, then right-click AppTunnel in the system tray and choose Exit interface. Run Setup again when AppTunnel has closed.
brazilianportuguese.CloseAppTunnel=Feche a janela de login da Proton. Depois, clique com o botão direito no AppTunnel na bandeja e escolha Sair da interface. Abra o instalador novamente quando o AppTunnel fechar.
english.ProtectedDirectory=AppTunnel must be installed in the AppTunnel folder under Program Files.
brazilianportuguese.ProtectedDirectory=O AppTunnel deve ser instalado na pasta AppTunnel em Arquivos de Programas.
english.CleanupFailed=Could not disconnect the AppTunnel service or remove startup. Open AppTunnel and disconnect the VPN, then try uninstalling again.
brazilianportuguese.CleanupFailed=Não foi possível desconectar o serviço do AppTunnel ou remover a inicialização. Abra o AppTunnel, desconecte a VPN e tente desinstalar novamente.

[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\AppTunnel"; Filename: "{app}\AppTunnel.exe"; WorkingDir: "{app}"

[Registry]
Root: HKLM64; Subkey: "SOFTWARE\AppTunnel\Installation"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
Filename: "{sys}\cmd.exe"; Parameters: "/d /c winget install --id NTKERNEL.WireSockVPNClientCLI --exact --source winget --interactive"; Description: "{cm:InstallWireSock}"; Flags: postinstall skipifsilent runasoriginaluser; Check: NeedsWireSock and HasWinGet
Filename: "https://wiresock.net/wiresock-secure-connect/download"; Description: "{cm:InstallWireSock}"; Flags: postinstall skipifsilent shellexec runasoriginaluser nowait; Check: NeedsWireSock and not HasWinGet
Filename: "{sys}\cmd.exe"; Parameters: "/d /c winget install --id Microsoft.EdgeWebView2Runtime --exact --source winget"; Description: "{cm:InstallWebView}"; Flags: postinstall skipifsilent runasoriginaluser; Check: NeedsWebView and HasWinGet
Filename: "https://developer.microsoft.com/microsoft-edge/webview2/"; Description: "{cm:InstallWebView}"; Flags: postinstall skipifsilent shellexec runasoriginaluser nowait; Check: NeedsWebView and not HasWinGet
Filename: "{app}\AppTunnel.exe"; Description: "{cm:OpenAppTunnel}"; Flags: postinstall skipifsilent nowait runascurrentuser

[Code]
const
  AppTunnelMutex = 'Local\AppTunnel.Interface';
  SetupEvent = 'Local\AppTunnel.QuitForSetup';
  EventModifyState = $0002;
  ReparsePoint = $0400;
  EdgeClientKey = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function WindowsFileAttributes(Name: string): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';
function OpenEvent(DesiredAccess: Cardinal; InheritHandle: BOOL; Name: string): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Event: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

function RequestInterfaceExit(): Boolean;
var
  Event: THandle;
  Attempt: Integer;
begin
  Result := not CheckForMutexes(AppTunnelMutex);
  if Result then
    exit;

  Event := OpenEvent(EventModifyState, False, SetupEvent);
  if Event = 0 then
    exit;
  try
    if not SetEvent(Event) then
      exit;
  finally
    CloseHandle(Event);
  end;

  for Attempt := 1 to 100 do
  begin
    Sleep(100);
    Result := not CheckForMutexes(AppTunnelMutex);
    if Result then
      exit;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
begin
  Result := '';
  if (CompareText(ExpandConstant('{app}'), ExpandConstant('{autopf}\AppTunnel')) <> 0) or
     (DirExists(ExpandConstant('{app}')) and
      ((WindowsFileAttributes(ExpandConstant('{app}')) and ReparsePoint) <> 0)) then
    Result := CustomMessage('ProtectedDirectory')
  else if not RequestInterfaceExit() then
    Result := CustomMessage('CloseAppTunnel');
end;

procedure CurUninstallStepChanged(Step: TUninstallStep);
var
  ExitCode: Integer;
begin
  if Step <> usUninstall then
    exit;
  if not RequestInterfaceExit() then
    RaiseException(CustomMessage('CloseAppTunnel'));
  if not FileExists(ExpandConstant('{app}\AppTunnel.exe')) then
    exit;
  if not Exec(ExpandConstant('{app}\AppTunnel.exe'), '--uninstall', ExpandConstant('{app}'),
    SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
  begin
    Log(Format('AppTunnel uninstall cleanup returned %d.', [ExitCode]));
    RaiseException(CustomMessage('CleanupFailed'));
  end;
end;

function HasWinGet(): Boolean;
begin
  Result := FileExists(ExpandConstant('{localappdata}\Microsoft\WindowsApps\winget.exe'));
end;

function SupportedWireSock(const Folder: string): Boolean;
var
  HighVersion, LowVersion: Cardinal;
begin
  Result := GetVersionNumbers(AddBackslash(Folder) + 'sdk\wiresock-client.exe', HighVersion, LowVersion)
    or GetVersionNumbers(AddBackslash(Folder) + 'wiresock-client.exe', HighVersion, LowVersion)
    or GetVersionNumbers(AddBackslash(Folder) + 'bin\wiresock-client.exe', HighVersion, LowVersion);
  if Result then
    Result := (HighVersion > $30008) or ((HighVersion = $30008) and (LowVersion >= $20001));
end;

function WireSockInRegistry(Root: Integer): Boolean;
var
  Keys: TArrayOfString;
  Key, DisplayName, Folder: string;
  Index: Integer;
  UninstallKey: string;
begin
  Result := False;
  UninstallKey := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall';
  if not RegGetSubkeyNames(Root, UninstallKey, Keys) then
    exit;
  for Index := 0 to GetArrayLength(Keys) - 1 do
  begin
    Key := UninstallKey + '\' + Keys[Index];
    if RegQueryStringValue(Root, Key, 'DisplayName', DisplayName) and
       (Pos('wiresock', Lowercase(DisplayName)) = 1) and
       RegQueryStringValue(Root, Key, 'InstallLocation', Folder) and
       (Folder <> '') and SupportedWireSock(Folder) then
    begin
      Result := True;
      exit;
    end;
  end;
end;

function NeedsWireSock(): Boolean;
begin
  Result := not (SupportedWireSock(ExpandConstant('{pf64}\WireSock Secure Connect')) or
    SupportedWireSock(ExpandConstant('{pf64}\WireSock VPN Client')) or
    WireSockInRegistry(HKLM64) or WireSockInRegistry(HKLM32));
end;

function HasWebView(Root: Integer): Boolean;
var
  Version: string;
begin
  Result := RegQueryStringValue(Root, EdgeClientKey, 'pv', Version) and
    (Version <> '') and (Version <> '0.0.0.0');
end;

function NeedsWebView(): Boolean;
begin
  Result := not (HasWebView(HKLM32) or HasWebView(HKCU));
end;
