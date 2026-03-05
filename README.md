**[🇫🇷 Français](#français) · [🇬🇧 English](#english)**

---

# Français

## WinBack — Sauvegarde automatique sur disque externe

Branchez votre disque. WinBack fait le reste.

WinBack surveille les insertions de disques externes et déclenche instantanément
la sauvegarde de vos dossiers — sans clic, sans planning, sans cloud.
Vos données restent chez vous, chiffrées si vous le souhaitez,
accessibles sans internet même en cas de catastrophe.

### Pourquoi WinBack ?

| | |
|---|---|
| **Zéro friction** | Branchez le disque, la sauvegarde démarre toute seule. Débranchez-le, elle s'arrête proprement. |
| **Incrémentiel par nature** | Seuls les fichiers qui ont changé sont copiés. Une sauvegarde de 500 Go prend quelques secondes si peu de choses ont changé. |
| **Vos données vous appartiennent** | Aucun abonnement, aucun cloud, aucune donnée qui quitte votre réseau. Chiffrement AES-256 en option si le disque se perd. |

### Comment ça marche

1. **Configurez une fois** — choisissez quels dossiers sauvegarder et sur quel disque (identifié de façon stable par son GUID Windows, même si sa lettre change)
2. **Branchez, c'est sauvegardé** — WinBack vit dans la barre système et agit en arrière-plan dès la détection du disque
3. **Restaurez en un clic** — parcourez l'arborescence de sauvegarde, sélectionnez ce dont vous avez besoin, choisissez la destination

### Ce que WinBack sait faire

#### Sauvegarder intelligemment
- **Sauvegarde incrémentielle** — seuls les fichiers ajoutés, modifiés ou supprimés sont traités
- **Support VSS** — copie les fichiers ouverts (boîtes mail Outlook, bases de données, fichiers verrouillés…)
- **Trois stratégies** — miroir strict, corbeille avec rétention configurable, ou accumulation sans suppression
- **Filtres d'exclusion** — patterns glob (`*.tmp`, `~$*`, `node_modules/**`…) pour ignorer ce qui ne compte pas
- **Exclusions globales** — définissez une liste de patterns dans les Paramètres : ils s'appliquent automatiquement à tous les profils et toutes les paires, avec un bouton "Suggestions communes" pour démarrer vite
- **Retry automatique** — en cas d'erreur de copie, WinBack réessaie avant de déclarer l'échec
- **Aperçu avant sauvegarde** — voyez exactement ce qui va changer (X ajouts, Y modifications, Z suppressions) sans toucher un seul fichier

#### Garder le contrôle
- **Pause / Reprise** — interrompez une sauvegarde longue et reprenez-la là où elle s'est arrêtée
- **Annulation propre** — retirer le disque pendant une sauvegarde la marque `Interrupted`, distincte d'une annulation manuelle
- **Audit d'intégrité** — vérifiez à tout moment que vos fichiers sauvegardés sont intacts et non corrompus
- **Vérification post-copie** — hash SHA-256 optionnel pour s'assurer que chaque fichier est identique à la source

#### Sécuriser et chiffrer
- **Chiffrement AES-256 par profil** — les fichiers sur le disque sont illisibles sans votre mot de passe
- **IV aléatoire par fichier** — chaque fichier est chiffré différemment, même s'il est identique
- **Aucun stockage du mot de passe** — saisi à l'insertion du disque, jamais enregistré nulle part
- **Portable entre machines** — restaurez un disque chiffré sur n'importe quel PC avec le même mot de passe

#### Restaurer sans stress
- **Restauration intégrée** — vers n'importe quel dossier, sur n'importe quelle machine
- **Restauration sélective** — arborescence interactive avec cases à cocher (tri-state) pour ne restaurer que ce dont vous avez besoin
- **Déchiffrement transparent** — WinBack reconstruit vos fichiers en clair si vous fournissez le bon mot de passe

#### Partager et migrer
- **Export de profil** — sauvegardez votre configuration dans un fichier `.winback.json`
- **Import de profil** — restaurez une configuration sur un nouveau PC en quelques secondes
- **Plusieurs profils par disque** — un disque peut servir à sauvegarder plusieurs machines ou plusieurs ensembles de dossiers

#### Rester informé
- **Notifications Windows** — un ballon système à chaque fin de sauvegarde ; cliquez dessus pour ouvrir l'historique directement
- **Barre de progression dans la barre des tâches** — la progression de la sauvegarde est visible directement sur l'icône dans la barre des tâches Windows (verte en cours, jaune en pause), même si la fenêtre est fermée
- **Historique détaillé** — chaque exécution est enregistrée avec le nombre de fichiers traités, les erreurs, et la durée
- **Démarrage automatique** avec Windows — WinBack est prêt dès l'ouverture de session

---

### Télécharger

La page [Releases](../../releases/latest) contient `WinBack-X.Y.Z-Setup.exe`, prêt à l'emploi.
Aucun prérequis — .NET est embarqué dans l'installateur.

```
WinBack-X.Y.Z-Setup.exe → suivez l'assistant → c'est installé
```

---

### Pour les développeurs

#### Prérequis

- [.NET SDK 9.0+](https://dot.net/download)
- Windows 10/11 x64
- Visual Studio 2022, Rider, ou `dotnet` CLI

#### Compiler et lancer

```powershell
# Icônes placeholder (première fois)
cd WinBack.App\Resources\Icons && .\create_placeholder_icons.ps1

# Compiler
dotnet build WinBack.sln -c Release

# Lancer
dotnet run --project WinBack.App
```

> L'application démarre dans la barre système. Double-clic ou clic droit sur l'icône pour ouvrir l'interface.

#### Générer l'installateur

```powershell
.\build.ps1 -Installer   # Inno Setup téléchargé automatiquement si absent
```

| Commande | Fichier produit | Taille | Prérequis cible |
|---|---|---|---|
| `.\build.ps1` | `publish\WinBack.exe` | ~15 Mo | .NET 9 Runtime |
| `.\build.ps1 -SelfContained` | `publish\WinBack.exe` | ~90 Mo | Aucun |
| `.\build.ps1 -Installer` | `installer\output\WinBack-X.Y.Z-Setup.exe` | ~90 Mo | Aucun |

#### Architecture

```
WinBack.sln
├── WinBack.Core/          Bibliothèque métier (sans dépendance UI)
│   ├── Models/            Entités EF Core (profils, snapshots, historique)
│   ├── Data/              DbContext SQLite (EF Core 9, migrations manuelles)
│   └── Services/
│       ├── BackupEngine       Moteur de copie incrémentielle asynchrone
│       ├── RestoreEngine      Moteur de restauration (clair ou chiffré)
│       ├── DiffCalculator     Calcul des différences source / snapshot
│       ├── ProfileService     CRUD profils, export/import JSON
│       └── VssHelper          Gestion des snapshots VSS
│
├── WinBack.App/           Application WPF (.NET 9, MVVM)
│   ├── Services/
│   │   ├── UsbMonitorService    Surveillance WM_DEVICECHANGE
│   │   ├── BackupOrchestrator   Cycle complet : détection → pause → copie → notification
│   │   └── NotificationService  Ballons système (Shell_NotifyIcon)
│   ├── ViewModels/          CommunityToolkit.Mvvm
│   ├── Views/               WPF / XAML (thème Windows 11)
│   └── Resources/           Styles, icônes
│
└── WinBack.Tests/         Tests unitaires (xunit)
    ├── BackupPairTests, DiffCalculatorTests, BackupRunTests
    ├── BackupEngineEncryptionTests   AES-256 roundtrip
    └── AuditTests                    Intégrité (OK / manquant / corrompu)
```

#### Données utilisateur

```
%LOCALAPPDATA%\WinBack\winback.db   ← profils, snapshots, historique (SQLite)
```

#### Packages NuGet

| Package | Rôle |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite 9.x` | ORM + base de données |
| `Microsoft.Extensions.Hosting 9.x` | DI + services hébergés |
| `CommunityToolkit.Mvvm 8.x` | ObservableObject, RelayCommand |
| `H.NotifyIcon.Wpf 2.x` | Icône barre système |
| `System.Management 9.x` | WMI (identification disques, VSS) |

---

### Licence

MIT

---

# English

## WinBack — Automatic backup to external drives

Plug in your drive. WinBack handles the rest.

WinBack watches for external drive insertions and instantly backs up your folders —
no clicks, no schedules, no cloud. Your data stays on your hardware, encrypted
if you choose, and accessible without internet even when things go wrong.

### Why WinBack?

| | |
|---|---|
| **Zero friction** | Plug in the drive, the backup starts by itself. Unplug it, it stops cleanly. |
| **Incremental by design** | Only changed files are copied. A 500 GB backup can take seconds when little has changed. |
| **Your data, your hardware** | No subscription, no cloud, no data leaving your network. Optional AES-256 encryption in case the drive is lost. |

### How it works

1. **Configure once** — pick which folders to back up and to which drive (identified reliably by its Windows volume GUID, even if the drive letter changes)
2. **Plug in, it's backed up** — WinBack lives in the system tray and acts silently in the background the moment it detects the drive
3. **Restore in one click** — browse the backup tree, select what you need, choose a destination

### What WinBack can do

#### Back up intelligently
- **Incremental backup** — only added, modified, or deleted files are processed
- **VSS support** — copies open files (Outlook mail, databases, locked files…)
- **Three strategies** — strict mirror, recycle bin with configurable retention, or additive (never delete)
- **Exclusion filters** — glob patterns (`*.tmp`, `~$*`, `node_modules/**`…) to skip what doesn't matter
- **Global exclusions** — define a list of patterns in Settings that apply automatically to all profiles and all pairs, with a "Common suggestions" button to get started quickly
- **Automatic retry** — on copy error, WinBack retries before declaring failure
- **Backup preview** — see exactly what will change (X additions, Y modifications, Z deletions) without touching a single file

#### Stay in control
- **Pause / Resume** — interrupt a long backup and pick it back up exactly where it left off
- **Clean cancellation** — removing the drive during a backup marks it `Interrupted`, distinct from a manual cancel
- **Integrity audit** — verify at any time that your backed-up files are intact and uncorrupted
- **Post-copy verification** — optional SHA-256 hash to confirm every file is identical to its source

#### Secure and encrypt
- **Per-profile AES-256 encryption** — files on the drive are unreadable without your password
- **Random IV per file** — every file is encrypted differently, even if its content is identical
- **Password never stored** — entered at drive insertion, never written anywhere
- **Cross-machine portable** — restore an encrypted drive on any PC with the same password

#### Restore without stress
- **Built-in restore engine** — to any folder, on any machine
- **Selective restore** — interactive file tree with tri-state checkboxes to restore only what you need
- **Transparent decryption** — WinBack reconstructs your files in plain text if you provide the right password

#### Share and migrate
- **Profile export** — save your entire backup configuration to a `.winback.json` file
- **Profile import** — restore a configuration on a new PC in seconds
- **Multiple profiles per drive** — one drive can serve multiple machines or multiple sets of folders

#### Stay informed
- **Windows notifications** — a system balloon after every backup; click it to open the history directly
- **Taskbar progress bar** — backup progress is visible directly on the taskbar icon (green while running, yellow when paused), even when the window is closed
- **Detailed history** — every run is recorded with file counts, errors, and duration
- **Auto-start with Windows** — WinBack is ready the moment you log in

---

### Download

The [Releases](../../releases/latest) page has `WinBack-X.Y.Z-Setup.exe`, ready to use.
No prerequisites — .NET is bundled in the installer.

```
WinBack-X.Y.Z-Setup.exe → follow the wizard → done
```

---

### For developers

#### Requirements

- [.NET SDK 9.0+](https://dot.net/download)
- Windows 10/11 x64
- Visual Studio 2022, Rider, or `dotnet` CLI

#### Build and run

```powershell
# Placeholder icons (first time only)
cd WinBack.App\Resources\Icons && .\create_placeholder_icons.ps1

# Build
dotnet build WinBack.sln -c Release

# Run
dotnet run --project WinBack.App
```

> The app starts in the system tray. Double-click or right-click the icon to open the interface.

#### Generate the installer

```powershell
.\build.ps1 -Installer   # Inno Setup downloaded automatically if missing
```

| Command | Output | Size | Target requirements |
|---|---|---|---|
| `.\build.ps1` | `publish\WinBack.exe` | ~15 MB | .NET 9 Runtime |
| `.\build.ps1 -SelfContained` | `publish\WinBack.exe` | ~90 MB | None |
| `.\build.ps1 -Installer` | `installer\output\WinBack-X.Y.Z-Setup.exe` | ~90 MB | None |

#### Architecture

```
WinBack.sln
├── WinBack.Core/          Business library (no UI dependency)
│   ├── Models/            EF Core entities (profiles, snapshots, history)
│   ├── Data/              SQLite DbContext (EF Core 9, manual migrations)
│   └── Services/
│       ├── BackupEngine       Async incremental copy engine
│       ├── RestoreEngine      Restore engine (plain or encrypted)
│       ├── DiffCalculator     Source vs snapshot diff computation
│       ├── ProfileService     Profile CRUD, JSON export/import
│       └── VssHelper          VSS snapshot management
│
├── WinBack.App/           WPF application (.NET 9, MVVM)
│   ├── Services/
│   │   ├── UsbMonitorService    WM_DEVICECHANGE monitoring
│   │   ├── BackupOrchestrator   Full cycle: detect → pause → copy → notify
│   │   └── NotificationService  System balloons (Shell_NotifyIcon)
│   ├── ViewModels/          CommunityToolkit.Mvvm
│   ├── Views/               WPF / XAML (Windows 11 theme)
│   └── Resources/           Styles, icons
│
└── WinBack.Tests/         Unit tests (xunit)
    ├── BackupPairTests, DiffCalculatorTests, BackupRunTests
    ├── BackupEngineEncryptionTests   AES-256 roundtrip
    └── AuditTests                    Integrity (ok / missing / corrupted)
```

#### User data

```
%LOCALAPPDATA%\WinBack\winback.db   ← profiles, snapshots, history (SQLite)
```

#### NuGet packages

| Package | Purpose |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite 9.x` | ORM + database |
| `Microsoft.Extensions.Hosting 9.x` | DI + hosted services |
| `CommunityToolkit.Mvvm 8.x` | ObservableObject, RelayCommand |
| `H.NotifyIcon.Wpf 2.x` | System tray icon |
| `System.Management 9.x` | WMI (drive identification, VSS) |

---

### License

MIT
