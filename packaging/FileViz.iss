#ifndef PackageDir
  #error PackageDir is required
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
[Setup]
AppId={{ADC30985-9B15-49CB-AEC7-E9B8D3A02281}
AppName=FileViz
AppVersion={#AppVersion}
AppPublisher=FileViz contributors
AppPublisherURL=https://github.com/gduplessy/fileviz
DefaultDirName={localappdata}\Programs\FileViz
DefaultGroupName=FileViz
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=FileViz-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile={#PackageDir}\LICENSE
UninstallDisplayIcon={app}\FileViz.exe
[Tasks]
Name: desktopicon; Description: Create a desktop shortcut; Flags: unchecked
[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\FileViz"; Filename: "{app}\FileViz.exe"
Name: "{autodesktop}\FileViz"; Filename: "{app}\FileViz.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\FileViz.exe"; Description: Launch FileViz; Flags: nowait postinstall skipifsilent