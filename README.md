**[🇫🇷 Français](#français) · [🇬🇧 English](#english)**

---

# Français

## WinBack

**WinBack** est une application Windows de sauvegarde incrémentielle automatique. Elle surveille les insertions de disques externes (HDD/SSD) et déclenche automatiquement la sauvegarde des dossiers configurés, en ne copiant que les fichiers nouveaux, modifiés ou supprimés depuis la dernière sauvegarde.

### Fonctionnalités

- **Détection automatique** des disques externes à l'insertion
- **Identification fiable** par GUID de volume Windows (stable entre reconnexions)
- **Plusieurs profils par disque** : plusieurs configurations de sauvegarde peuvent être associées au même disque
- **Sauvegarde incrémentielle** : seuls les fichiers ajoutés, modifiés ou supprimés sont traités
- **Filtres d'exclusion** par patterns glob (`*.tmp`, `~$*`, `node_modules/**`…)
- **Trois stratégies de suppression** : miroir strict, corbeille de sauvegarde (avec rétention configurable), ou accumulation
- **Retry automatique** sur erreur de copie (nombre de tentatives et délai configurables)
- **Reprise sur interruption** : si le disque est retiré pendant la sauvegarde, l'exécution est marquée `Interrupted` (distincte d'une annulation manuelle)
- **Support VSS** (Volume Shadow Copy) pour copier les fichiers ouverts (PST Outlook, bases de données…)
- **Vérification d'intégrité** optionnelle par hash MD5 post-copie
- **Chiffrement AES-256** par profil : les fichiers copiés sur le disque de sauvegarde sont chiffrés (IV aléatoire par fichier, clé dérivée du mot de passe — portable entre machines, aucun stockage du mot de passe)
- **Audit d'intégrité à la demande** : vérifie que les fichiers sauvegardés correspondent aux snapshots (détecte les fichiers manquants ou corrompus)
- **Restauration intégrée** : restaure un dossier de sauvegarde (chiffré ou non) vers n'importe quel dossier de destination, sur n'importe quelle machine avec le même mot de passe
- **Notifications cliquables** : cliquer sur le ballon de notification ouvre directement l'historique
- **Mode simulation (dry run)** pour prévisualiser sans modifier aucun fichier
- **Interface simplifiée** (assistant 4 étapes) et **mode avancé** optionnel
- **Notifications Windows** via l'icône de la barre système
- **Historique détaillé** de chaque exécution (fichiers ajoutés, modifiés, supprimés, erreurs)
- **Démarrage automatique** avec Windows (registre `HKCU\Run`)

### Architecture

```
WinBack.sln
├── WinBack.Core/          Bibliothèque métier (sans dépendance UI)
│   ├── Models/            Entités EF Core (profils, snapshots, historique)
│   ├── Data/              DbContext SQLite (EF Core 9)
│   └── Services/
│       ├── BackupEngine       Moteur de copie incrémentielle asynchrone
│       ├── DiffCalculator     Calcul des différences source / snapshot
│       ├── DriveIdentifier    Identification disque par GUID (P/Invoke)
│       ├── ProfileService     CRUD profils et historique
│       └── VssHelper          Gestion des snapshots VSS
│
├── WinBack.App/           Application WPF (.NET 9, MVVM)
│   ├── Services/
│   │   ├── UsbMonitorService    Surveillance WM_DEVICECHANGE
│   │   ├── BackupOrchestrator   Coordination du cycle de sauvegarde
│   │   └── NotificationService  Notifications via Shell_NotifyIcon (P/Invoke)
│   ├── Controls/            Composants WPF réutilisables
│   │   └── StackPanelEx     Propriété attachée Spacing (équivalent WinUI)
│   ├── ViewModels/          CommunityToolkit.Mvvm
│   ├── Views/               WPF / XAML (thème Windows 11)
│   └── Resources/           Styles, icônes
│
└── WinBack.Tests/         Tests unitaires (xunit)
    ├── BackupPairTests              Glob patterns et filtres d'exclusion
    ├── DiffCalculatorTests          Calcul diff (ajouts, modifs, suppressions, exclusions)
    ├── BackupRunTests               Propriétés calculées (Duration, TotalFiles, statuts)
    ├── BackupEngineEncryptionTests  Chiffrement AES-256 (DeriveKey, roundtrip chiffrement/déchiffrement)
    └── AuditTests                   Audit d'intégrité (OK, manquant, corrompu)
```

### Prérequis

| Composant | Version minimale |
|---|---|
| Windows | 10 (build 1903) ou 11 |
| Architecture | x64 uniquement |
| .NET Runtime | 9.0 (sauf build `-SelfContained`) |
| Droits | Administrateur (requis pour VSS) |

### Compilation

#### Prérequis outils

