#ifndef AppVersion
  #define AppVersion "0.1.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

[Setup]
AppId={{AC9C76A2-C4F4-4AB2-A2A7-7D03F89F1DE2}
AppName=Limit Lens
AppVersion={#AppVersion}
AppPublisher=Limit Lens contributors
DefaultDirName={localappdata}\Programs\LimitLens
DefaultGroupName=Limit Lens
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=LimitLens-Setup-{#AppVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\LimitLens.App\Assets\LimitLens.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\LimitLens.exe
CloseApplications=yes
CloseApplicationsFilter=LimitLens.exe
SetupLogging=yes
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Limit Lens
VersionInfoDescription=Limit Lens per-user installer
VersionInfoCompany=Limit Lens contributors

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Limit Lens"; Filename: "{app}\LimitLens.exe"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "LimitLens"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\LimitLens.exe"; Description: "Launch Limit Lens"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM LimitLens.exe /F"; Flags: runhidden; RunOnceId: "StopLimitLens"
