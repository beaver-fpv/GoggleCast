#if VER < EncodeVer(6,7,0)
  #error Inno Setup 6.7 or newer is required.
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef AppSource
  #error Build with build-installer.ps1 so the application directory is explicit.
#endif
#ifndef RuntimeSource
  #error Build with build-installer.ps1 so the pinned runtime is prepared before compilation.
#endif

[Setup]
AppId={{BB215550-B850-485A-8E5A-11B37342BFD4}
AppName=观幕 GoggleCast
AppVersion={#AppVersion}
AppPublisher=GoggleCast contributors
AppPublisherURL=https://github.com/beaver-fpv/GoggleCast
AppSupportURL=https://github.com/beaver-fpv/GoggleCast/issues
AppUpdatesURL=https://github.com/beaver-fpv/GoggleCast/releases
DefaultDirName={localappdata}\Programs\GoggleCast
UsePreviousAppDir=yes
DefaultGroupName=GoggleCast
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#InstallerOutput}
OutputBaseFilename=GoggleCast-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=yes
DisableReadyPage=yes
DisableDirPage=no
DisableProgramGroupPage=yes
AllowNoIcons=no
DirExistsWarning=no
UninstallDisplayIcon={app}\GoggleCast.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage
ExtraDiskSpaceRequired=500000000

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl,ChineseSimplified.isl"

[CustomMessages]
english.InstallNow=&Install
english.InstallDetails=Install GoggleCast for your Windows account. FFmpeg is included; installation works offline. You can change the folder below.
english.FrameworkMissing=GoggleCast requires Microsoft .NET Framework 4.8 or later. Please install it from Microsoft, then run Setup again.%n%nhttps://dotnet.microsoft.com/download/dotnet-framework/net48
english.Launch=Open GoggleCast
chinesesimplified.InstallNow=立即安装(&I)
chinesesimplified.InstallDetails=为当前 Windows 用户安装观幕。安装包已包含 FFmpeg，无需联网。可在下方修改安装位置。
chinesesimplified.FrameworkMissing=观幕需要 Microsoft .NET Framework 4.8 或更新版本。请先从微软安装，再重新运行安装程序。%n%nhttps://dotnet.microsoft.com/download/dotnet-framework/net48
chinesesimplified.Launch=打开观幕 GoggleCast

[Files]
Source: "{#AppSource}\GoggleCast.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\MainWindow.xaml"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSource}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#RuntimeSource}\{#FFmpegExecutable}"; DestDir: "{app}\runtime"; DestName: "ffmpeg.exe"; Flags: ignoreversion
Source: "{#RuntimeSource}\{#FFmpegLicense}"; DestDir: "{app}\runtime"; DestName: "FFmpeg-LICENSE.txt"; Flags: ignoreversion
Source: "{#RuntimeSource}\{#FFmpegReadme}"; DestDir: "{app}\runtime"; DestName: "FFmpeg-README.txt"; Flags: ignoreversion
Source: "{#RuntimeMetadata}"; DestDir: "{app}\runtime"; DestName: "runtime-source.json"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\GoggleCast\观幕 GoggleCast"; Filename: "{app}\GoggleCast.exe"; WorkingDir: "{app}"
Name: "{autoprograms}\GoggleCast\卸载 GoggleCast"; Filename: "{uninstallexe}"
Name: "{autodesktop}\观幕 GoggleCast"; Filename: "{app}\GoggleCast.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\GoggleCast.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup: Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
    Result := Release >= 528040;
  if not Result then
    if RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
      Result := Release >= 528040;
  if not Result then SuppressibleMsgBox(CustomMessage('FrameworkMissing'), mbCriticalError, MB_OK, IDOK);
end;

procedure InitializeWizard;
var
  OldHeight: Integer;
  Delta: Integer;
begin
  OldHeight := WizardForm.SelectDirLabel.Height;
  WizardForm.SelectDirLabel.Caption := CustomMessage('InstallDetails');
  WizardForm.AdjustLabelHeight(WizardForm.SelectDirLabel);
  Delta := WizardForm.SelectDirLabel.Height - OldHeight;
  if Delta > 0 then
  begin
    WizardForm.DirEdit.Top := WizardForm.DirEdit.Top + Delta;
    WizardForm.DirBrowseButton.Top := WizardForm.DirBrowseButton.Top + Delta;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectDir then WizardForm.NextButton.Caption := CustomMessage('InstallNow');
end;

{ settings.xml and logs are created by the application, never installed or
  deleted by this script. Upgrades and uninstall preserve those user files. }
