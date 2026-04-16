; BIBIM Copilot Installer Script (Multi-Revit Version Support)
; Inno Setup 6.x
; Supports Revit 2022-2027

#define MyAppName "BIBIM AI"
; Version is passed from build_installer.ps1 via /D flag
; Fallback to 0.0.0 if not provided (for manual builds)
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef AppLanguage
  #define AppLanguage "kr"
#endif
#define MyAppPublisher "SquareZero Inc."
#define MyAppURL "https://github.com/SquareZero-Inc/bibim-dynamo"
#define SourceDir "."

#if AppLanguage == "en"
  #define InstallerLanguageName "english"
  #define InstallerLanguageFile "compiler:Default.isl"
  #define TextNoDynamoSuffix "(no Dynamo detected)"
  #define TextRunGuidePageTitle "How to Run BIBIM AI"
  #define TextRunGuidePageDesc "Installation is complete. Follow the guide below to launch BIBIM AI."
  #define TextRunGuideTitle "Installation complete! Start BIBIM AI"
  #define TextRunGuideStep1 "1. Launch Revit"
  #define TextRunGuideStep2 "2. Open Dynamo (Manage tab > Dynamo)"
  #define TextRunGuideStep3 "3. In the top menu, click Extensions > Open BIBIM Chat"
  #define TextRunGuideTipLine1 "Tip: BIBIM AI runs as an extension inside Dynamo."
  #define TextRunGuideTipLine2 "Use it only when both Revit and Dynamo are running."
  #define TextRunGuideApiKeyLine "Enter your Anthropic API key in Settings (gear icon) to get started. Get one at: console.anthropic.com/account/keys"
  #define TextVersionPageTitle "Version Selection"
  #define TextVersionPageDesc "Select the Revit and Dynamo versions for BIBIM Copilot installation."
  #define TextDetectedPairsHeader "Detected Revit-Dynamo versions:"
  #define TextDetectedVersionHeader "Detected versions:"
  #define TextWarningLine1 "Important: The auto-detected version may differ from your actual installed version."
  #define TextWarningLine2 "For accurate installation, check your actual Revit and Dynamo versions and select them below."
  #define TextRevitLabel "Select Revit version:"
  #define TextDynamoLabel "Select Dynamo version:"
#else
  #define InstallerLanguageName "korean"
  #define InstallerLanguageFile "compiler:Languages\Korean.isl"
  #define TextNoDynamoSuffix "(Dynamo 없음)"
  #define TextRunGuidePageTitle "BIBIM AI 실행 방법"
  #define TextRunGuidePageDesc "설치가 완료되었습니다! 아래 안내에 따라 BIBIM AI를 실행하세요."
  #define TextRunGuideTitle "설치 완료! BIBIM AI를 시작해보세요"
  #define TextRunGuideStep1 "1. Revit을 실행합니다"
  #define TextRunGuideStep2 "2. Dynamo를 엽니다 (관리 탭 > Dynamo)"
  #define TextRunGuideStep3 "3. 상단 메뉴에서 확장(Extensions) > Open BIBIM Chat 클릭"
  #define TextRunGuideTipLine1 "Tip: BIBIM AI는 Dynamo 내에서 실행되는 확장 프로그램입니다."
  #define TextRunGuideTipLine2 "Revit과 Dynamo가 모두 실행된 상태에서만 사용할 수 있습니다."
  #define TextRunGuideApiKeyLine "설정(⚙)에서 Anthropic API 키를 입력하면 바로 사용할 수 있습니다. 발급: console.anthropic.com/account/keys"
  #define TextVersionPageTitle "버전 선택"
  #define TextVersionPageDesc "BIBIM Copilot을 설치할 Revit과 Dynamo 버전을 선택하세요."
  #define TextDetectedPairsHeader "감지된 Revit-Dynamo 버전:"
  #define TextDetectedVersionHeader "감지된 버전:"
  #define TextWarningLine1 "중요: 자동 감지된 버전이 실제 설치된 버전과 다를 수 있습니다."
  #define TextWarningLine2 "정확한 설치를 위해 Revit과 Dynamo의 실제 버전을 직접 확인한 후 아래에서 선택해주세요."
  #define TextRevitLabel "Revit 버전 선택:"
  #define TextDynamoLabel "Dynamo 버전 선택:"
#endif

