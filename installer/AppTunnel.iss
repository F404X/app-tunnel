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
AppPublisher=F404X
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
Name: "english"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "..\docs\terms\en.txt"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"; InfoBeforeFile: "..\docs\terms\pt.txt"

Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"; InfoBeforeFile: "..\docs\terms\ru.txt"

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

russian.InstallWireSock=Установить WireSock Secure Connect CLI (для работы VPN)
russian.InstallWebView=Установить Microsoft Edge WebView2 Runtime (для входа в Proton)
russian.OpenAppTunnel=Открыть AppTunnel
russian.CloseAppTunnel=Закройте окно входа в Proton, затем нажмите правой кнопкой на значок AppTunnel в трее и выберите «Закрыть интерфейс». Запустите установщик снова.
russian.ProtectedDirectory=AppTunnel необходимо установить в папку AppTunnel в Program Files.
russian.CleanupFailed=Не удалось отключить службу AppTunnel или удалить автозапуск. Откройте AppTunnel, отключите VPN и повторите удаление.

english.TelemetryTitle=Privacy and feedback
english.TelemetryPage=Choose which optional reports AppTunnel may collect.
english.TelemetryExplain=Diagnostics require a separate approval for each report.
english.TelemetryStats=Send anonymous statistics
english.TelemetryErrors=Automatic error reports
english.TelemetryDiagnostics=Include detailed diagnostics
english.TelemetryPreview=View data sent
english.TelemetryExamples=Examples below show the full JSON schema. A new installation has no local history.
english.TelemetryClose=Close
english.TelemetrySaveFailure=Could not save telemetry preferences. Check the options in AppTunnel after installation.
brazilianportuguese.TelemetryTitle=Privacidade e feedback
brazilianportuguese.TelemetryPage=Escolha quais relatórios opcionais o AppTunnel pode coletar.
brazilianportuguese.TelemetryExplain=Cada relatório detalhado exige uma autorização separada.
brazilianportuguese.TelemetryStats=Enviar estatísticas anônimas
brazilianportuguese.TelemetryErrors=Relatórios automáticos de erros
brazilianportuguese.TelemetryDiagnostics=Incluir diagnósticos detalhados
brazilianportuguese.TelemetryPreview=Visualizar dados enviados
brazilianportuguese.TelemetryExamples=Os exemplos abaixo mostram o JSON completo. Uma instalação nova começa com o histórico vazio.
brazilianportuguese.TelemetryClose=Fechar
brazilianportuguese.TelemetrySaveFailure=Não foi possível salvar as preferências. Confira as opções no AppTunnel após instalar.
russian.TelemetryTitle=Конфиденциальность и отчёты
russian.TelemetryPage=Выберите, какие необязательные отчёты может собирать AppTunnel.
russian.TelemetryExplain=Каждый подробный отчёт требует отдельного разрешения.
russian.TelemetryStats=Отправлять анонимную статистику
russian.TelemetryErrors=Автоматические отчёты об ошибках
russian.TelemetryDiagnostics=Включать подробную диагностику
russian.TelemetryPreview=Просмотреть отправленные данные
russian.TelemetryExamples=Ниже приведены примеры JSON. При новой установке история пуста.
russian.TelemetryClose=Закрыть
russian.TelemetrySaveFailure=Не удалось сохранить параметры отчётов. Проверьте их в AppTunnel после установки.

[Files]
Source: "..\docs\telemetry-examples.txt"; Flags: dontcopy
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{app}\cs\*.resources.dll"
Type: dirifempty; Name: "{app}\cs"
Type: files; Name: "{app}\de\*.resources.dll"
Type: dirifempty; Name: "{app}\de"
Type: files; Name: "{app}\es\*.resources.dll"
Type: dirifempty; Name: "{app}\es"
Type: files; Name: "{app}\fr\*.resources.dll"
Type: dirifempty; Name: "{app}\fr"
Type: files; Name: "{app}\it\*.resources.dll"
Type: dirifempty; Name: "{app}\it"
Type: files; Name: "{app}\ja\*.resources.dll"
Type: dirifempty; Name: "{app}\ja"
Type: files; Name: "{app}\ko\*.resources.dll"
Type: dirifempty; Name: "{app}\ko"
Type: files; Name: "{app}\pl\*.resources.dll"
Type: dirifempty; Name: "{app}\pl"
Type: files; Name: "{app}\tr\*.resources.dll"
Type: dirifempty; Name: "{app}\tr"
Type: files; Name: "{app}\zh-Hans\*.resources.dll"
Type: dirifempty; Name: "{app}\zh-Hans"
Type: files; Name: "{app}\zh-Hant\*.resources.dll"
Type: dirifempty; Name: "{app}\zh-Hant"

[Icons]
Name: "{autoprograms}\AppTunnel"; Filename: "{app}\AppTunnel.exe"; WorkingDir: "{app}"

[Registry]
Root: HKLM64; Subkey: "SOFTWARE\AppTunnel\Installation"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey

Root: HKLM64; Subkey: "SOFTWARE\AppTunnel\Installation"; ValueType: dword; ValueName: "TelemetryViewer"; ValueData: "1"

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
  TelemetryKey = 'Software\F404X\AppTunnel\Telemetry';
  EventModifyState = $0002;
  ReparsePoint = $0400;
  EdgeClientKey = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

