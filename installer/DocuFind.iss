; DocuFind 安装脚本（Inno Setup 6）
;
; 打包步骤：
;   1) dotnet publish src\DocuFind.App\DocuFind.App.csproj -c Release -r win-x64 ^
;        --self-contained true -p:WindowsAppSDKSelfContained=true -o ..\Temp\docufind-publish
;   2) ISCC.exe /DSourceDir=<发布目录> /O<输出目录> installer\DocuFind.iss
;
; 说明：按用户安装（不需要管理员），装到 %LOCALAPPDATA%\Programs\DocuFind；
;      索引和设置仍在 %LOCALAPPDATA%\DocuFind，卸载时保留。

#define MyAppName "DocuFind"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "kevin-anycode"
#define MyAppUrl "https://gitee.com/kevin-anycode/DocuFind"
#define MyAppExeName "DocuFind.exe"

#ifndef SourceDir
  #define SourceDir "..\..\Temp\docufind-publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{7D1B6C2E-4E1A-4B7E-9C1D-3F2A8B6D5C41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
AppUpdatesURL={#MyAppUrl}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=DocuFind-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\assets\icon\app.ico
CloseApplications=yes
AppMutex=Local\DocuFind.SingleInstance
SetupLogging=yes

[Languages]
Name: "chs"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"
