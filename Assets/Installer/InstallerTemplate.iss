[Setup]
AppId=@@APP_ID@@
AppName=@@APP_NAME@@
AppVersion=@@APP_VERSION@@
@@PUBLISHER_DIRECTIVE@@
DefaultDirName={pf32}\Steam\steamapps\common\Grand Theft Auto Vice City
AppendDefaultDirName=no
DisableDirPage=no
DirExistsWarning=no
DisableProgramGroupPage=yes
DisableReadyMemo=no
DisableWelcomePage=no
OutputDir=output
OutputBaseFilename=setup
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
WizardStyle=modern dynamic
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
UsePreviousAppDir=yes
UsePreviousSetupType=no
AllowNoIcons=yes
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
UninstallFilesDir={app}\_BelarusianMod\@@PRODUCT_ID@@\uninstall
UninstallDisplayName=@@APP_NAME@@ @@APP_VERSION@@
SetupLogging=yes
SignedUninstaller=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "belarusian"; MessagesFile: "compiler:Default.isl,Belarusian.isl"

[Types]
Name: "full"; Description: "{cm:SetupTypeFull}"
Name: "compact"; Description: "{cm:SetupTypeCore}"
Name: "custom"; Description: "{cm:SetupTypeCustom}"; Flags: iscustom

[Components]
Name: "core"; Description: "{cm:ComponentCore}"; Types: full compact custom; Flags: fixed
Name: "silentpatch"; Description: "{cm:ComponentSilentPatch}"; Types: full

[CustomMessages]
english.SetupTypeFull=Belarusian language and SilentPatch
belarusian.SetupTypeFull=Беларуская мова і SilentPatch
english.SetupTypeCore=Belarusian language only
belarusian.SetupTypeCore=Толькі беларуская мова
english.SetupTypeCustom=Custom installation
belarusian.SetupTypeCustom=Выбарачнае ўсталяванне
english.ComponentCore=Belarusian language
belarusian.ComponentCore=Беларуская мова
english.ComponentSilentPatch=SilentPatch (optional)
belarusian.ComponentSilentPatch=SilentPatch (неабавязкова)
english.BackupFailed=Setup could not create a backup of: %1
belarusian.BackupFailed=Не ўдалося стварыць рэзервовую копію: %1
english.FileChangedPrompt=The file "%1" was changed after installation.%n%nYes — save the changed file to the conflicts folder and restore the previous state.%nNo — keep the changed file and save the previous version to the conflicts folder.
belarusian.FileChangedPrompt=Файл «%1» быў зменены пасля ўсталявання.%n%nТак — захаваць зменены файл у папцы канфліктаў і аднавіць папярэдні стан.%nНе — пакінуць зменены файл і захаваць папярэднюю версію ў папцы канфліктаў.
english.ConflictsSaved=Changed or previous files were saved here:%n%1
belarusian.ConflictsSaved=Змененыя або папярэднія файлы захаваныя тут:%n%1
english.InvalidState=The installer backup state is damaged. Setup cannot continue safely.
belarusian.InvalidState=Стан рэзервовых копій пашкоджаны. Бяспечна працягнуць усталяванне немагчыма.
english.LegacyState=An older installation of this localization was detected. Uninstall it before running this setup.
belarusian.LegacyState=Знойдзена старое ўсталяванне гэтай беларусізацыі. Выдаліце яго перад запускам гэтага інсталятара.
english.DifferentGameDirectory=This localization is already installed in another folder. Uninstall it there before selecting a new folder.
belarusian.DifferentGameDirectory=Гэтая беларусізацыя ўжо ўсталяваная ў іншай папцы. Выдаліце яе там, перш чым выбраць новую папку.

[Files]
@@FILE_ENTRIES@@
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}\_BelarusianMod\@@PRODUCT_ID@@\licenses"; Flags: ignoreversion

