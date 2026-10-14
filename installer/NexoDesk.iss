; NexoDesk New: interface elevada e serviço SYSTEM num único instalador.
#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#define AppExe "NexoDesk New.exe"
#define ServiceName "NexoDeskRemote"

[Setup]
AppId={{14A345F9-A58C-4AA3-9C29-8084C765E437}
AppName=NexoDesk New
AppVersion={#AppVersion}
AppVerName=NexoDesk New {#AppVersion}
AppPublisher=Nexotool
AppCopyright=Nexotool
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\NexoDesk New
DefaultGroupName=NexoDesk New
DisableProgramGroupPage=yes
DisableDirPage=yes
OutputDir=..\artifacts
OutputBaseFilename=NexoDesk-New-Setup-{#AppVersion}
SetupIconFile=..\src\NexoDesk.App\Assets\nexodesk.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
PrivilegesRequired=admin
MinVersion=10.0.19045
CloseApplications=force
CloseApplicationsFilter=NexoDesk New.exe,NexoDesk.Service.exe
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos:"

[Files]
Source: "..\publish\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\NexoDesk New"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\NexoDesk New"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""NexoDesk New (Tailscale)"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""NexoDesk New (Rede local)"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""NexoDesk New (Tailscale)"" dir=in action=allow program=""{app}\service\NexoDesk.Service.exe"" protocol=TCP localport=47115 remoteip=100.64.0.0/10,fd7a:115c:a1e0::/48 profile=any"; Flags: runhidden waituntilterminated; StatusMsg: "Configurando a rede Tailscale..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""NexoDesk New (Rede local)"" dir=in action=allow program=""{app}\service\NexoDesk.Service.exe"" protocol=TCP localport=47115 remoteip=LocalSubnet profile=private"; Flags: runhidden waituntilterminated; StatusMsg: "Configurando a rede local..."
Filename: "{app}\{#AppExe}"; Description: "Abrir o NexoDesk New agora"; Flags: nowait postinstall skipifsilent runascurrentuser; Check: NotUpdaterRun

Filename: "{app}\{#AppExe}"; Flags: nowait runascurrentuser; Check: IsUpdaterRun

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "PararServico"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "RemoverServico"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""NexoDesk-New.Interface"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "RemoverTarefa"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""NexoDesk New (Tailscale)"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoverFirewallTailscale"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""NexoDesk New (Rede local)"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoverFirewallLocal"

[Code]
function GetLastError(): LongWord;
  external 'GetLastError@kernel32.dll stdcall';

function OpenProcess(Access: LongWord; InheritHandle: LongWord; ProcessId: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function GetExitCodeProcess(Handle: THandle; var Code: LongWord): Integer;
  external 'GetExitCodeProcess@kernel32.dll stdcall';
function QueryFullProcessImageName(Handle: THandle; Flags: LongWord; Name: String; var Size: LongWord): Integer;
  external 'QueryFullProcessImageNameW@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Timeout: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function TerminateProcess(Handle: THandle; Code: LongWord): Integer;
  external 'TerminateProcess@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Integer;
  external 'CloseHandle@kernel32.dll stdcall';

function IsUpdaterRun(): Boolean;
begin
  Result := StrToIntDef(ExpandConstant('{param:UPDATEPID|0}'), 0) > 0;
end;

function NotUpdaterRun(): Boolean;
begin
  Result := not IsUpdaterRun();
end;

function IsInstallationExecutable(Name: String): Boolean;
begin
  Result := (CompareText(Name, ExpandConstant('{app}\{#AppExe}')) = 0) or
    (CompareText(Name, ExpandConstant('{app}\service\NexoDesk.Service.exe')) = 0);
end;

function StopPreviousProcessByPid(ProcessId: LongWord): String;
var
  Code, Size: LongWord;
  Handle: THandle;
  Name: String;
begin
  Result := '';

  if ProcessId = 0 then exit;
  Handle := OpenProcess($00101001, 0, ProcessId);
  if Handle = 0 then
  begin
    if GetLastError() <> 87 then Result := 'Não foi possível encerrar um processo da versão anterior.';
    exit;
  end;
  try
    if (GetExitCodeProcess(Handle, Code) <> 0) and (Code <> 259) then exit;
    SetLength(Name, 32768);
    Size := 32768;
    if QueryFullProcessImageName(Handle, 0, Name, Size) = 0 then
    begin
      Result := 'Não foi possível conferir o processo da versão anterior.';
      exit;
    end;
    SetLength(Name, Size);
    if not IsInstallationExecutable(Name) then
    begin
      Result := 'Atualize pela interface instalada em Program Files\NexoDesk New.';
      exit;
    end;
    { Espera saída ordenada; termina somente o executável desta instalação se ele travar. }
    if WaitForSingleObject(Handle, 15000) = 258 then
    begin
      if TerminateProcess(Handle, 0) = 0 then
      begin
        Result := 'A versão anterior não encerrou. Feche o NexoDesk New e tente novamente.';
        exit;
      end;
      if WaitForSingleObject(Handle, 5000) <> 0 then
        Result := 'Ainda há um processo antigo em execução.';
    end;
  finally
    CloseHandle(Handle);
  end;
end;

function EnumProcesses(var FirstId: LongWord; BufferSize: LongWord; var Needed: LongWord): Integer;
  external 'EnumProcesses@psapi.dll stdcall';

function StopPreviousProcesses(): String;
var
  Ids: array of LongWord;
  Needed, I, Size, ProcessId: LongWord;
  Handle: THandle;
  Name: String;
begin
  Result := '';
  ProcessId := StrToIntDef(ExpandConstant('{param:UPDATEPID|0}'), 0);
  if ProcessId > 0 then
  begin
    Result := StopPreviousProcessByPid(ProcessId);
    if Result <> '' then exit;
  end;
  { Também atende a atualização iniciada pela versão 1.0, que ainda não envia UPDATEPID. }
  SetArrayLength(Ids, 4096);
  if (EnumProcesses(Ids[0], 4096 * 4, Needed) = 0) or (Needed >= 4096 * 4) then
  begin
    Result := 'Não foi possível conferir os processos da instalação anterior.';
    exit;
  end;
  if Needed = 0 then exit;
  for I := 0 to (Needed div 4) - 1 do
  begin
    Handle := OpenProcess($1000, 0, Ids[I]);
    if Handle <> 0 then
    begin
      try
        SetLength(Name, 32768);
        Size := 32768;
        if QueryFullProcessImageName(Handle, 0, Name, Size) <> 0 then
        begin
          SetLength(Name, Size);
          if IsInstallationExecutable(Name) then
          begin
            Result := StopPreviousProcessByPid(Ids[I]);
            if Result <> '' then exit;
          end;
        end;
      finally
        CloseHandle(Handle);
      end;
    end;
  end;
end;
function RunSystem(Command: String; Parameters: String): Integer;
var
  ExitCode: Integer;
begin
  if not Exec(ExpandConstant('{sys}\' + Command), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    ExitCode := -1;
  Result := ExitCode;
end;

function StopInstalledService(): String;
var
  Code: Integer;
begin
  Result := '';
  Code := RunSystem('sc.exe', 'failure {#ServiceName} reset= 86400 actions= ""');
  if (Code <> 0) and (Code <> 1060) then
  begin
    Result := 'Não foi possível suspender a recuperação do serviço antigo.';
    exit;
  end;
  Code := RunSystem('sc.exe', 'stop {#ServiceName}');
  if (Code <> 0) and (Code <> 1060) and (Code <> 1062) and (Code <> 1061) then
    Result := 'Não foi possível parar o serviço antigo.';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { Desativa recuperação antes da parada: o SCM não pode recriar a versão antiga. }
  Result := StopInstalledService();
  if Result <> '' then exit;
  Result := StopPreviousProcesses();
  if Result = '' then Result := StopPreviousProcesses();
  { Cada PID é conferido pelo caminho completo, inclusive o agente e o supervisor.
    Qualquer falha impede substituir arquivos ainda em uso; Tailscale não é afetado. }
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Binary: String;
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    Binary := '\"' + ExpandConstant('{app}\service\NexoDesk.Service.exe') + '\"';
    if RunSystem('sc.exe', 'query {#ServiceName}') = 0 then
      ResultCode := RunSystem('sc.exe', 'config {#ServiceName} binPath= "' + Binary + '" start= auto obj= LocalSystem')
    else
      ResultCode := RunSystem('sc.exe', 'create {#ServiceName} binPath= "' + Binary + '" start= auto obj= LocalSystem DisplayName= "NexoDesk New - Acesso remoto"');
    if ResultCode <> 0 then
      RaiseException('Falha ao registrar o serviço NexoDesk New (' + IntToStr(ResultCode) + ').');
    RunSystem('sc.exe', 'failure {#ServiceName} reset= 86400 actions= restart/5000/restart/15000/restart/60000');
    RunSystem('sc.exe', 'description {#ServiceName} "Acesso privado por certificado e senha, na sessão física ativa."');
    ResultCode := RunSystem('sc.exe', 'start {#ServiceName}');
    if (ResultCode <> 0) and (ResultCode <> 1056) then
      RaiseException('O serviço NexoDesk New não iniciou (' + IntToStr(ResultCode) + ').');
  end;
end;
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Error: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    Error := StopInstalledService();
    if Error = '' then Error := StopPreviousProcesses();
    if Error = '' then Error := StopPreviousProcesses();
    if Error <> '' then RaiseException(Error);
  end;
end;