[Setup]
; AppId is set dynamically in code based on selected Revit/Dynamo version
AppId={{B1B1M-C0P1-L0T1-2025-REVIT000001}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
; Default path (will be overridden by code)
DefaultDirName={userappdata}\Dynamo\Dynamo Revit\3.3\packages\BIBIM_MVP
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#SourceDir}\Output
OutputBaseFilename=BIBIM_Setup_{#AppLanguage}_v{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LicenseFile=LICENSE
SetupIconFile=Assets\Icons\bibim-icon-blue.ico
UninstallDisplayIcon={app}\bibim-icon-blue.ico
; Use code functions to set dynamic AppId and display name
UsePreviousAppDir=no

[Languages]
Name: "{#InstallerLanguageName}"; MessagesFile: "{#InstallerLanguageFile}"

[Messages]
#if AppLanguage == "en"
english.WelcomeLabel2=This will install BIBIM Copilot on your computer.%n%nPlease close Revit and Dynamo before continuing.
#else
korean.WelcomeLabel2=BIBIM Copilot을 설치합니다.%n%nRevit과 Dynamo가 실행 중이라면 먼저 종료해주세요.
#endif

[Dirs]
Name: "{app}\dyf"
Name: "{app}\extra"
Name: "{app}\bin"

[UninstallDelete]
Type: files; Name: "{app}\rag_config.json"
Type: files; Name: "{app}\extra\BIBIM_ViewExtensionDefinition.xml"
Type: filesandordirs; Name: "{app}\bin"
Type: filesandordirs; Name: "{app}\extra"
Type: filesandordirs; Name: "{app}\dyf"
Type: filesandordirs; Name: "{app}"

[Files]
; Root folder - pkg.json (Common)
Source: "{#SourceDir}\pkg.json"; DestDir: "{app}"; Flags: ignoreversion

; ViewExtension XML (Common)
Source: "{#SourceDir}\BIBIM_ViewExtensionDefinition.xml"; DestDir: "{app}\extra"; Flags: ignoreversion

; Run Guide Image (for installer display)
Source: "{#SourceDir}\Assets\Icons\bibim_run_guide.bmp"; DestDir: "{tmp}"; Flags: dontcopy

; ----------------------------------------------------------------------
; Revit 2026 Builds (4 variants)
; ----------------------------------------------------------------------
Source: "{#SourceDir}\bin\{#AppLanguage}\R2027_D402\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2027_D402
Source: "{#SourceDir}\bin\{#AppLanguage}\R2026_D361\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2026_D361

Source: "{#SourceDir}\bin\{#AppLanguage}\R2026_D360\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2026_D360

Source: "{#SourceDir}\bin\{#AppLanguage}\R2026_D350\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2026_D350

Source: "{#SourceDir}\bin\{#AppLanguage}\R2026_D341\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2026_D341

; ----------------------------------------------------------------------
; Revit 2025 Builds (3 variants)
; ----------------------------------------------------------------------
Source: "{#SourceDir}\bin\{#AppLanguage}\R2025_D330\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2025_D330

Source: "{#SourceDir}\bin\{#AppLanguage}\R2025_D321\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2025_D321

Source: "{#SourceDir}\bin\{#AppLanguage}\R2025_D303\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2025_D303

; ----------------------------------------------------------------------
; Revit 2024 Builds (3 variants)
; ----------------------------------------------------------------------
Source: "{#SourceDir}\bin\{#AppLanguage}\R2024_D293\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2024_D293

Source: "{#SourceDir}\bin\{#AppLanguage}\R2024_D281\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2024_D281

Source: "{#SourceDir}\bin\{#AppLanguage}\R2024_D270\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2024_D270

; ----------------------------------------------------------------------
; Revit 2023 Builds (2 variants)
; ----------------------------------------------------------------------
Source: "{#SourceDir}\bin\{#AppLanguage}\R2023_D261\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2023_D261

Source: "{#SourceDir}\bin\{#AppLanguage}\R2023_D230\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2023_D230

; ----------------------------------------------------------------------
; Revit 2022 Builds (2 variants)
; ----------------------------------------------------------------------
Source: "{#SourceDir}\bin\{#AppLanguage}\R2022_D220\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsR2022_D220


[Code]
var
  VersionSelectionPage: TWizardPage;
  RunGuidePage: TWizardPage;
  RunGuideImage: TBitmapImage;
  RevitVersionCombo: TNewComboBox;
  DynamoVersionCombo: TNewComboBox;
  DetectedInfoLabel: TNewStaticText;
  
  SelectedRevitVersion: String;
  SelectedDynamoVersion: String;
  DetectedRevitVersion: String;
  DetectedDynamoVersion: String;
  FinalInstallPath: String;
  RagStoreName: String;

// RAG Store names
const
  RAG_STORE_2022 = 'fileSearchStores/bibimragrevit2022-homl9wrbleij';
  RAG_STORE_2023 = 'fileSearchStores/bibimragrevit2023-3b3hydditjyx';
  RAG_STORE_2024 = 'fileSearchStores/bibimragrevit2024-yvbw4ko15ai4';
  RAG_STORE_2025 = 'fileSearchStores/bibimragrevit2025-91bikbwcazgl';
  RAG_STORE_2025_3 = 'fileSearchStores/bibimragrevit20253-hd3ya0ixm652';
  RAG_STORE_2026 = 'fileSearchStores/bibimragrevit2026-zjhf7qg5wyhd';
  // R2027: placeholder — update this ID after creating the Dynamo 4.x corpus in Google AI Studio
  RAG_STORE_2027 = 'fileSearchStores/bibimragrevit2027-PLACEHOLDER';

// Check functions for [Files] section
function IsR2027_D402(): Boolean;
begin
  Result := (Pos('2027', SelectedRevitVersion) > 0) and (SelectedDynamoVersion = '27.0');
end;

function IsR2026_D361(): Boolean;
begin
  Result := (Pos('2026', SelectedRevitVersion) > 0) and (SelectedDynamoVersion = '3.6.1');
end;

function IsR2026_D360(): Boolean;
begin
  Result := (Pos('2026', SelectedRevitVersion) > 0) and ((SelectedDynamoVersion = '3.6') or (SelectedDynamoVersion = '3.6.0'));
end;

function IsR2026_D350(): Boolean;
begin
  Result := (Pos('2026', SelectedRevitVersion) > 0) and ((SelectedDynamoVersion = '3.5') or (SelectedDynamoVersion = '3.5.0'));
end;

function IsR2026_D341(): Boolean;
begin
  Result := (Pos('2026', SelectedRevitVersion) > 0) and ((SelectedDynamoVersion = '3.4') or (SelectedDynamoVersion = '3.4.1'));
end;

function IsR2025_D330(): Boolean;
begin
  Result := (Pos('2025', SelectedRevitVersion) > 0) and ((SelectedDynamoVersion = '3.3') or (SelectedDynamoVersion = '3.3.0'));
end;

function IsR2025_D321(): Boolean;
begin
  Result := (Pos('2025', SelectedRevitVersion) > 0) and ((SelectedDynamoVersion = '3.2') or (SelectedDynamoVersion = '3.2.1'));
end;

function IsR2025_D303(): Boolean;
begin
  Result := (Pos('2025', SelectedRevitVersion) > 0) and ((SelectedDynamoVersion = '3.0') or (SelectedDynamoVersion = '3.0.3'));
end;

function IsR2024_D293(): Boolean;
begin
  Result := (Pos('2024', SelectedRevitVersion) > 0) and ((SelectedDynamoVersion = '2.19') or (SelectedDynamoVersion = '2.19.3'));
end;

function IsR2024_D281(): Boolean;
begin
  Result := (Pos('2024', SelectedRevitVersion) > 0) and ((SelectedDynamoVersion = '2.18') or (SelectedDynamoVersion = '2.18.1'));
end;

function IsR2024_D270(): Boolean;
begin
  Result := (Pos('2024', SelectedRevitVersion) > 0) and ((SelectedDynamoVersion = '2.17') or (SelectedDynamoVersion = '2.17.0'));
end;

function IsR2023_D261(): Boolean;
begin
  Result := (Pos('2023', SelectedRevitVersion) > 0) and ((SelectedDynamoVersion = '2.16') or (SelectedDynamoVersion = '2.16.1'));
end;

function IsR2023_D230(): Boolean;
begin
  Result := (Pos('2023', SelectedRevitVersion) > 0) and ((SelectedDynamoVersion = '2.13') or (SelectedDynamoVersion = '2.13.0'));
end;

function IsR2022_D220(): Boolean;
begin
  Result := (Pos('2022', SelectedRevitVersion) > 0) and ((SelectedDynamoVersion = '2.12') or (SelectedDynamoVersion = '2.12.0'));
end;

function GetDynamoBasePath(): String;
begin
  Result := ExpandConstant('{userappdata}') + '\Dynamo\Dynamo Revit';
end;

// Helper to check if directory is effectively empty
function IsDirEmpty(Path: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := True;
  if FindFirst(Path + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Result := False;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// Detect ALL installed Revit versions
function DetectInstalledRevit(): String;
var
  Versions: array of String;
  i: Integer;
  RegKey: String;
  InstallPath: String;
  Found: Boolean;
begin
  Result := '';
  SetArrayLength(Versions, 6);
  Versions[0] := '2027';
  Versions[1] := '2026';
  Versions[2] := '2025';
  Versions[3] := '2024';
  Versions[4] := '2023';
  Versions[5] := '2022';
  
  for i := 0 to GetArrayLength(Versions) - 1 do
  begin
    Found := False;
    RegKey := 'SOFTWARE\Autodesk\Revit\Autodesk Revit ' + Versions[i];
    if RegQueryStringValue(HKLM, RegKey, 'InstallationLocation', InstallPath) and DirExists(InstallPath) then Found := True;
    
    if not Found then
    begin
       RegKey := 'SOFTWARE\Autodesk\Revit ' + Versions[i];
       if RegQueryStringValue(HKLM, RegKey, 'InstallPath', InstallPath) and DirExists(InstallPath) then Found := True;
    end;

    if not Found then
      if DirExists(ExpandConstant('{pf64}') + '\Autodesk\Revit ' + Versions[i]) then Found := True;

    if Found then
    begin
      if Result = '' then Result := Versions[i] else Result := Result + ', ' + Versions[i];
    end;
  end;
end;

function DetectInstalledDynamo(): String;
var
  BasePath: String;
  Versions: array of String;
  i: Integer;
begin
  Result := '';
  BasePath := GetDynamoBasePath();
  SetArrayLength(Versions, 13);
  Versions[0] := '27.0';
  Versions[1] := '3.6';
  Versions[2] := '3.5';
  Versions[3] := '3.4';
  Versions[4] := '3.3';
  Versions[5] := '3.2';
  Versions[6] := '3.0';
  Versions[7] := '2.19';
  Versions[8] := '2.18';
  Versions[9] := '2.17';
  Versions[10] := '2.16';
  Versions[11] := '2.13';
  Versions[12] := '2.12';
  
  for i := 0 to GetArrayLength(Versions) - 1 do
  begin
    // Check if both version folder exists AND packages subfolder has content
    if DirExists(BasePath + '\' + Versions[i]) and 
       DirExists(BasePath + '\' + Versions[i] + '\packages') and 
       (not IsDirEmpty(BasePath + '\' + Versions[i] + '\packages')) then
    begin
      if Result = '' then
        Result := Versions[i]
      else
        Result := Result + ', ' + Versions[i];
    end;
  end;
end;

// Helper: Find first available Dynamo version from comma-separated list
function FindDynamoForRevit(VerList: String; BasePath: String): String;
var
  Versions: array of String;
  i, Count, CommaPos: Integer;
  TempList: String;
begin
  Result := '';
  TempList := VerList;
  Count := 0;
  
  // Count commas
  for i := 1 to Length(VerList) do
    if VerList[i] = ',' then Count := Count + 1;
  
  SetArrayLength(Versions, Count + 1);
  
  // Parse comma-separated versions
  i := 0;
  while (Length(TempList) > 0) and (i <= Count) do
  begin
    CommaPos := Pos(',', TempList);
    if CommaPos > 0 then
    begin
      Versions[i] := Trim(Copy(TempList, 1, CommaPos - 1));
      TempList := Copy(TempList, CommaPos + 1, Length(TempList));
    end
    else
    begin
      Versions[i] := Trim(TempList);
      TempList := '';
    end;
    i := i + 1;
  end;
  
  // Find first valid Dynamo version (must exist and not be empty)
  for i := 0 to GetArrayLength(Versions) - 1 do
  begin
    if DirExists(BasePath + '\' + Versions[i]) and (not IsDirEmpty(BasePath + '\' + Versions[i])) then
    begin
      Result := Versions[i];
      Exit;
    end;
  end;
end;

// Detect Revit-Dynamo pairs (e.g., "Revit 2025 → Dynamo 3.3")
function DetectRevitDynamoPairs(): String;
var
  RevitVersions: array of String;
  DynamoVersions: array of String;
  i: Integer;
  RegKey, InstallPath, BasePath: String;
  Found: Boolean;
  DynamoVer: String;
begin
  Result := '';
  BasePath := GetDynamoBasePath();
  
  // Revit versions and their typical Dynamo versions
  SetArrayLength(RevitVersions, 6);
  SetArrayLength(DynamoVersions, 6);
  RevitVersions[0] := '2027'; DynamoVersions[0] := '27.0';
  RevitVersions[1] := '2026'; DynamoVersions[1] := '3.6,3.5,3.4';
  RevitVersions[2] := '2025'; DynamoVersions[2] := '3.3,3.2,3.0';
  RevitVersions[3] := '2024'; DynamoVersions[3] := '2.19,2.18,2.17';
  RevitVersions[4] := '2023'; DynamoVersions[4] := '2.16,2.13';
  RevitVersions[5] := '2022'; DynamoVersions[5] := '2.12,2.10';
  
  for i := 0 to GetArrayLength(RevitVersions) - 1 do
  begin
    Found := False;
    // Check if Revit is installed
    RegKey := 'SOFTWARE\Autodesk\Revit\Autodesk Revit ' + RevitVersions[i];
    if RegQueryStringValue(HKLM, RegKey, 'InstallationLocation', InstallPath) and DirExists(InstallPath) then 
      Found := True;
    
    if not Found then
    begin
      RegKey := 'SOFTWARE\Autodesk\Revit ' + RevitVersions[i];
      if RegQueryStringValue(HKLM, RegKey, 'InstallPath', InstallPath) and DirExists(InstallPath) then 
        Found := True;
    end;

    if not Found then
      if DirExists(ExpandConstant('{pf64}') + '\Autodesk\Revit ' + RevitVersions[i]) then 
        Found := True;

    if Found then
    begin
      // Find matching Dynamo version using the helper function
      DynamoVer := FindDynamoForRevit(DynamoVersions[i], BasePath);
      
      if Result <> '' then
        Result := Result + #13#10;
        
      if DynamoVer <> '' then
        Result := Result + '    • Revit ' + RevitVersions[i] + ' → Dynamo ' + DynamoVer
      else
        Result := Result + '    • Revit ' + RevitVersions[i] + ' {#TextNoDynamoSuffix}';
    end;
  end;
end;

function GetRagStoreForRevit(RevitVer: String): String;
begin
  if Pos('2022', RevitVer) > 0 then Result := RAG_STORE_2022
  else if Pos('2023', RevitVer) > 0 then Result := RAG_STORE_2023
  else if Pos('2024', RevitVer) > 0 then Result := RAG_STORE_2024
  else if (RevitVer = '2025.3') or (RevitVer = '2025.4') then Result := RAG_STORE_2025_3
  else if Pos('2025', RevitVer) > 0 then Result := RAG_STORE_2025
  else if Pos('2026', RevitVer) > 0 then Result := RAG_STORE_2026
  else if Pos('2027', RevitVer) > 0 then Result := RAG_STORE_2027
  else Result := RAG_STORE_2026;
end;

// Returns the fallback RAG store if the primary is unavailable (e.g. 2027 → 2026)
function GetFallbackRagStoreForRevit(RevitVer: String): String;
begin
  if Pos('2027', RevitVer) > 0 then Result := RAG_STORE_2026
  else Result := '';
end;

function GetRevitYear(RevitVer: String): String;
begin
  if Length(RevitVer) >= 4 then Result := Copy(RevitVer, 1, 4) else Result := RevitVer;
end;

function CalculateInstallPath(): String;
begin
  Result := GetDynamoBasePath() + '\' + SelectedDynamoVersion + '\packages\BIBIM_MVP';
end;

// Extract canonical version from a grouped display label.
// e.g. '2025 / 2025.1 / 2025.2' -> '2025',  '2025.3 / 2025.4' -> '2025.3'
function ParseRevitVersion(DisplayText: String): String;
var
  SlashPos: Integer;
begin
  SlashPos := Pos(' / ', DisplayText);
  if SlashPos > 0 then
    Result := Trim(Copy(DisplayText, 1, SlashPos - 1))
  else
    Result := DisplayText;
end;

procedure RevitVersionChanged(Sender: TObject);
begin
  SelectedRevitVersion := ParseRevitVersion(RevitVersionCombo.Items[RevitVersionCombo.ItemIndex]);
end;

procedure DynamoVersionChanged(Sender: TObject);
begin
  SelectedDynamoVersion := DynamoVersionCombo.Items[DynamoVersionCombo.ItemIndex];
end;

// ============================================================
// Run Guide Page - Shows how to launch BIBIM after installation
// Must be declared BEFORE InitializeWizard (Pascal forward declaration)
// ============================================================
procedure CreateRunGuidePage();
var
  TitleLabel, Step1Label, Step2Label, Step3Label, TipLabel, ApiKeyLabel: TNewStaticText;
begin
  RunGuidePage := CreateCustomPage(
    wpInstalling,
    '{#TextRunGuidePageTitle}',
    '{#TextRunGuidePageDesc}');

  // Title
  TitleLabel := TNewStaticText.Create(RunGuidePage);
  TitleLabel.Parent := RunGuidePage.Surface;
  TitleLabel.Top := 0;
  TitleLabel.Left := 0;
  TitleLabel.Width := RunGuidePage.SurfaceWidth;
  TitleLabel.Caption := '{#TextRunGuideTitle}';
  TitleLabel.Font.Style := [fsBold];
  TitleLabel.Font.Size := 11;
  TitleLabel.Font.Color := $00008000; // Dark Green

  // Step 1
  Step1Label := TNewStaticText.Create(RunGuidePage);
  Step1Label.Parent := RunGuidePage.Surface;
  Step1Label.Top := 30;
  Step1Label.Left := 0;
  Step1Label.Width := RunGuidePage.SurfaceWidth;
  Step1Label.Caption := '{#TextRunGuideStep1}';
  Step1Label.Font.Size := 10;

  // Step 2
  Step2Label := TNewStaticText.Create(RunGuidePage);
  Step2Label.Parent := RunGuidePage.Surface;
  Step2Label.Top := 52;
  Step2Label.Left := 0;
  Step2Label.Width := RunGuidePage.SurfaceWidth;
  Step2Label.Caption := '{#TextRunGuideStep2}';
  Step2Label.Font.Size := 10;

  // Step 3
  Step3Label := TNewStaticText.Create(RunGuidePage);
  Step3Label.Parent := RunGuidePage.Surface;
  Step3Label.Top := 74;
  Step3Label.Left := 0;
  Step3Label.Width := RunGuidePage.SurfaceWidth;
  Step3Label.WordWrap := True;
  Step3Label.Height := 40;
  Step3Label.Caption := '{#TextRunGuideStep3}';
  Step3Label.Font.Size := 10;
  Step3Label.Font.Style := [fsBold];
  Step3Label.Font.Color := $000000FF; // Red for emphasis

  // Guide Image
  ExtractTemporaryFile('bibim_run_guide.bmp');
  RunGuideImage := TBitmapImage.Create(RunGuidePage);
  RunGuideImage.Parent := RunGuidePage.Surface;
  RunGuideImage.Top := 115;
  RunGuideImage.Left := 0;
  RunGuideImage.Width := 450;
  RunGuideImage.Height := 280;
  RunGuideImage.Stretch := True;
  RunGuideImage.Bitmap.LoadFromFile(ExpandConstant('{tmp}\bibim_run_guide.bmp'));

  // Tip
  TipLabel := TNewStaticText.Create(RunGuidePage);
  TipLabel.Parent := RunGuidePage.Surface;
  TipLabel.Top := 400;
  TipLabel.Left := 0;
  TipLabel.Width := RunGuidePage.SurfaceWidth;
  TipLabel.Height := 40;
  TipLabel.WordWrap := True;
  TipLabel.Caption := '{#TextRunGuideTipLine1}' + #13#10 + '      {#TextRunGuideTipLine2}';
  TipLabel.Font.Color := $00808080; // Gray

  // API Key hint
  ApiKeyLabel := TNewStaticText.Create(RunGuidePage);
  ApiKeyLabel.Parent := RunGuidePage.Surface;
  ApiKeyLabel.Top := 440;
  ApiKeyLabel.Left := 0;
  ApiKeyLabel.Width := RunGuidePage.SurfaceWidth;
  ApiKeyLabel.Height := 36;
  ApiKeyLabel.WordWrap := True;
  ApiKeyLabel.Caption := '{#TextRunGuideApiKeyLine}';
  ApiKeyLabel.Font.Size := 9;
  ApiKeyLabel.Font.Color := $00005500; // Dark green
end;

procedure InitializeWizard();
var
  RevitLabel, DynamoLabel, WarningLabel: TNewStaticText;
  DetectedText: String;
  PairsText: String;
begin
  DetectedRevitVersion := DetectInstalledRevit();
  DetectedDynamoVersion := DetectInstalledDynamo();

  VersionSelectionPage := CreateCustomPage(
    wpWelcome,
    '{#TextVersionPageTitle}',
    '{#TextVersionPageDesc}');

  DetectedInfoLabel := TNewStaticText.Create(VersionSelectionPage);
  DetectedInfoLabel.Parent := VersionSelectionPage.Surface;
  DetectedInfoLabel.Top := 0;
  DetectedInfoLabel.Width := VersionSelectionPage.SurfaceWidth;
  DetectedInfoLabel.Height := 100;
  DetectedInfoLabel.WordWrap := True;
  DetectedInfoLabel.Font.Style := [fsBold];
  DetectedInfoLabel.Font.Color := clGreen;
  
  // Show all Revit-Dynamo pairs
  PairsText := DetectRevitDynamoPairs();
  if PairsText <> '' then
    DetectedText := '💡 {#TextDetectedPairsHeader}' + #13#10 + PairsText
  else
    DetectedText := '💡 {#TextDetectedVersionHeader}' + #13#10 + '    • Revit: ' + DetectedRevitVersion + #13#10 + '    • Dynamo: ' + DetectedDynamoVersion;
  DetectedInfoLabel.Caption := DetectedText;

  // WARNING LABEL - Visible and emphasized
  WarningLabel := TNewStaticText.Create(VersionSelectionPage);
  WarningLabel.Parent := VersionSelectionPage.Surface;
  WarningLabel.Top := 105;
  WarningLabel.Width := VersionSelectionPage.SurfaceWidth;
  WarningLabel.Height := 50;
  WarningLabel.WordWrap := True;
  WarningLabel.Font.Style := [fsBold];
  WarningLabel.Font.Color := clRed;
  WarningLabel.Caption := '⚠️  {#TextWarningLine1}' + #13#10 + '{#TextWarningLine2}';

  RevitLabel := TNewStaticText.Create(VersionSelectionPage);
  RevitLabel.Parent := VersionSelectionPage.Surface;
  RevitLabel.Top := 165;
  RevitLabel.Caption := '{#TextRevitLabel}';
  RevitLabel.Font.Style := [fsBold];

  RevitVersionCombo := TNewComboBox.Create(VersionSelectionPage);
  RevitVersionCombo.Parent := VersionSelectionPage.Surface;
  RevitVersionCombo.Top := 185;
  RevitVersionCombo.Width := 250;
  RevitVersionCombo.Style := csDropDownList;
  RevitVersionCombo.OnChange := @RevitVersionChanged;
  
  RevitVersionCombo.Items.Add('2022');                   // Index 0
  RevitVersionCombo.Items.Add('2023');                   // Index 1
  RevitVersionCombo.Items.Add('2024');                   // Index 2
  RevitVersionCombo.Items.Add('2025 / 2025.1 / 2025.2'); // Index 3 — RAG_STORE_2025
  RevitVersionCombo.Items.Add('2025.3 / 2025.4');        // Index 4 — RAG_STORE_2025_3
  RevitVersionCombo.Items.Add('2026');                   // Index 5
  RevitVersionCombo.Items.Add('2027');                   // Index 6

  // Auto-detect sets the group; user can manually switch to 2025.3/2025.4 if needed
  RevitVersionCombo.ItemIndex := 3; // Default: 2025 group
  if Pos('2022', DetectedRevitVersion) > 0 then RevitVersionCombo.ItemIndex := 0;
  if Pos('2023', DetectedRevitVersion) > 0 then RevitVersionCombo.ItemIndex := 1;
  if Pos('2024', DetectedRevitVersion) > 0 then RevitVersionCombo.ItemIndex := 2;
  if Pos('2025', DetectedRevitVersion) > 0 then RevitVersionCombo.ItemIndex := 3;
  if Pos('2026', DetectedRevitVersion) > 0 then RevitVersionCombo.ItemIndex := 5;
  if Pos('2027', DetectedRevitVersion) > 0 then RevitVersionCombo.ItemIndex := 6;

  SelectedRevitVersion := ParseRevitVersion(RevitVersionCombo.Items[RevitVersionCombo.ItemIndex]);

  DynamoLabel := TNewStaticText.Create(VersionSelectionPage);
  DynamoLabel.Parent := VersionSelectionPage.Surface;
  DynamoLabel.Top := 225;
  DynamoLabel.Caption := '{#TextDynamoLabel}';
  DynamoLabel.Font.Style := [fsBold];

  DynamoVersionCombo := TNewComboBox.Create(VersionSelectionPage);
  DynamoVersionCombo.Parent := VersionSelectionPage.Surface;
  DynamoVersionCombo.Top := 245;
  DynamoVersionCombo.Width := 200;
  DynamoVersionCombo.Style := csDropDownList;
  DynamoVersionCombo.OnChange := @DynamoVersionChanged;
  
  // 2.10 omitted — no installer build exists for R2022_D210
  DynamoVersionCombo.Items.Add('2.12');   // Index 0
  DynamoVersionCombo.Items.Add('2.13');   // Index 1
  DynamoVersionCombo.Items.Add('2.16');   // Index 2
  DynamoVersionCombo.Items.Add('2.17');   // Index 3
  DynamoVersionCombo.Items.Add('2.18');   // Index 4
  DynamoVersionCombo.Items.Add('2.19');   // Index 5
  DynamoVersionCombo.Items.Add('3.0');    // Index 6
  DynamoVersionCombo.Items.Add('3.2');    // Index 7
  DynamoVersionCombo.Items.Add('3.3');    // Index 8
  DynamoVersionCombo.Items.Add('3.4');    // Index 9
  DynamoVersionCombo.Items.Add('3.5');    // Index 10
  DynamoVersionCombo.Items.Add('3.6');    // Index 11
  DynamoVersionCombo.Items.Add('27.0');   // Index 12 — Revit 2027 bundled Dynamo

  DynamoVersionCombo.ItemIndex := 8; // Default 3.3

  // DEFAULT: Use latest known Dynamo version for each Revit
  // Most users will have the latest version installed
  if Pos('2022', SelectedRevitVersion) > 0 then DynamoVersionCombo.ItemIndex := 0;  // 2.12 (latest for 2022)
  if Pos('2023', SelectedRevitVersion) > 0 then DynamoVersionCombo.ItemIndex := 2;  // 2.16 (latest for 2023)
  if Pos('2024', SelectedRevitVersion) > 0 then DynamoVersionCombo.ItemIndex := 5;  // 2.19 (latest for 2024)
  if Pos('2025', SelectedRevitVersion) > 0 then DynamoVersionCombo.ItemIndex := 8;  // 3.3 (latest for 2025)
  if Pos('2026', SelectedRevitVersion) > 0 then DynamoVersionCombo.ItemIndex := 11; // 3.6 (latest for 2026)
  if Pos('2027', SelectedRevitVersion) > 0 then DynamoVersionCombo.ItemIndex := 12; // 27.0 (bundled with 2027)

  // OVERRIDE: Revit 2027 always uses 27.0 folder (Dynamo year-based versioning)
  if (Pos('2027', SelectedRevitVersion) > 0) then
  begin
    DynamoVersionCombo.ItemIndex := 12; // 27.0 (bundled with Revit 2027)
  end;

  if (Pos('2026', SelectedRevitVersion) > 0) then
  begin
    // Revit 2026 users: default to 3.6, but override to 3.4 if that's what's installed
    if (Pos('3.4', DetectedDynamoVersion) > 0) and (Pos('3.6', DetectedDynamoVersion) = 0) then
      DynamoVersionCombo.ItemIndex := 9; // 3.4 (bundled with Revit 2026)
  end;

  if (Pos('2024', SelectedRevitVersion) > 0) then
  begin
    // Revit 2024: default to 2.19, override to older if needed
    if (Pos('2.17', DetectedDynamoVersion) > 0) and (Pos('2.18', DetectedDynamoVersion) = 0) and (Pos('2.19', DetectedDynamoVersion) = 0) then
      DynamoVersionCombo.ItemIndex := 3; // 2.17
  end;
  
  SelectedDynamoVersion := DynamoVersionCombo.Items[DynamoVersionCombo.ItemIndex];
  
  // ============================================================
  // Run Guide Page - Shows how to launch BIBIM after installation
  // ============================================================
  CreateRunGuidePage();
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = VersionSelectionPage.ID then
  begin
    FinalInstallPath := CalculateInstallPath();
    WizardForm.DirEdit.Text := FinalInstallPath;
    
    // Auto-create folders if missing — check each level independently
    // (version folder may exist but packages subfolder may not)
    if not DirExists(GetDynamoBasePath() + '\' + SelectedDynamoVersion) then
      CreateDir(GetDynamoBasePath() + '\' + SelectedDynamoVersion);
    if not DirExists(GetDynamoBasePath() + '\' + SelectedDynamoVersion + '\packages') then
      CreateDir(GetDynamoBasePath() + '\' + SelectedDynamoVersion + '\packages');
    
    RagStoreName := GetRagStoreForRevit(SelectedRevitVersion);
  end;
end;

procedure CreateRagConfigFile();
var
  ConfigPath, ConfigContent, RevitYear, FallbackStore: String;
begin
  ConfigPath := ExpandConstant('{app}') + '\rag_config.json';
  RevitYear := GetRevitYear(SelectedRevitVersion);
  FallbackStore := GetFallbackRagStoreForRevit(SelectedRevitVersion);

  ConfigContent := '{' + #13#10;
  ConfigContent := ConfigContent + '  "detected_revit_version": "' + RevitYear + '",' + #13#10;
  ConfigContent := ConfigContent + '  "detected_dynamo_version": "' + SelectedDynamoVersion + '",' + #13#10;
  ConfigContent := ConfigContent + '  "active_store": "' + RagStoreName + '",' + #13#10;
  if FallbackStore <> '' then
    ConfigContent := ConfigContent + '  "fallback_store": "' + FallbackStore + '",' + #13#10;
  ConfigContent := ConfigContent + '  "claude_model": "claude-sonnet-4-6",' + #13#10;
  ConfigContent := ConfigContent + '  "gemini_model": "gemini-2.0-flash",' + #13#10;
  ConfigContent := ConfigContent + '  "api_keys": {' + #13#10;
  ConfigContent := ConfigContent + '    "claude_api_key": "",' + #13#10;
  ConfigContent := ConfigContent + '    "gemini_api_key": ""' + #13#10;
  ConfigContent := ConfigContent + '  },' + #13#10;
  ConfigContent := ConfigContent + '  "validation": {' + #13#10;
  ConfigContent := ConfigContent + '    "gate_enabled": true,' + #13#10;
  ConfigContent := ConfigContent + '    "auto_fix_enabled": true,' + #13#10;
  ConfigContent := ConfigContent + '    "auto_fix_max_attempts": 2,' + #13#10;
  ConfigContent := ConfigContent + '    "verify_stage_enabled": false,' + #13#10;
  ConfigContent := ConfigContent + '    "enable_api_xml_hints": true,' + #13#10;
  ConfigContent := ConfigContent + '    "rollout_phase": "phase3"' + #13#10;
  ConfigContent := ConfigContent + '  }' + #13#10;
  ConfigContent := ConfigContent + '}';
  SaveStringToFile(ConfigPath, ConfigContent, False);
end;

procedure UpdateViewExtensionXml();
var
  XmlPath, DllPath: String;
  XmlLines: TArrayOfString;
begin
  XmlPath := ExpandConstant('{app}') + '\extra\BIBIM_ViewExtensionDefinition.xml';
  DllPath := ExpandConstant('{app}') + '\BIBIM_MVP.dll';
  
  // 한글 사용자명 경로 문제 해결: 템플릿 파일 읽지 않고 직접 생성
  // UTF-8로 저장하여 한글 경로도 정상 처리
  SetArrayLength(XmlLines, 5);
  XmlLines[0] := '<?xml version="1.0" encoding="utf-8"?>';
  XmlLines[1] := '<ViewExtensionDefinition>';
  XmlLines[2] := '  <AssemblyPath>' + DllPath + '</AssemblyPath>';
  XmlLines[3] := '  <TypeName>BIBIM_MVP.BIBIM_Extension</TypeName>';
  XmlLines[4] := '</ViewExtensionDefinition>';
  
  SaveStringsToUTF8File(XmlPath, XmlLines, False);
end;
// Generate unique AppId based on Revit/Dynamo version
// Dots stripped from both versions to keep the registry key clean
// e.g. Revit 2025.3 + Dynamo 3.3 → BIBIM_R2025_3_D33
function GetDynamicAppId(): String;
var
  DynamoClean, RevitClean: String;
begin
  DynamoClean := SelectedDynamoVersion;
  StringChangeEx(DynamoClean, '.', '', True);
  RevitClean := SelectedRevitVersion;
  StringChangeEx(RevitClean, '.', '_', True);
  Result := 'BIBIM_R' + RevitClean + '_D' + DynamoClean;
end;

// Get display name with version info
function GetDisplayName(): String;
begin
  Result := 'BIBIM AI (Revit ' + SelectedRevitVersion + ' - Dynamo ' + SelectedDynamoVersion + ')';
end;

procedure CreateCustomUninstaller();
var
  UninstallScript, ScriptPath, RegKeyName: String;
  InstallPath: String;
begin
  InstallPath := ExpandConstant('{app}');
  ScriptPath := InstallPath + '\uninstall.ps1';
  RegKeyName := GetDynamicAppId() + '_is1';
  
  // Create PowerShell uninstall script that copies itself to temp and runs from there
  UninstallScript := '# BIBIM AI Uninstaller' + #13#10;
  UninstallScript := UninstallScript + '$ErrorActionPreference = "SilentlyContinue"' + #13#10;
  UninstallScript := UninstallScript + '$InstallPath = "' + InstallPath + '"' + #13#10;
  UninstallScript := UninstallScript + '$RegKey = "' + RegKeyName + '"' + #13#10;
  UninstallScript := UninstallScript + '' + #13#10;
  UninstallScript := UninstallScript + '# Delete install folder' + #13#10;
  UninstallScript := UninstallScript + 'Start-Sleep -Seconds 1' + #13#10;
  UninstallScript := UninstallScript + 'Remove-Item -Path $InstallPath -Recurse -Force' + #13#10;
  UninstallScript := UninstallScript + '' + #13#10;
  UninstallScript := UninstallScript + '# Delete registry key' + #13#10;
  UninstallScript := UninstallScript + 'Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$RegKey" -Recurse -Force' + #13#10;
  
  SaveStringToFile(ScriptPath, UninstallScript, False);
end;

procedure RegisterVersionSpecificUninstall();
var
  NewKey: String;
  DisplayName, InstallPath: String;
begin
  NewKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + GetDynamicAppId() + '_is1';
  InstallPath := ExpandConstant('{app}');
  DisplayName := GetDisplayName();
  
  Log('Creating version-specific registry key: ' + NewKey);
  
  // Register this version with its own uninstaller
  RegWriteStringValue(HKCU, NewKey, 'DisplayName', DisplayName);
  RegWriteStringValue(HKCU, NewKey, 'UninstallString', 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + InstallPath + '\uninstall.ps1"');
  RegWriteStringValue(HKCU, NewKey, 'InstallLocation', InstallPath);
  RegWriteStringValue(HKCU, NewKey, 'DisplayIcon', InstallPath + '\bibim-icon-blue.ico');
  RegWriteStringValue(HKCU, NewKey, 'Publisher', '{#MyAppPublisher}');
  RegWriteStringValue(HKCU, NewKey, 'DisplayVersion', '{#MyAppVersion}');
  RegWriteStringValue(HKCU, NewKey, 'URLInfoAbout', '{#MyAppURL}');
  RegWriteDWordValue(HKCU, NewKey, 'NoModify', 1);
  RegWriteDWordValue(HKCU, NewKey, 'NoRepair', 1);
  
  Log('Registered: ' + DisplayName);
end;

procedure DeleteDefaultInnoSetupKey();
var
  DefaultKey: String;
  OpenBrace, CloseBrace: String;
begin
  OpenBrace := Chr(123);
  CloseBrace := Chr(125);
  DefaultKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + OpenBrace + 'B1B1M-C0P1-L0T1-2025-REVIT000001' + CloseBrace + CloseBrace + '_is1';
  
  if RegKeyExists(HKCU, DefaultKey) then
  begin
    RegDeleteKeyIncludingSubkeys(HKCU, DefaultKey);
    Log('Deleted default Inno Setup key');
  end;
end;

procedure CreateInstallLog();
var
  LogPath: String;
  LogLines: TArrayOfString;
  Timestamp: String;
begin
  // 설치 로그를 사용자 폴더에 생성 (디버깅용, 개인정보 제외)
  LogPath := GetEnv('USERPROFILE') + '\bibim_install_log.txt';
  Timestamp := GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':');
  
  SetArrayLength(LogLines, 12);
  LogLines[0] := '=== BIBIM AI Installation Log ===';
  LogLines[1] := 'Timestamp: ' + Timestamp;
  LogLines[2] := 'Installer Version: {#MyAppVersion}';
  LogLines[3] := 'Selected Revit: ' + SelectedRevitVersion;
  LogLines[4] := 'Selected Dynamo: ' + SelectedDynamoVersion;
  LogLines[5] := 'Detected Revit: ' + DetectedRevitVersion;
  LogLines[6] := 'Detected Dynamo: ' + DetectedDynamoVersion;
  LogLines[7] := 'Install Path: ' + ExpandConstant('{app}');
  LogLines[8] := 'RAG Store: ' + RagStoreName;
  LogLines[9] := '---';
  LogLines[10] := 'ViewExtension XML: ' + ExpandConstant('{app}') + '\extra\BIBIM_ViewExtensionDefinition.xml';
  LogLines[11] := 'DLL Path: ' + ExpandConstant('{app}') + '\BIBIM_MVP.dll';
  
  SaveStringsToUTF8File(LogPath, LogLines, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    CreateRagConfigFile();
    UpdateViewExtensionXml();
    CreateInstallLog();
    CreateCustomUninstaller();
    RegisterVersionSpecificUninstall();
    DeleteDefaultInnoSetupKey();
  end;
end;

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-ChildItem -Path '{app}' -Recurse | Unblock-File"""; Flags: runhidden
