; AutoDash - Inno Setup script
; In CI, pass /DPublishDir=path\to\publish to iscc

#define MyAppName "AutoDash"
#define MyAppVersion "1.0.0"
#ifndef PublishDir
  #define PublishDir "publish"
#endif

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=AutoDash-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\PhotoshopPipelineApp.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\PhotoshopPipelineApp.exe"

[Run]
Filename: "{app}\PhotoshopPipelineApp.exe"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
