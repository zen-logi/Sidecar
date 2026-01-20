; Sidecar Client Installer Script for Inno Setup
; https://jrsoftware.org/isinfo.php

#define MyAppName "Sidecar Client"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Sidecar Project"
#define MyAppURL "https://github.com/zen-logi/Sidecar"
#define MyAppExeName "Sidecar.Client.exe"

[Setup]
AppId={{9F8A3B2C-7D4E-5F6A-8B9C-0D1E2F3A4B5C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=LICENSE
OutputDir=installer
OutputBaseFilename=Sidecar.Client-Setup
SetupIconFile=src\Sidecar.Host\Resources\appicon.ico
UninstallDisplayIcon={app}\Sidecar.Client.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Client application only
Source: "publish\client\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Sidecar Client"; Filename: "{app}\Sidecar.Client.exe"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Sidecar Client"; Filename: "{app}\Sidecar.Client.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Sidecar.Client.exe"; Description: "{cm:LaunchProgram,Sidecar Client}"; Flags: nowait postinstall skipifsilent
