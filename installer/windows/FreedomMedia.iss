; Inno Setup script for the FreedomMedia Windows installer.
; Build:  ISCC.exe /DAppVersion=1.0.3 /DSrcDir=<publish\win-x64> /DOutDir=<dist> FreedomMedia.iss
; The defaults below let it also be built from a checkout without passing anything.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SrcDir
  #define SrcDir "..\..\publish\win-x64"
#endif
#ifndef OutDir
  #define OutDir "..\..\dist"
#endif
#ifndef IconFile
  #define IconFile "..\..\src\FreedomMedia.App\Assets\freedommedia.ico"
#endif

[Setup]
AppId={{7C2B1E90-4E2A-4B7B-9C1E-FreedomMedia01}
AppName=FreedomMedia
AppVersion={#AppVersion}
AppPublisher=FreedomSoft
AppPublisherURL=https://freedomsoft.uk
AppSupportURL=https://github.com/shahidzafarsg/freedommedia
DefaultDirName={autopf}\FreedomMedia
DefaultGroupName=FreedomMedia
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\FreedomMedia.exe
OutputDir={#OutDir}
OutputBaseFilename=FreedomMedia-Setup-x64
SetupIconFile={#IconFile}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Per-user install by default, so no administrator prompt is required; the user may switch to
; an all-users install in the dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SrcDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\FreedomMedia"; Filename: "{app}\FreedomMedia.exe"
Name: "{group}\Uninstall FreedomMedia"; Filename: "{uninstallexe}"
Name: "{autodesktop}\FreedomMedia"; Filename: "{app}\FreedomMedia.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FreedomMedia.exe"; Description: "{cm:LaunchProgram,FreedomMedia}"; Flags: nowait postinstall skipifsilent
