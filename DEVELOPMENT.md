# WinBack — Guide de développement

## Prérequis

| Outil | Version | Téléchargement |
|---|---|---|
| .NET SDK | 9.0+ | https://dot.net/download |
| Visual Studio | 2022 17.8+ | Community Edition gratuite |
| Windows | 10/11 x64 | — |

> **Note** : l'application doit être compilée et exécutée sous Windows (APIs spécifiques : WMI, WM_DEVICECHANGE, VSS, registre).

---

## Structure du projet

```
WinBack.sln
├── WinBack.Core/              Bibliothèque métier (sans UI)
│   ├── Models/                Entités EF Core
│   ├── Data/                  DbContext SQLite
│   └── Services/
│       ├── BackupEngine.cs    Moteur de sauvegarde incrémentielle
│       ├── DiffCalculator.cs  Calcul des différences source/snapshot
│       ├── DriveIdentifier.cs Identification disques par GUID
│       ├── ProfileService.cs  CRUD profils + historique
│       └── VssHelper.cs       Volume Shadow Copy Service
│
└── WinBack.App/               Application WPF
    ├── Services/
    │   ├── UsbMonitorService.cs    Surveillance WM_DEVICECHANGE
    │   ├── BackupOrchestrator.cs   Coordination backup complet
    │   └── NotificationService.cs  Notifications tray balloon
    ├── ViewModels/            MVVM (CommunityToolkit.Mvvm)
    ├── Views/                 Fenêtres WPF (XAML)
    ├── Converters/            IValueConverter pour XAML
    ├── Resources/
    │   ├── Styles.xaml        Thème Windows 11 (couleurs, boutons, cartes)
    │   └── Icons/             Icônes .ico (à fournir)
    ├── App.xaml(.cs)          Point d'entrée, DI, tray icon
    └── app.manifest           Droits admin requis (VSS)
```

---

## Première compilation

### 1. Créer les icônes placeholder
```powershell
cd WinBack.App\Resources\Icons
.\create_placeholder_icons.ps1
```

### 2. Restaurer les packages
```powershell
dotnet restore
```

### 3. Compiler
```powershell
dotnet build -c Debug
```

### 4. Lancer (depuis Visual Studio ou)
```powershell
cd WinBack.App
dotnet run
```

> L'application démarre dans la barre système. Clic droit sur l'icône pour ouvrir l'interface.

---

## Build Release (exécutable distributable)

```powershell
# Nécessite .NET 9 Runtime sur le PC cible
.\build.ps1

# Tout inclus, redistribuable sans installation .NET
.\build.ps1 -SelfContained
```

---

## Base de données

La base SQLite est créée automatiquement au premier démarrage :
```
%LOCALAPPDATA%\WinBack\winback.db
```

Pour inspecter la base en développement : [DB Browser for SQLite](https://sqlitebrowser.org/).

---

## Packages NuGet utilisés

| Package | Usage |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite 9.x` | ORM + base de données |
| `Microsoft.Extensions.Hosting 9.x` | DI + IHostedService |
| `CommunityToolkit.Mvvm 8.x` | ObservableObject, RelayCommand |
| `H.NotifyIcon.Wpf 2.x` | Icône système + balloon tips |
| `System.Management 9.x` | WMI (identification disques, VSS) |

---

## Flux de données

```
Insertion USB
    │
    ▼
UsbMonitorService (WM_DEVICECHANGE)
    │  driveLetter = "E"
    ▼
DriveIdentifier.GetDriveDetails("E:\")
    │  volumeGuid = "{xxxx-...}"
    ▼
BackupOrchestrator.OnDriveInsertedAsync(DriveDetails)
    │
    ├─ ProfileService.GetByVolumeGuidAsync(guid)
    │       ├─ Trouvé → BackupEngine.RunAsync(profile, destRoot)
    │       └─ Inconnu → Événement UnknownDriveInserted → UI MessageBox
    │
    ▼
BackupEngine.RunAsync()
    │
    ├─ Pour chaque BackupPair :
    │   ├─ DiffCalculator.Compute(source, snapshots) → Added/Modified/Deleted
    │   ├─ VssSessionManager.GetOrCreate(volume) → VssSnapshot
    │   ├─ Copie des fichiers ajoutés/modifiés (via VSS si dispo)
    │   ├─ Traitement suppressions (Mirror/RecycleBin/Additive)
    │   └─ Mise à jour FileSnapshot en base
    │
    └─ BackupRun sauvegardé → NotificationService.NotifyBackupComplete()
```

---

## Ajouter un logger fichier (production)

Installer Serilog :
```
dotnet add WinBack.App package Serilog.Extensions.Hosting
dotnet add WinBack.App package Serilog.Sinks.File
```

Dans `App.xaml.cs`, remplacer `.ConfigureLogging(...)` par :
```csharp
.UseSerilog((ctx, cfg) => cfg
    .MinimumLevel.Information()
    .WriteTo.File(
        Path.Combine(WinBackContext.GetDatabasePath().Replace("winback.db",""), "Logs", "winback-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30))
```

---

## Packaging (distribution)

### Option A : Inno Setup (installateur classique)
- Crée un setup.exe traditionnel
- Installe dans Program Files, crée raccourcis

### Option B : MSIX (Windows Store / sideload)
- Package moderne Windows
- Nécessite signature de code pour la distribution
- Avantage : mises à jour automatiques, désinstallation propre

Script Inno Setup minimal inclus dans `/installer/winback.iss` (à créer).
