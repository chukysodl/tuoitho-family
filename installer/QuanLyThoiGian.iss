#define AppName "Quản lý thời gian"
#define AppVersion "1.0.0"
#define AppPublisher "Local Family"
#ifndef ChromeExtensionId
  #define ChromeExtensionId ""
#endif
#ifndef EdgeExtensionId
  #define EdgeExtensionId ""
#endif

[Setup]
AppId={{7A70B42B-9F58-4A61-A56B-5F1D24E866A4}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\QuanLyThoiGian
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=QuanLyThoiGian_Setup
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
SetupLogging=yes
WizardStyle=modern

[Files]
Source: "..\artifacts\production\Service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\production\SessionAgent\*"; DestDir: "{app}\SessionAgent"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\production\Parent\*"; DestDir: "{app}\Parent"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\production\BrowserHost\*"; DestDir: "{app}\BrowserHost"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\production\AdminTool\*"; DestDir: "{app}\AdminTool"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\install-production.ps1"; DestDir: "{app}\Installer"; Flags: ignoreversion
Source: "..\scripts\uninstall-production.ps1"; DestDir: "{app}\Installer"; Flags: ignoreversion
Source: "..\scripts\production-check.ps1"; DestDir: "{app}\Installer"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\Parent\TuoiTho.Parent.exe"
Name: "{autoprograms}\{#AppName}\Kiểm tra bảo vệ"; Filename: "powershell.exe"; Parameters: "-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""{app}\Installer\production-check.ps1"" -InstallDir ""{app}"""; WorkingDir: "{app}"; IconFilename: "{app}\Parent\TuoiTho.Parent.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Parent\TuoiTho.Parent.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng Quản lý thời gian trên Desktop"; Flags: unchecked

[Run]
Filename: "powershell.exe"; Parameters: "-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""{app}\Installer\install-production.ps1"" -InstallDir ""{app}"" -ChromeExtensionId ""{#ChromeExtensionId}"" -EdgeExtensionId ""{#EdgeExtensionId}"""; StatusMsg: "Đang cấu hình dịch vụ bảo vệ (tối đa khoảng 30 giây)..."; Flags: runhidden waituntilterminated logoutput
Filename: "{app}\Parent\TuoiTho.Parent.exe"; Description: "Mở Quản lý thời gian"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""{app}\Installer\uninstall-production.ps1"" -InstallDir ""{app}"" -ChromeExtensionId ""{#ChromeExtensionId}"" -EdgeExtensionId ""{#EdgeExtensionId}"""; Flags: runhidden waituntilterminated; RunOnceId: "ProductionCleanup"

[Code]
function ParentAuthPath(): String;
begin
  Result := ExpandConstant('{commonappdata}\TuoiTho\parent-auth.json');
end;

function AdminToolPath(): String;
begin
  Result := ExpandConstant('{autopf}\QuanLyThoiGian\AdminTool\TuoiTho.AdminTool.exe');
end;

function VerifyExistingParentPassword(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if FileExists(AdminToolPath()) and FileExists(ParentAuthPath()) then
  begin
    if not Exec(AdminToolPath(), '--verify-password --authorize-maintenance', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
      Result := False
    else
      Result := ResultCode = 0;
    if not Result then
      MsgBox('Cần mật khẩu phụ huynh để repair hoặc nâng cấp Quản lý thời gian.', mbError, MB_OK);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := VerifyExistingParentPassword();
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
  Tool: String;
begin
  Tool := ExpandConstant('{app}\AdminTool\TuoiTho.AdminTool.exe');
  Result := FileExists(Tool) and Exec(Tool, '--authorize-uninstall', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  if not Result then
    MsgBox('Không thể gỡ bảo vệ nếu chưa xác nhận đúng mật khẩu phụ huynh.', mbError, MB_OK);
end;


procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Tool: String;
  CheckScript: String;
  CheckParams: String;
begin
  if CurStep = ssPostInstall then
  begin
    if not FileExists(ParentAuthPath()) then
    begin
      Tool := ExpandConstant('{app}\AdminTool\TuoiTho.AdminTool.exe');
      if not FileExists(Tool) then
        RaiseException('Thiếu thành phần thiết lập mật khẩu phụ huynh.');

      if not Exec(Tool, '--set-password', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
        RaiseException('Không thể mở phần thiết lập mật khẩu phụ huynh.');

      if ResultCode <> 0 then
        RaiseException('Cài đặt chưa hoàn tất vì chưa thiết lập mật khẩu phụ huynh.');
    end;

    CheckScript := ExpandConstant('{app}\Installer\production-check.ps1');
    CheckParams := '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "' + CheckScript +
      '" -InstallDir "' + ExpandConstant('{app}') + '"';
    if not Exec('powershell.exe', CheckParams, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('Không thể chạy kiểm tra bảo vệ sau cài đặt.');

    if ResultCode <> 0 then
      RaiseException('Kiểm tra bảo vệ sau cài đặt chưa đạt. Hãy chạy Repair hoặc xem mục Kiểm tra bảo vệ.');
  end;
end;