var
  TelemetryPage: TInputOptionWizardPage;

function WindowsFileAttributes(Name: string): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';
function OpenEvent(DesiredAccess: Cardinal; InheritHandle: BOOL; Name: string): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Event: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

function AppLanguage(): string;
begin
  Result := 'en';
  if ActiveLanguage() = 'brazilianportuguese' then Result := 'pt'
  else if ActiveLanguage() = 'russian' then Result := 'ru';
end;

procedure ShowTelemetryPreview(Sender: TObject);
var
  Form: TSetupForm;
  Memo: TNewMemo;
  CloseButton: TNewButton;
  Example: AnsiString;
  InstalledApp, InstalledDir: string;
  Viewer: Cardinal;
  ExitCode: Integer;
begin
  InstalledDir := ExpandConstant('{autopf}\AppTunnel');
  InstalledApp := InstalledDir + '\AppTunnel.exe';
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\AppTunnel\Installation', 'TelemetryViewer', Viewer) and
     (Viewer = 1) and FileExists(InstalledApp) and
     ((WindowsFileAttributes(InstalledDir) and ReparsePoint) = 0) and
     ((WindowsFileAttributes(InstalledApp) and ReparsePoint) = 0) then
  begin
    if Exec(InstalledApp, '--telemetry-preview ' + AppLanguage(), InstalledDir, SW_SHOWNORMAL, ewWaitUntilTerminated, ExitCode) then
      if ExitCode = 0 then exit;
  end;

  ExtractTemporaryFile('telemetry-examples.txt');
  if not LoadStringFromFile(ExpandConstant('{tmp}\telemetry-examples.txt'), Example) then Example := '';
  Form := CreateCustomForm(ScaleX(544), ScaleY(317), False, True);
  try
    Form.Caption := CustomMessage('TelemetryPreview');
    Form.Position := poScreenCenter;
    Memo := TNewMemo.Create(Form);
    Memo.Parent := Form;
    Memo.SetBounds(ScaleX(12), ScaleY(12), Form.ClientWidth - ScaleX(24), Form.ClientHeight - ScaleY(58));
    Memo.ReadOnly := True;
    Memo.ScrollBars := ssVertical;
    Memo.Text := CustomMessage('TelemetryExamples') + #13#10#13#10 + String(Example);
    CloseButton := TNewButton.Create(Form);
    CloseButton.Parent := Form;
    CloseButton.SetBounds(Form.ClientWidth - ScaleX(110), Form.ClientHeight - ScaleY(34), ScaleX(98), ScaleY(23));
    CloseButton.Caption := CustomMessage('TelemetryClose');
    CloseButton.ModalResult := mrCancel;
    CloseButton.Cancel := True;
    Form.ShowModal();
  finally
    Form.Free();
  end;
end;

procedure InitializeWizard();
var
  Consent: Cardinal;
  Preview: TNewButton;
begin
  TelemetryPage := CreateInputOptionPage(wpWelcome, CustomMessage('TelemetryTitle'),
    CustomMessage('TelemetryPage'), CustomMessage('TelemetryExplain'), False, False);
  TelemetryPage.Add(CustomMessage('TelemetryStats'));
  TelemetryPage.Add(CustomMessage('TelemetryErrors'));
  TelemetryPage.Add(CustomMessage('TelemetryDiagnostics'));
  Consent := 0;
  RegQueryDWordValue(HKCU, TelemetryKey, 'Consent', Consent);
  TelemetryPage.Values[0] := (Consent and 1) <> 0;
  TelemetryPage.Values[1] := (Consent and 2) <> 0;
  TelemetryPage.Values[2] := (Consent and 4) <> 0;
  TelemetryPage.CheckListBox.Height := ScaleY(92);
  Preview := TNewButton.Create(TelemetryPage);
  Preview.Parent := TelemetryPage.Surface;
  Preview.SetBounds(0, TelemetryPage.CheckListBox.Top + TelemetryPage.CheckListBox.Height + ScaleY(8), ScaleX(205), ScaleY(25));
  Preview.Caption := CustomMessage('TelemetryPreview');
  Preview.OnClick := @ShowTelemetryPreview;
end;

procedure CurStepChanged(Step: TSetupStep);
var
  Consent: Cardinal;
  ExitCode: Integer;
begin
  if (Step = ssPostInstall) and not WizardSilent then
  begin
    Consent := 0;
    if TelemetryPage.Values[0] then Consent := Consent or 1;
    if TelemetryPage.Values[1] then Consent := Consent or 2;
    if TelemetryPage.Values[2] then Consent := Consent or 4;
    if not RegWriteDWordValue(HKCU, TelemetryKey, 'Consent', Consent) then
      SuppressibleMsgBox(CustomMessage('TelemetrySaveFailure'), mbError, MB_OK, IDOK);
  end;
  if (Step = ssDone) and WizardSilent and (CompareText(ExpandConstant('{param:REOPENAPPTUNNEL|0}'), '1') = 0) then
  begin
    if not Exec(ExpandConstant('{app}\AppTunnel.exe'), '--startup', ExpandConstant('{app}'),
      SW_SHOWNORMAL, ewNoWait, ExitCode) then Log('AppTunnel could not restart automatically.');
  end;
end;

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
