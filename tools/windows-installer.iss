[Setup]
AppId={{B7F49A3E-8E21-4F3B-9C4A-7D65B2D1E094}}
AppName=P2PMessenger
AppVersion={#AppVersion}
AppPublisher=Mikita Besau
AppPublisherURL=https://github.com/KaunazDagaz/P2PMessenger
AppSupportURL=https://github.com/KaunazDagaz/P2PMessenger
DefaultDirName={autopf}\P2PMessenger
DefaultGroupName=P2PMessenger
AllowNoIcons=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#IconPath}
UninstallDisplayIcon={app}\P2PMessenger.Desktop.exe
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "vcredist"; Description: "Install Microsoft Visual C++ Redistributable (x64) (required by P2PMessenger)"; Check: NeedsVcRedist

[Files]
Source: "{#SelfContainedDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#VcRedistPath}"; DestDir: "{tmp}"; DestName: "vc_redist.x64.exe"; Flags: dontcopy

[Icons]
Name: "{group}\P2PMessenger"; Filename: "{app}\P2PMessenger.Desktop.exe"
Name: "{group}\{cm:UninstallProgram,P2PMessenger}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\P2PMessenger"; Filename: "{app}\P2PMessenger.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/norestart"; StatusMsg: "Installing Microsoft Visual C++ Redistributable (x64)..."; Tasks: vcredist; Check: NeedsVcRedist; BeforeInstall: ExtractVcRedist; Flags: waituntilterminated
Filename: "{app}\P2PMessenger.Desktop.exe"; Description: "{cm:LaunchProgram,P2PMessenger}"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsVcRedist: Boolean;
var
  Installed: Cardinal;
begin
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
    Result := Installed <> 1
  else
    Result := True;
end;

procedure ExtractVcRedist;
begin
  ExtractTemporaryFile('vc_redist.x64.exe');
end;
