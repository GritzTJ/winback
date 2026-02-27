; ============================================================================
;  WinBack — Script d'installation Inno Setup 6
;  Application : sauvegarde incrémentielle automatique sur disques externes
;  Version     : 0.1.0
;
;  Prérequis de compilation :
;    1. Inno Setup 6.x  (https://jrsoftware.org/isinfo.php)
;    2. build.ps1 -Installer  (génère publish\WinBack.exe AVANT de lancer iscc)
; ============================================================================

#define MyAppName      "WinBack"
#define MyAppVersion   "0.1.0"
#define MyAppPublisher "WinBack Contributors"
#define MyAppExeName   "WinBack.exe"
#define MyAppExeSrc    "..\publish\WinBack.exe"

; ---------------------------------------------------------------------------
[Setup]
; NOTE : AppId doit rester stable d'une version à l'autre.
; Changer ce GUID force Windows à considérer l'appli comme une nouvelle entrée.
AppId={{B7E3A124-9F5C-4D82-A1B6-8C2E7D0F3A45}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright (C) 2026 WinBack Contributors

; Installation dans Program Files (64-bit)
DefaultDirName={autopf64}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes

; Licence affichée à l'installateur
LicenseFile=..\LICENSE

; Fichier de sortie
OutputDir=output
OutputBaseFilename=WinBack-{#MyAppVersion}-Setup

; Compression maximale
Compression=lzma2/ultra64
SolidCompression=yes

; Apparence
WizardStyle=modern
WizardSizePercent=110

; Droits administrateur obligatoires (VSS)
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=

; Architecture x64 uniquement
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

; Windows 10 version 1903 minimum (build 18362)
MinVersion=10.0.18362

; Entrée dans Paramètres → Applications
UninstallDisplayName={#MyAppName} {#MyAppVersion}
UninstallDisplayIcon={app}\{#MyAppExeName},0

; Redémarrage non nécessaire
RestartIfNeededByRun=no

; ---------------------------------------------------------------------------
[Languages]
Name: "french";  MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

; ---------------------------------------------------------------------------
[CustomMessages]
; ── Français ────────────────────────────────────────────────────────────────
french.TaskDesktop=Créer un raccourci sur le Bureau
french.TaskAutoStart=Démarrer automatiquement avec Windows
french.GroupOther=Options
french.DelDataTitle=Suppression des données
french.DelDataText=Les données suivantes ont été trouvées sur cet ordinateur :%n%n    %1%n%nVoulez-vous les supprimer définitivement ?%n%n(Profils de sauvegarde, historique des exécutions)
french.PostInstallNote=WinBack démarre dans la barre des tâches.%nDouble-clic sur l'icône pour ouvrir l'interface.

; ── English ─────────────────────────────────────────────────────────────────
english.TaskDesktop=Create a Desktop shortcut
english.TaskAutoStart=Start automatically with Windows
english.GroupOther=Options
english.DelDataTitle=Remove user data
english.DelDataText=The following folder was found on this computer:%n%n    %1%n%nDo you want to permanently delete it?%n%n(Backup profiles, run history)
english.PostInstallNote=WinBack starts in the system tray.%nDouble-click the icon to open the interface.

; ---------------------------------------------------------------------------
[Tasks]
; Raccourci bureau (optionnel, décoché par défaut)
Name: "desktopicon"; \
  Description: "{cm:TaskDesktop}"; \
  GroupDescription: "{cm:AdditionalIcons}"; \
  Flags: unchecked

; Démarrage automatique (optionnel, coché par défaut)
Name: "autostart"; \
  Description: "{cm:TaskAutoStart}"; \
  GroupDescription: "{cm:GroupOther}"

; ---------------------------------------------------------------------------
[Files]
; Exécutable principal — seul fichier nécessaire (PublishSingleFile)
Source: "{#MyAppExeSrc}"; \
  DestDir: "{app}"; \
  DestName: "{#MyAppExeName}"; \
  Flags: ignoreversion

; ---------------------------------------------------------------------------
[Icons]
; Menu Démarrer
Name: "{group}\{#MyAppName}";                    Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

; Bureau (si tâche cochée)
Name: "{autodesktop}\{#MyAppName}"; \
  Filename: "{app}\{#MyAppExeName}"; \
  Tasks: desktopicon

; ---------------------------------------------------------------------------
[Registry]
; Clé Run pour démarrage automatique (ajoutée seulement si tâche cochée ;
; supprimée par l'installateur si décochée à la mise à jour)
Root: HKCU; \
  Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; \
  ValueName: "{#MyAppName}"; \
  ValueData: """{app}\{#MyAppExeName}"""; \
  Tasks: autostart; \
  Flags: uninsdeletevalue

; ---------------------------------------------------------------------------
[UninstallRun]
; Arrêter WinBack proprement avant de supprimer les fichiers
Filename: "taskkill.exe"; \
  Parameters: "/F /IM ""{#MyAppExeName}"""; \
  Flags: runhidden waituntilterminated; \
  RunOnceId: "KillWinBack"

; ---------------------------------------------------------------------------
[Run]
; Proposer de lancer l'application après installation
Filename: "{app}\{#MyAppExeName}"; \
  Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent runasoriginaluser

; ---------------------------------------------------------------------------
[Code]

// ── Désinstallation ─────────────────────────────────────────────────────────

/// Appelé à chaque étape de la désinstallation.
/// À l'étape usPostUninstall :
///   1. Supprime la clé Run même si elle a été créée par l'app elle-même
///   2. Propose de supprimer %LOCALAPPDATA%\WinBack (profils, historique)
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir : String;
  Msg     : String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // 1. Supprimer la clé Run (elle peut exister même si la tâche n'était pas
    //    cochée à l'installation, car l'app peut la créer elle-même via les
    //    Paramètres → "Démarrer avec Windows").
    RegDeleteValue(
      HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      '{#MyAppName}');

    // 2. Proposer la suppression des données utilisateur
    DataDir := ExpandConstant('{localappdata}\WinBack');
    if DirExists(DataDir) then
    begin
      Msg := FmtMessage(CustomMessage('DelDataText'), [DataDir]);
      if MsgBox(Msg, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;

// ── Vérification à l'installation ──────────────────────────────────────────

/// Bloque l'installation si l'architecture n'est pas x64.
function InitializeSetup(): Boolean;
begin
  Result := True;
  if not Is64BitInstallMode then
  begin
    MsgBox(
      '{#MyAppName} requires a 64-bit version of Windows (x64).',
      mbError, MB_OK);
    Result := False;
  end;
end;