[UninstallDelete]
Type: filesandordirs; Name: "{app}\_BelarusianModBackup\@@PRODUCT_ID@@"
Type: dirifempty; Name: "{app}\_BelarusianModBackup"
Type: files; Name: "{app}\_BelarusianMod\@@PRODUCT_ID@@\state.ini"
Type: files; Name: "{app}\_BelarusianMod\@@PRODUCT_ID@@\state.index"
Type: files; Name: "{app}\_BelarusianMod\@@PRODUCT_ID@@\pending.index"
Type: files; Name: "{app}\_BelarusianMod\@@PRODUCT_ID@@\state.pending.ini"
Type: files; Name: "{app}\_BelarusianMod\@@PRODUCT_ID@@\index.pending"
Type: files; Name: "{app}\_BelarusianMod\@@PRODUCT_ID@@\state.previous.ini"
Type: files; Name: "{app}\_BelarusianMod\@@PRODUCT_ID@@\index.previous"
Type: files; Name: "{app}\_BelarusianMod\@@PRODUCT_ID@@\stale-cleanup.index"
Type: filesandordirs; Name: "{app}\_BelarusianMod\@@PRODUCT_ID@@\pending"
Type: dirifempty; Name: "{app}\_BelarusianMod\@@PRODUCT_ID@@\licenses"
Type: dirifempty; Name: "{app}\_BelarusianMod\@@PRODUCT_ID@@\uninstall"
Type: dirifempty; Name: "{app}\_BelarusianMod\@@PRODUCT_ID@@"
Type: dirifempty; Name: "{app}\_BelarusianMod"

[Code]
var
  InstallPrepared: Boolean;
  InstallCommitted: Boolean;
  HadConflicts: Boolean;
  HadPreviousState: Boolean;

function SupportRoot: String;
begin
  Result := ExpandConstant('{app}\_BelarusianMod\@@PRODUCT_ID@@');
end;

function LegacySupportRoot: String;
begin
  Result := ExpandConstant('{commonappdata}\GTA GXT Editor\Installations\@@PRODUCT_ID@@');
end;

function BackupRoot: String;
begin
  Result := ExpandConstant('{app}\_BelarusianModBackup\@@PRODUCT_ID@@');
end;

function StateFile: String;
begin
  Result := AddBackslash(SupportRoot) + 'state.ini';
end;

function IndexFile: String;
begin
  Result := AddBackslash(SupportRoot) + 'state.index';
end;

function PendingFile: String;
begin
  Result := AddBackslash(SupportRoot) + 'pending.index';
end;

function StaleCleanupFile: String;
begin
  Result := AddBackslash(SupportRoot) + 'stale-cleanup.index';
end;

function WorkingStateFile: String;
begin
  Result := AddBackslash(SupportRoot) + 'state.pending.ini';
end;

function WorkingIndexFile: String;
begin
  Result := AddBackslash(SupportRoot) + 'index.pending';
end;

function ActiveStateFile: String;
begin
  if IsUninstaller then
    Result := StateFile
  else
    Result := WorkingStateFile;
end;

function ActiveIndexFile: String;
begin
  if IsUninstaller then
    Result := IndexFile
  else
    Result := WorkingIndexFile;
end;

function SectionFor(const RelativePath: String): String;
begin
  Result := GetSHA256OfUnicodeString(Lowercase(RelativePath));
end;

function BackupFileFor(const RelativePath: String): String;
begin
  Result := AddBackslash(BackupRoot) + RelativePath;
end;

function PendingBackupFileFor(const RelativePath: String): String;
begin
  Result := AddBackslash(SupportRoot) + 'pending\' + SectionFor(RelativePath) + '.current';
end;

function DestinationFileFor(const RelativePath: String): String;
begin
  Result := AddBackslash(ExpandConstant('{app}')) + RelativePath;
end;

function IsSilentMode: Boolean;
begin
  if IsUninstaller then
    Result := UninstallSilent
  else
    Result := WizardSilent;
end;

