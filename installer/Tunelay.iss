; Built by installer\Release.proj, which passes the defines below.
#ifndef AppVersion
  #error Run "dotnet msbuild installer/Release.proj" instead of compiling this file directly
#endif

#define AppName "Tunelay"
#define AppExe "Tunelay.exe"

[Setup]
; Never change the AppId: it is how updates and uninstall find the installed copy.
AppId={{6B2E7A51-3C94-4D0B-9E67-2F1A8C5D4E30}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=EGF09
AppPublisherURL=https://github.com/EGF09
AppSupportURL=https://github.com/EGF09
VersionInfoVersion={#FileVersion}
VersionInfoProductName={#AppName}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription={#AppName} Kurulumu

; Per user, so no administrator rights are asked for.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

; The app's own single-instance lock: setup and uninstall ask the user to exit it, and never kill it.
AppMutex=Local\Tunelay.SingleInstance
CloseApplications=no

; Spotify's terms: the agreement and the privacy text are shown before anything is installed.
DisableWelcomePage=no
LicenseFile={#Source}\SOZLESME.txt
InfoBeforeFile={#Source}\GIZLILIK.txt

WizardStyle=modern
WizardSmallImageFile={#Source}\Tunelay.png
SetupIconFile={#Source}\Tunelay.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} (Beta)

OutputDir={#Output}
OutputBaseFilename=Tunelay-Setup-{#AppVersion}-x64
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Messages]
tr.WelcomeLabel2=[name/ver] bilgisayarınıza kurulacak. Bu bir beta sürümüdür ve hata içerebilir.%n%nSpotify masaüstü uygulaması gerekir. Yönetici izni gerekmez.%n%nTunelay bağımsız bir uygulamadır, Spotify ile bağlantılı değildir.
tr.InfoBeforeLabel=Kurmadan önce gizlilik metnini okuyun.

[CustomMessages]
tr.DesktopShortcut=Masaüstüne kısayol oluştur
tr.Shortcuts=Kısayollar:
tr.LaunchApp=Tunelay'i başlat
tr.DeleteData=Tunelay ayarlarınız ve Spotify oturumunuz da silinsin mi?%n%nYeniden kuracaksanız Hayır'ı seçin. Evet'i seçerseniz geri alınamaz.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:Shortcuts}"; Flags: unchecked

[Files]
Source: "{#Source}\app\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Source}\SOZLESME.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Source}\GIZLILIK.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Source}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

{ "Start with Windows" is switched on inside the app, so only an entry for this copy is removed. }
procedure RemoveStartupEntry;
var
  Command: String;
begin
  if RegQueryStringValue(HKCU, RunKey, '{#AppName}', Command) then
    if CompareText(Command, '"' + ExpandConstant('{app}\{#AppExe}') + '" --background') = 0 then
      RegDeleteValue(HKCU, RunKey, '{#AppName}');
end;

{ Only the files the app writes, never a whole folder tree. }
procedure DeleteDataFolder(Folder: String);
begin
  DeleteFile(Folder + '\settings.json');
  DeleteFile(Folder + '\settings.json.tmp');
  DeleteFile(Folder + '\tokens.bin');
  RemoveDir(Folder);
end;

procedure DeleteUserData;
var
  Art: String;
begin
  DeleteDataFolder(ExpandConstant('{userappdata}\{#AppName}'));
  { The old name's folder too, or the next install would copy the session back. }
  DeleteDataFolder(ExpandConstant('{userappdata}\Spotiwidget'));
  Art := AddBackslash(GetTempDir) + 'Tunelay-art';
  DelTree(Art + '\*.img', False, True, False);
  RemoveDir(Art);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveStartupEntry;

  if CurUninstallStep = usPostUninstall then
    { A silent uninstall answers No. }
    if SuppressibleMsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      DeleteUserData;
end;