- [.NET SDK 9.0+](https://dot.net/download)
- Visual Studio 2022 (17.8+) ou Rider, ou `dotnet` CLI seul

#### 1. Créer les icônes placeholder

```powershell
cd WinBack.App\Resources\Icons
.\create_placeholder_icons.ps1
```

#### 2. Compiler

```powershell
dotnet build WinBack.sln -c Release
```

#### 3. Lancer en développement

```powershell
dotnet run --project WinBack.App
```

> L'application démarre dans la barre système. Double-clic ou clic droit sur l'icône pour ouvrir l'interface.

### Télécharger l'installateur (utilisateurs finaux)

La page [Releases](../../releases/latest) du dépôt contient le fichier `WinBack-X.Y.Z-Setup.exe` prêt à l'emploi — aucun prérequis, aucune installation de .NET nécessaire.

### Distribution (développeurs)

Le script `build.ps1` génère l'exécutable et/ou l'installateur.

| Commande | Fichier produit | Taille approx. | Prérequis sur le PC cible |
|---|---|---|---|
| `.\build.ps1` | `publish\WinBack.exe` | ~15–20 Mo | .NET 9 Runtime installé |
| `.\build.ps1 -SelfContained` | `publish\WinBack.exe` | ~80–100 Mo | Aucun |
| `.\build.ps1 -Installer` | `installer\output\WinBack-0.3.1-Setup.exe` | ~80–100 Mo | Aucun |
| `.\build.ps1 -Clean` | (nettoyage avant build) | — | — |

```powershell
# Installateur Windows complet (recommandé pour la distribution)
.\build.ps1 -Installer
```

> **Note :** un exécutable `PublishSingleFile` décompresse ses ressources dans un dossier temporaire au premier lancement. Les lancements suivants sont instantanés.

#### Prérequis pour générer l'installateur

Aucun — si Inno Setup 6 n'est pas détecté, `build.ps1` le télécharge et l'installe automatiquement dans `.tools\innosetup\` (dossier local au projet, gitignorée).

### Installation et désinstallation

#### Installation via le setup

Lancer `WinBack-0.3.1-Setup.exe` et suivre l'assistant. Options proposées :

- Raccourci sur le bureau (décoché par défaut)
- Démarrage automatique avec Windows (coché par défaut)

L'application s'installe dans `C:\Program Files\WinBack\` et apparaît dans **Paramètres → Applications**.

#### Désinstallation

Depuis **Paramètres → Applications → WinBack → Désinstaller**, ou via le raccourci dans le menu Démarrer.

Lors de la désinstallation, il est proposé de supprimer également les données utilisateur (`%LOCALAPPDATA%\WinBack\`) contenant les profils de sauvegarde et l'historique.

### Structure de la base de données

La base SQLite est créée automatiquement au premier démarrage :

```
%LOCALAPPDATA%\WinBack\winback.db
```

Elle contient les profils de sauvegarde, les snapshots d'état des fichiers (utilisés pour le calcul différentiel) et l'historique de toutes les exécutions.

### Packages NuGet

| Package | Rôle |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite 9.x` | ORM + base de données |
| `Microsoft.Extensions.Hosting 9.x` | DI + services hébergés |
| `CommunityToolkit.Mvvm 8.x` | ObservableObject, RelayCommand |
| `H.NotifyIcon.Wpf 2.x` | Icône barre système (tray) |
| `System.Management 9.x` | WMI (identification disques, VSS) |
| `xunit 2.x` | Tests unitaires (WinBack.Tests) |

### Licence

MIT

---

# English

## WinBack

**WinBack** is a Windows application for automatic incremental backups. It monitors external drive insertions (HDD/SSD) and automatically triggers backups of configured folders, copying only files that are new, modified, or deleted since the last backup.

### Features

- **Automatic detection** of external drives on insertion
- **Reliable identification** by Windows volume GUID (stable across reconnections)
- **Multiple profiles per drive**: several backup configurations can be associated with the same drive
- **Incremental backup**: only added, modified, or deleted files are processed
- **Exclusion filters** via glob patterns (`*.tmp`, `~$*`, `node_modules/**`…)
- **Three deletion strategies**: strict mirror, recycle bin (with configurable retention), or additive
- **Automatic retry** on copy error (configurable attempt count and delay)
- **Interruption detection**: if the drive is removed during a backup, the run is marked `Interrupted` (distinct from a manual cancellation)
- **VSS support** (Volume Shadow Copy) to copy open files (Outlook PST, databases…)
- **Optional integrity check** via MD5 hash after copy
- **AES-256 encryption** per profile: files copied to the backup drive are encrypted (random IV per file, key derived from password — portable across machines, password never stored)
- **On-demand integrity audit**: verifies that backed-up files match their snapshots (detects missing or corrupted files)
- **Built-in restore**: restores a backup folder (encrypted or not) to any destination folder, on any machine with the same password
- **Clickable notifications**: clicking the balloon notification opens the history window directly
- **Dry run mode** to preview changes without modifying any files
- **Simplified interface** (4-step wizard) with optional **advanced mode**
- **Windows notifications** via system tray icon
- **Detailed history** of each run (added, modified, deleted, error counts)
- **Auto-start with Windows** (registry `HKCU\Run`)

### Architecture

```
WinBack.sln
├── WinBack.Core/          Business library (no UI dependency)
│   ├── Models/            EF Core entities (profiles, snapshots, history)
│   ├── Data/              SQLite DbContext (EF Core 9)
│   └── Services/
│       ├── BackupEngine       Async incremental copy engine
│       ├── DiffCalculator     Source vs snapshot diff computation
│       ├── DriveIdentifier    Drive identification by GUID (P/Invoke)
│       ├── ProfileService     Profile & history CRUD
│       └── VssHelper          VSS snapshot management
│
├── WinBack.App/           WPF application (.NET 9, MVVM)
│   ├── Services/
│   │   ├── UsbMonitorService    WM_DEVICECHANGE monitoring
│   │   ├── BackupOrchestrator   Backup lifecycle coordination
│   │   └── NotificationService  Notifications via Shell_NotifyIcon (P/Invoke)
│   ├── Controls/            Reusable WPF components
│   │   └── StackPanelEx     Spacing attached property (WinUI equivalent)
│   ├── ViewModels/          CommunityToolkit.Mvvm
│   ├── Views/               WPF / XAML (Windows 11 theme)
│   └── Resources/           Styles, icons
│
└── WinBack.Tests/         Unit tests (xunit)
    ├── BackupPairTests              Glob patterns and exclusion filters
    ├── DiffCalculatorTests          Diff computation (added, modified, deleted, excluded)
    ├── BackupRunTests               Computed properties (Duration, TotalFiles, statuses)
    ├── BackupEngineEncryptionTests  AES-256 encryption (DeriveKey, encrypt/decrypt roundtrip)
    └── AuditTests                   Integrity audit (ok, missing, corrupted)
```

### Requirements

| Component | Minimum version |
|---|---|
| Windows | 10 (build 1903) or 11 |
| Architecture | x64 only |
| .NET Runtime | 9.0 (except `-SelfContained` build) |
| Privileges | Administrator (required for VSS) |

### Building

#### Tool requirements

- [.NET SDK 9.0+](https://dot.net/download)
- Visual Studio 2022 (17.8+), Rider, or `dotnet` CLI

#### 1. Create placeholder icons

```powershell
cd WinBack.App\Resources\Icons
.\create_placeholder_icons.ps1
```

#### 2. Build

```powershell
dotnet build WinBack.sln -c Release
```

#### 3. Run in development

```powershell
dotnet run --project WinBack.App
```

> The app starts in the system tray. Double-click or right-click the icon to open the interface.

### Download the installer (end users)

The [Releases](../../releases/latest) page contains a ready-to-use `WinBack-X.Y.Z-Setup.exe` — no prerequisites, no .NET installation required.

### Distribution (developers)

The `build.ps1` script produces the executable and/or the installer.

| Command | Output file | Approx. size | Requirements on target PC |
|---|---|---|---|
| `.\build.ps1` | `publish\WinBack.exe` | ~15–20 MB | .NET 9 Runtime installed |
| `.\build.ps1 -SelfContained` | `publish\WinBack.exe` | ~80–100 MB | None |
| `.\build.ps1 -Installer` | `installer\output\WinBack-0.3.1-Setup.exe` | ~80–100 MB | None |
| `.\build.ps1 -Clean` | (clean before build) | — | — |

```powershell
# Full Windows installer (recommended for distribution)
.\build.ps1 -Installer
```

> **Note:** A `PublishSingleFile` executable extracts its resources to a temp folder on first launch. Subsequent launches are instant.

#### Installer build requirement

None — if Inno Setup 6 is not detected, `build.ps1` downloads and installs it automatically into `.tools\innosetup\` (local project folder, git-ignored).

### Installation and uninstallation

#### Installing via the setup wizard

Run `WinBack-0.3.1-Setup.exe` and follow the wizard. Optional steps:

- Desktop shortcut (unchecked by default)
- Start automatically with Windows (checked by default)

The app is installed to `C:\Program Files\WinBack\` and appears in **Settings → Apps**.

#### Uninstalling

From **Settings → Apps → WinBack → Uninstall**, or via the shortcut in the Start Menu.

During uninstallation, you will be asked whether to also delete user data (`%LOCALAPPDATA%\WinBack\`) containing backup profiles and run history.

### Database

The SQLite database is created automatically on first launch:

```
%LOCALAPPDATA%\WinBack\winback.db
```

It stores backup profiles, file state snapshots (used for incremental diff computation), and the complete run history.

### NuGet Packages

| Package | Purpose |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite 9.x` | ORM + database |
| `Microsoft.Extensions.Hosting 9.x` | DI + hosted services |
| `CommunityToolkit.Mvvm 8.x` | ObservableObject, RelayCommand |
| `H.NotifyIcon.Wpf 2.x` | System tray icon |
| `System.Management 9.x` | WMI (drive identification, VSS) |
| `xunit 2.x` | Unit tests (WinBack.Tests) |

### License

MIT
