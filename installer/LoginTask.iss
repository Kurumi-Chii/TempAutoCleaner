const
  TaskMarker = '{#InstallerId} logon cleanup';
  TaskTriggerLogon = 9;
  TaskActionExec = 0;
  TaskLogonInteractiveToken = 3;
  TaskRunLevelLeastPrivilege = 0;
  TaskInstancesIgnoreNew = 2;
  WaitObject = 0;
  WaitAbandoned = $80;
  WaitTimeout = $102;

var
  CleanupMutex: THandle;
  TaskFolder: Variant;
  UserSid: String;
  UserAccount: String;
  TaskName: String;
  PriorTaskXml: String;
  TaskChanged: Boolean;
  TaskCommitted: Boolean;

{ Inno Setup 6 runs this script in a 32-bit process, even in 64-bit install mode. }
function CreateMutexHandle(Attributes: LongWord; InitialOwner: LongBool; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
function WaitForHandle(Handle: THandle; Milliseconds: Cardinal): Cardinal;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function ReleaseMutexHandle(Handle: THandle): LongBool;
  external 'ReleaseMutex@kernel32.dll stdcall';
function CloseKernelHandle(Handle: THandle): LongBool;
  external 'CloseHandle@kernel32.dll stdcall';
function LookupAccountName(SystemName: LongWord; AccountName: String; Sid: AnsiString;
  var SidSize: Cardinal; Domain: String; var DomainSize: Cardinal; var SidUse: Cardinal): LongBool;
  external 'LookupAccountNameW@advapi32.dll stdcall';
function ConvertSidToString(Sid: AnsiString; var Value: LongWord): LongBool;
  external 'ConvertSidToStringSidW@advapi32.dll stdcall';
function CopySidString(Buffer: String; Value: LongWord; MaxLength: Integer): LongWord;
  external 'lstrcpynW@kernel32.dll stdcall';
function FreeSidString(Value: LongWord): LongWord;
  external 'LocalFree@kernel32.dll stdcall';

function CurrentUserSid: String;
var
  Output: TExecOutput;
  ExitCode, Comma, I: Integer;
begin
  if UserSid <> '' then begin
    Result := UserSid;
    Exit;
  end;
  if not ExecAndCaptureOutput(ExpandConstant('{sys}\whoami.exe'), '/user /fo csv /nh',
      '', SW_HIDE, ewWaitUntilTerminated, ExitCode, Output) then
    RaiseException(CustomMessage('TaskError'));
  if (ExitCode <> 0) or Output.Error or (GetArrayLength(Output.StdOut) <> 1) then
    RaiseException(CustomMessage('TaskError'));
  Comma := RPos(',', Output.StdOut[0]);
  Result := RemoveQuotes(Trim(Copy(Output.StdOut[0], Comma + 1, MaxInt)));
  if (Comma = 0) or (Copy(Result, 1, 4) <> 'S-1-') or (Length(Result) < 5) then
    RaiseException(CustomMessage('TaskError'));
  for I := 5 to Length(Result) do
    if (Result[I] <> '-') and ((Result[I] < '0') or (Result[I] > '9')) then
      RaiseException(CustomMessage('TaskError'));
  UserAccount := RemoveQuotes(Trim(Copy(Output.StdOut[0], 1, Comma - 1)));
  if UserAccount = '' then RaiseException(CustomMessage('TaskError'));
  UserSid := Result;
  TaskName := '{#InstallerId}-' + UserSid;
end;

function AcquireCleanupMutex: Boolean;
var
  Handle: THandle;
  Status: Cardinal;
begin
  Result := True;
  if CleanupMutex <> 0 then Exit;
  Handle := CreateMutexHandle(0, False, 'Global\{#InstallerId}-' + CurrentUserSid);
  if Handle = 0 then RaiseException(CustomMessage('TaskError'));
  Status := WaitForHandle(Handle, 0);
  if (Status = WaitObject) or (Status = WaitAbandoned) then
    CleanupMutex := Handle
  else begin
    CloseKernelHandle(Handle);
    if Status <> WaitTimeout then RaiseException(CustomMessage('TaskError'));
    Result := False;
  end;
end;

procedure ReleaseCleanupMutex;
begin
  if CleanupMutex <> 0 then begin
    ReleaseMutexHandle(CleanupMutex);
    CloseKernelHandle(CleanupMutex);
    CleanupMutex := 0;
  end;
end;

function ConnectScheduler: Variant;
begin
  CurrentUserSid;
  Result := CreateOleObject('Schedule.Service');
  Result.Connect();
  TaskFolder := Result.GetFolder('\');
end;

function FindLoginTask: Variant;
var
  Tasks, Candidate: Variant;
  I: Integer;
begin
  Result := Null;
  Tasks := TaskFolder.GetTasks(1);
  for I := 1 to Tasks.Count do begin
    Candidate := Tasks.Item(I);
    if SameText(Candidate.Name, TaskName) then begin
      Result := Candidate;
      Exit;
    end;
  end;
end;

function IsCurrentTaskUser(const Value: String): Boolean;
var
  Sid: AnsiString;
  Domain, SidString: String;
  SidSize, DomainSize, SidUse, StringPointer: Cardinal;
  Terminator: Integer;
begin
  Result := SameText(Value, UserSid) or SameText(Value, UserAccount);
  if Result or (Value = '') then Exit;
  SidSize := 68;
  DomainSize := 256;
  Sid := StringOfChar(#0, SidSize);
  Domain := StringOfChar(#0, DomainSize);
  if not LookupAccountName(0, Value, Sid, SidSize, Domain, DomainSize, SidUse) then Exit;
  if not ConvertSidToString(Sid, StringPointer) then Exit;
  try
    SidString := StringOfChar(#0, 184);
    if CopySidString(SidString, StringPointer, 184) = 0 then Exit;
    Terminator := Pos(#0, SidString);
    if Terminator > 0 then SetLength(SidString, Terminator - 1);
    Result := SameText(SidString, UserSid);
  finally
    FreeSidString(StringPointer);
  end;
end;

procedure RequireOwnedTask(Task: Variant);
var
  Definition, Trigger, Action: Variant;
begin
  Definition := Task.Definition;
  if (Definition.RegistrationInfo.Description <> TaskMarker) or
     not IsCurrentTaskUser(Definition.Principal.UserId) or
     (Definition.Principal.LogonType <> TaskLogonInteractiveToken) or
     (Definition.Principal.RunLevel <> TaskRunLevelLeastPrivilege) or
     (Definition.Triggers.Count <> 1) or (Definition.Actions.Count <> 1) then
    RaiseException(CustomMessage('TaskError'));
  Trigger := Definition.Triggers.Item(1);
  Action := Definition.Actions.Item(1);
  if (IDispatchInvoke(Trigger, False, 'Type', []) <> TaskTriggerLogon) or
     not IsCurrentTaskUser(Trigger.UserId) or
     (IDispatchInvoke(Action, False, 'Type', []) <> TaskActionExec) or
     not SameText(ExtractFileName(Action.Path), 'TempAutoCleaner.exe') then
    RaiseException(CustomMessage('TaskError'));
end;

procedure ImportTaskXml(const Xml: String; ReplaceExisting: Boolean);
var
  XmlFile, Content, Parameters: String;
  Output: TExecOutput;
  ExitCode, DeclarationEnd, I, CodePoint: Integer;
begin
  XmlFile := ExpandConstant('{tmp}\logon-task.xml');
  Content := Trim(Xml);
  if Copy(Content, 1, 5) = '<?xml' then begin
    DeclarationEnd := Pos('?>', Content);
    if DeclarationEnd = 0 then RaiseException(CustomMessage('TaskError'));
    Delete(Content, 1, DeclarationEnd + 1);
  end;
  { Keep the XML transport ASCII while preserving non-ASCII characters through XML references. }
  Parameters := '';
  I := 1;
  while I <= Length(Content) do begin
    CodePoint := Ord(Content[I]);
    if (CodePoint >= $D800) and (CodePoint <= $DBFF) and (I < Length(Content)) then begin
      if (Ord(Content[I + 1]) < $DC00) or (Ord(Content[I + 1]) > $DFFF) then
        RaiseException(CustomMessage('TaskError'));
      CodePoint := $10000 + (CodePoint - $D800) * $400 + Ord(Content[I + 1]) - $DC00;
      I := I + 1;
    end;
    if CodePoint > 127 then
      Parameters := Parameters + '&#' + IntToStr(CodePoint) + ';'
    else
      Parameters := Parameters + Content[I];
    I := I + 1;
  end;
  if not SaveStringToFile(XmlFile, Utf8Encode(Parameters), False) then
    RaiseException(CustomMessage('TaskError'));
  try
    Parameters := '/Create /TN ' + AddQuotes(TaskName) + ' /XML ' + AddQuotes(XmlFile);
    if ReplaceExisting then Parameters := Parameters + ' /F';
    if not ExecAndCaptureOutput(ExpandConstant('{sys}\schtasks.exe'), Parameters,
        '', SW_HIDE, ewWaitUntilTerminated, ExitCode, Output) then
      RaiseException(CustomMessage('TaskError'));
    if ExitCode <> 0 then begin
      Log('Task Scheduler import exit code: ' + IntToStr(ExitCode));
      for I := 0 to GetArrayLength(Output.StdErr) - 1 do Log(Output.StdErr[I]);
      RaiseException(CustomMessage('TaskError'));
    end;
  finally
    DeleteFile(XmlFile);
  end;
end;

procedure RegisterLoginTask;
var
  Scheduler, Existing, Definition, Trigger, Action, Registered: Variant;
  ExePath, WorkDir, Stage: String;
begin
  try
    Stage := 'connect';
    Scheduler := ConnectScheduler;
    Stage := 'find';
    Existing := FindLoginTask;
    if not VarIsNull(Existing) then begin
      RequireOwnedTask(Existing);
      PriorTaskXml := Existing.Xml;
    end;
    WorkDir := ExpandConstant('{app}');
    ExePath := AddBackslash(WorkDir) + 'TempAutoCleaner.exe';
    Stage := 'define';
    Definition := Scheduler.NewTask(0);
    Definition.RegistrationInfo.Description := TaskMarker;
    Definition.Principal.UserId := UserSid;
    Definition.Principal.LogonType := TaskLogonInteractiveToken;
    Definition.Principal.RunLevel := TaskRunLevelLeastPrivilege;
    Definition.Settings.Enabled := True;
    Definition.Settings.AllowDemandStart := False;
    Definition.Settings.MultipleInstances := TaskInstancesIgnoreNew;
    Definition.Settings.DisallowStartIfOnBatteries := False;
    Definition.Settings.StopIfGoingOnBatteries := False;
    Definition.Settings.StartWhenAvailable := False;
    Trigger := Definition.Triggers.Create(TaskTriggerLogon);
    Trigger.UserId := UserSid;
    Trigger.Delay := 'PT30S';
    Action := Definition.Actions.Create(TaskActionExec);
    Action.Path := ExePath;
    Action.WorkingDirectory := WorkDir;

    Stage := 'register';
    ImportTaskXml(Definition.XmlText, PriorTaskXml <> '');
    TaskChanged := True;
    Stage := 'verify';
    Registered := TaskFolder.GetTask(TaskName);
    RequireOwnedTask(Registered);
    Definition := Registered.Definition;
    Trigger := Definition.Triggers.Item(1);
    Action := Definition.Actions.Item(1);
    Stage := 'verify trigger';
    if (not Boolean(Registered.Enabled)) or (Trigger.Delay <> 'PT30S') or (not Boolean(Trigger.Enabled)) then
      RaiseException(CustomMessage('TaskError'));
    Stage := 'verify settings';
    if (Definition.Settings.MultipleInstances <> TaskInstancesIgnoreNew) or
       Boolean(Definition.Settings.AllowDemandStart) then
      RaiseException(CustomMessage('TaskError'));
    Stage := 'verify command path';
    if not SameText(Action.Path, ExePath) then
      RaiseException(CustomMessage('TaskError'));
    Stage := 'verify command arguments';
    if (not VarIsNull(Action.Arguments)) and (not VarIsEmpty(Action.Arguments)) and
       (String(Action.Arguments) <> '') then
      RaiseException(CustomMessage('TaskError'));
    Stage := 'verify working directory';
    if not SameText(RemoveBackslashUnlessRoot(Action.WorkingDirectory), RemoveBackslashUnlessRoot(WorkDir)) then
      RaiseException(CustomMessage('TaskError'));
#ifdef FailAfterRegistration
    RaiseException('Injected installer fixture failure.');
#endif
  except
    Log('Login task ' + Stage + ': ' + GetExceptionMessage);
    SuppressibleMsgBox(CustomMessage('TaskError'), mbError, MB_OK, IDOK);
    Abort;
  end;
end;

procedure RestoreLoginTask;
var
  Current: Variant;
begin
  if not TaskChanged then Exit;
  try
    Current := FindLoginTask;
    if VarIsNull(Current) then Exit;
    RequireOwnedTask(Current);
    if PriorTaskXml = '' then begin
      Current.Enabled := False;
      TaskFolder.DeleteTask(TaskName, 0);
    end else
      ImportTaskXml(PriorTaskXml, True);
  except
    Log('Could not restore the previous task state: ' + GetExceptionMessage);
    SuppressibleMsgBox(CustomMessage('TaskError'), mbError, MB_OK, IDOK);
  end;
end;

procedure RemoveLoginTask;
var
  Current: Variant;
  WasEnabled: Boolean;
begin
  try
    ConnectScheduler;
    Current := FindLoginTask;
    if VarIsNull(Current) then Exit;
    RequireOwnedTask(Current);
    WasEnabled := Current.Enabled;
    Current.Enabled := False;
    try
      TaskFolder.DeleteTask(TaskName, 0);
    except
      Current.Enabled := WasEnabled;
      RaiseLastException;
    end;
  except
    Log(GetExceptionMessage);
    RaiseException(CustomMessage('TaskError'));
  end;
end;