function IsSilentPatchSelected: Boolean;
begin
  Result := WizardIsComponentSelected('silentpatch');
end;

function ArrayContains(const Values: TArrayOfString; const Value: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to GetArrayLength(Values) - 1 do
    if CompareText(Values[I], Value) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

procedure AddLineUnique(const FileName, Value: String);
var
  Values: TArrayOfString;
begin
  if not LoadStringsFromFile(FileName, Values) then
    SetArrayLength(Values, 0);
  if ArrayContains(Values, Value) then
    Exit;
  SetArrayLength(Values, GetArrayLength(Values) + 1);
  Values[GetArrayLength(Values) - 1] := Value;
  if not SaveStringsToUTF8FileWithoutBOM(FileName, Values, False) then
    RaiseException(FmtMessage(CustomMessage('BackupFailed'), [Value]));
end;

procedure AddToIndex(const RelativePath: String);
begin
  AddLineUnique(ActiveIndexFile, RelativePath);
end;

procedure AddToPending(const RelativePath: String);
var
  Values: TArrayOfString;
  Section, Destination, PendingBackup: String;
begin
  if not LoadStringsFromFile(PendingFile, Values) then
    SetArrayLength(Values, 0);
  if ArrayContains(Values, RelativePath) then
    Exit;
  Section := SectionFor(RelativePath);
  Destination := DestinationFileFor(RelativePath);
  PendingBackup := PendingBackupFileFor(RelativePath);
  if FileExists(Destination) then
  begin
    ForceDirectories(ExtractFileDir(PendingBackup));
    if not CopyFile(Destination, PendingBackup, False) then
      RaiseException(FmtMessage(CustomMessage('BackupFailed'), [RelativePath]));
    SetIniBool(Section, 'PreInstallExists', True, WorkingStateFile);
  end
  else
    SetIniBool(Section, 'PreInstallExists', False, WorkingStateFile);
  AddLineUnique(PendingFile, RelativePath);
end;

procedure BackupTarget(const RelativePath, ManagedType: String; PreserveOriginal: Boolean);
var
  Section, Destination, Backup: String;
begin
  ForceDirectories(SupportRoot);
  Section := SectionFor(RelativePath);
  Destination := DestinationFileFor(RelativePath);
  Backup := BackupFileFor(RelativePath);
  if not IniKeyExists(Section, 'Destination', ActiveStateFile) then
  begin
    SetIniString(Section, 'Destination', RelativePath, ActiveStateFile);
    SetIniString(Section, 'ManagedType', ManagedType, ActiveStateFile);
    if PreserveOriginal and FileExists(Destination) then
    begin
      ForceDirectories(ExtractFileDir(Backup));
      if not CopyFile(Destination, Backup, False) then
        RaiseException(FmtMessage(CustomMessage('BackupFailed'), [RelativePath]));
      SetIniBool(Section, 'OriginalExists', True, ActiveStateFile);
      SetIniString(Section, 'OriginalHash', GetSHA256OfFile(Destination), ActiveStateFile);
    end
    else
      SetIniBool(Section, 'OriginalExists', False, ActiveStateFile);
  end;
  SetIniBool(Section, 'Current', True, ActiveStateFile);
  AddToIndex(RelativePath);
  AddToPending(RelativePath);
end;

procedure BackupPayload(const RelativePath: String; PreserveOriginal: Boolean);
begin
  BackupTarget(RelativePath, 'payload', PreserveOriginal);
end;

procedure RecordPayload(const RelativePath, InstalledHash: String);
var
  Section: String;
begin
  Section := SectionFor(RelativePath);
  SetIniString(Section, 'InstalledHash', InstalledHash, ActiveStateFile);
  SetIniBool(Section, 'Current', True, ActiveStateFile);
end;

procedure ResetCurrentPayloadFlags;
var
  Values: TArrayOfString;
  I: Integer;
  Section: String;
begin
  if not LoadStringsFromFile(ActiveIndexFile, Values) then
    Exit;
  for I := 0 to GetArrayLength(Values) - 1 do
  begin
    Section := SectionFor(Values[I]);
    if CompareText(GetIniString(Section, 'ManagedType', '', ActiveStateFile), 'payload') = 0 then
      SetIniBool(Section, 'Current', False, ActiveStateFile);
  end;
end;

procedure RecoverCommittedFile(const FinalName, RecoveryName: String);
begin
  if (not FileExists(FinalName)) and FileExists(RecoveryName) then
    RenameFile(RecoveryName, FinalName);
end;

procedure RecoverInterruptedCommit;
begin
  RecoverCommittedFile(StateFile, AddBackslash(SupportRoot) + 'state.previous.ini');
  RecoverCommittedFile(IndexFile, AddBackslash(SupportRoot) + 'index.previous');
end;

procedure SavePreInstallState;
begin
  ForceDirectories(SupportRoot);
  RecoverInterruptedCommit;
  HadPreviousState := FileExists(StateFile);
  DeleteFile(WorkingStateFile);
  DeleteFile(WorkingIndexFile);
  DelTree(AddBackslash(SupportRoot) + 'pending', True, True, True);
  DeleteFile(StaleCleanupFile);
  if FileExists(StateFile) and not CopyFile(StateFile, WorkingStateFile, False) then
    RaiseException(CustomMessage('InvalidState'));
  if FileExists(IndexFile) and not CopyFile(IndexFile, WorkingIndexFile, False) then
    RaiseException(CustomMessage('InvalidState'));
  SetIniInt('Installer', 'SchemaVersion', 2, WorkingStateFile);
  SetIniString('Installer', 'AppDirectory', ExpandConstant('{app}'), WorkingStateFile);
  DeleteFile(PendingFile);
end;

function HasLegacyProgramDataState: Boolean;
begin
  Result := FileExists(AddBackslash(LegacySupportRoot) + 'state.ini') or
            FileExists(AddBackslash(LegacySupportRoot) + 'state.index') or
            DirExists(AddBackslash(LegacySupportRoot) + 'uninstall');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  RecoverInterruptedCommit;
  if HasLegacyProgramDataState then
  begin
    Result := CustomMessage('LegacyState');
    Exit;
  end;
  if FileExists(StateFile) <> FileExists(IndexFile) then
  begin
    Result := CustomMessage('InvalidState');
    Exit;
  end;
  if FileExists(StateFile) and
     (GetIniString('Installer', 'SchemaVersion', '', StateFile) <> '2') then
  begin
    Result := CustomMessage('LegacyState');
    Exit;
  end;
  if FileExists(StateFile) and
     (not PathSame(
       GetIniString('Installer', 'AppDirectory', '', StateFile),
       ExpandConstant('{app}'))) then
  begin
    Result := CustomMessage('DifferentGameDirectory');
    Exit;
  end;
  if (not FileExists(StateFile)) and DirExists(BackupRoot) then
  begin
    Result := CustomMessage('InvalidState');
    Exit;
  end;
  SavePreInstallState;
  ResetCurrentPayloadFlags;
  InstallPrepared := True;
end;

function ConflictDirectory: String;
begin
  Result := AddBackslash(SupportRoot) + 'conflicts\' +
            GetDateTimeString('yyyymmdd-hhnnss', '-', '-');
end;

function ArchiveFile(const SourceFile, RelativePath, Suffix: String): Boolean;
var
  Target: String;
begin
  Target := AddBackslash(ConflictDirectory) + RelativePath + Suffix;
  ForceDirectories(ExtractFileDir(Target));
  Result := CopyFile(SourceFile, Target, False);
  if Result then
    HadConflicts := True;
end;

procedure RestoreEntry(const RelativePath: String);
var
  Section, ManagedType, Destination, Backup, InstalledHash, CurrentHash: String;
  OriginalExists, Changed, RestorePrevious: Boolean;
begin
  if not IsUninstaller then
    AddToPending(RelativePath);
  Section := SectionFor(RelativePath);
  ManagedType := GetIniString(Section, 'ManagedType', '', ActiveStateFile);
  if ManagedType = '' then
    Exit;
  Destination := DestinationFileFor(RelativePath);
  Backup := BackupFileFor(RelativePath);
  OriginalExists := GetIniBool(Section, 'OriginalExists', False, ActiveStateFile);
  InstalledHash := GetIniString(Section, 'InstalledHash', '', ActiveStateFile);
  Changed := False;
  if FileExists(Destination) then
  begin
    CurrentHash := GetSHA256OfFile(Destination);
    if CompareText(ManagedType, 'duplicate') = 0 then
      Changed := True
    else if (InstalledHash <> '') and (CompareText(CurrentHash, InstalledHash) <> 0) then
      Changed := True;
  end;

  RestorePrevious := True;
  if Changed then
  begin
    if IsSilentMode then
      RestorePrevious := True
    else
      RestorePrevious := MsgBox(
        FmtMessage(CustomMessage('FileChangedPrompt'), [RelativePath]),
        mbConfirmation,
        MB_YESNO) = IDYES;

    if RestorePrevious then
    begin
      if not ArchiveFile(Destination, RelativePath, '.changed') then
        RaiseException(FmtMessage(CustomMessage('BackupFailed'), [RelativePath]));
    end
    else if OriginalExists and FileExists(Backup) then
      if not ArchiveFile(Backup, RelativePath, '.original') then
        RaiseException(FmtMessage(CustomMessage('BackupFailed'), [RelativePath]));
  end;

  if RestorePrevious then
  begin
    if FileExists(Destination) and not DeleteFile(Destination) then
      RaiseException(FmtMessage(CustomMessage('BackupFailed'), [RelativePath]));
    if OriginalExists and FileExists(Backup) then
    begin
      ForceDirectories(ExtractFileDir(Destination));
      if not CopyFile(Backup, Destination, False) then
        RaiseException(FmtMessage(CustomMessage('BackupFailed'), [RelativePath]));
    end;
    RemoveDir(ExtractFileDir(Destination));
  end;

  if IsUninstaller then
    DeleteFile(Backup)
  else
    AddLineUnique(StaleCleanupFile, RelativePath);
  DeleteIniSection(Section, ActiveStateFile);
end;

procedure ProcessStalePayloads;
var
  Values, Remaining: TArrayOfString;
  I, RemainingCount: Integer;
  Section, ManagedType: String;
begin
  if not LoadStringsFromFile(ActiveIndexFile, Values) then
    Exit;
  SetArrayLength(Remaining, GetArrayLength(Values));
  RemainingCount := 0;
  for I := 0 to GetArrayLength(Values) - 1 do
  begin
    Section := SectionFor(Values[I]);
    ManagedType := GetIniString(Section, 'ManagedType', '', ActiveStateFile);
    if (CompareText(ManagedType, 'payload') = 0) and
       (not GetIniBool(Section, 'Current', False, ActiveStateFile)) then
      RestoreEntry(Values[I])
    else
    begin
      Remaining[RemainingCount] := Values[I];
      RemainingCount := RemainingCount + 1;
    end;
  end;
  SetArrayLength(Remaining, RemainingCount);
  SaveStringsToUTF8FileWithoutBOM(ActiveIndexFile, Remaining, False);
end;

procedure CommitFile(const WorkingName, FinalName, RecoveryName: String);
begin
  DeleteFile(RecoveryName);
  if FileExists(FinalName) and not RenameFile(FinalName, RecoveryName) then
    RaiseException(CustomMessage('InvalidState'));
  if not RenameFile(WorkingName, FinalName) then
  begin
    RecoverCommittedFile(FinalName, RecoveryName);
    RaiseException(CustomMessage('InvalidState'));
  end;
  DeleteFile(RecoveryName);
end;

procedure CommitInstallState;
var
  StaleValues, PendingValues: TArrayOfString;
  I: Integer;
begin
  ProcessStalePayloads;
  if LoadStringsFromFile(PendingFile, PendingValues) then
    for I := 0 to GetArrayLength(PendingValues) - 1 do
      DeleteIniEntry(
        SectionFor(PendingValues[I]),
        'PreInstallExists',
        WorkingStateFile);
  CommitFile(
    WorkingStateFile,
    StateFile,
    AddBackslash(SupportRoot) + 'state.previous.ini');
  CommitFile(
    WorkingIndexFile,
    IndexFile,
    AddBackslash(SupportRoot) + 'index.previous');
  InstallCommitted := True;
  if LoadStringsFromFile(StaleCleanupFile, StaleValues) then
    for I := 0 to GetArrayLength(StaleValues) - 1 do
      DeleteFile(BackupFileFor(StaleValues[I]));
  DeleteFile(StaleCleanupFile);
  DelTree(AddBackslash(SupportRoot) + 'pending', True, True, True);
  DeleteFile(PendingFile);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    CommitInstallState;
end;

procedure RollbackScriptState;
var
  Pending: TArrayOfString;
  I: Integer;
  RelativePath, Section, Destination, PendingBackup: String;
  PreInstallExists: Boolean;
begin
  if LoadStringsFromFile(PendingFile, Pending) then
    for I := 0 to GetArrayLength(Pending) - 1 do
    begin
      RelativePath := Pending[I];
      Section := SectionFor(RelativePath);
      Destination := DestinationFileFor(RelativePath);
      PendingBackup := PendingBackupFileFor(RelativePath);
      PreInstallExists := GetIniBool(
        Section,
        'PreInstallExists',
        False,
        WorkingStateFile);
      if FileExists(Destination) then
        DeleteFile(Destination);
      if PreInstallExists and FileExists(PendingBackup) then
      begin
        ForceDirectories(ExtractFileDir(Destination));
        CopyFile(PendingBackup, Destination, False);
      end;
      if not IniKeyExists(Section, 'Destination', StateFile) then
        DeleteFile(BackupFileFor(RelativePath));
    end;
  DeleteFile(WorkingStateFile);
  DeleteFile(WorkingIndexFile);
  DelTree(AddBackslash(SupportRoot) + 'pending', True, True, True);
  DeleteFile(StaleCleanupFile);
  DeleteFile(PendingFile);
  if not HadPreviousState then
  begin
    DelTree(BackupRoot, True, True, True);
    RemoveDir(ExpandConstant('{app}\_BelarusianModBackup'));
    RemoveDir(AddBackslash(SupportRoot) + 'licenses');
    RemoveDir(SupportRoot);
    RemoveDir(ExpandConstant('{app}\_BelarusianMod'));
  end;
end;

procedure DeinitializeSetup;
begin
  if InstallPrepared and not InstallCommitted then
    RollbackScriptState;
end;

function InitializeUninstall: Boolean;
begin
  RecoverInterruptedCommit;
  Result := FileExists(StateFile) and FileExists(IndexFile) and
            (GetIniString('Installer', 'SchemaVersion', '', StateFile) = '2');
  if not Result then
    MsgBox(CustomMessage('InvalidState'), mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Values: TArrayOfString;
  I: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    if LoadStringsFromFile(IndexFile, Values) then
      for I := GetArrayLength(Values) - 1 downto 0 do
        RestoreEntry(Values[I]);
    DeleteFile(IndexFile);
    DeleteFile(StateFile);
  end
  else if (CurUninstallStep = usDone) and HadConflicts and (not UninstallSilent) then
    MsgBox(
      FmtMessage(CustomMessage('ConflictsSaved'), [AddBackslash(SupportRoot) + 'conflicts']),
      mbInformation,
      MB_OK);
end;
