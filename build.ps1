<#
.SYNOPSIS
    Script de build WinBack — génère l'exécutable Release et/ou l'installateur.

.DESCRIPTION
    1. Crée les icônes placeholder si elles n'existent pas
    2. Restaure les packages NuGet
    3. Compile en Release x64
    4. Publie un exécutable autonome dans ./publish/
    5. (optionnel) Compile l'installateur Inno Setup dans ./installer/output/

.EXAMPLE
    .\build.ps1
    .\build.ps1 -SelfContained    # Inclut le runtime .NET dans l'exe
    .\build.ps1 -Clean            # Nettoie avant de compiler
    .\build.ps1 -Installer        # Génère aussi le setup .exe (force -SelfContained)
    .\build.ps1 -Installer -Clean # Nettoyage complet + installateur
#>

param(
    [switch]$SelfContained,
    [switch]$Clean,
    [switch]$Installer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root     = $PSScriptRoot
$appProj  = "$root\WinBack.App\WinBack.App.csproj"
$publish  = "$root\publish"
$iconsDir = "$root\WinBack.App\Resources\Icons"
$issFile  = "$root\installer\winback.iss"

Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  WinBack — Build Script" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

# L'installateur doit embarquer le runtime pour ne pas avoir de prérequis
if ($Installer -and -not $SelfContained) {
    Write-Host "`n→ Mode installateur : activation de -SelfContained automatiquement" -ForegroundColor Yellow
    $SelfContained = $true
}

# ── 0. Icônes placeholder ─────────────────────────────────────────────────────
$requiredIcons = @("winback.ico","winback_tray.ico","winback_busy.ico","winback_error.ico")
$missingIcons  = @($requiredIcons | Where-Object { -not (Test-Path "$iconsDir\$_") })

if ($missingIcons.Count -gt 0) {
    Write-Host "`n→ Création des icônes placeholder…" -ForegroundColor Yellow
    & "$iconsDir\create_placeholder_icons.ps1"
}

# ── 1. Nettoyage ──────────────────────────────────────────────────────────────
if ($Clean) {
    Write-Host "`n→ Nettoyage…" -ForegroundColor Yellow
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    dotnet clean "$root\WinBack.sln" -c Release -nologo /v:q
}

# ── 2. Restauration ───────────────────────────────────────────────────────────
Write-Host "`n→ Restauration des packages NuGet…" -ForegroundColor Yellow
dotnet restore "$root\WinBack.sln" --nologo /v:q
if ($LASTEXITCODE -ne 0) { throw "Restauration échouée" }

# ── 3. Compilation ────────────────────────────────────────────────────────────
Write-Host "`n→ Compilation Release x64…" -ForegroundColor Yellow
dotnet build "$root\WinBack.sln" -c Release --nologo /v:m
if ($LASTEXITCODE -ne 0) { throw "Compilation échouée" }

# ── 4. Publication ────────────────────────────────────────────────────────────
Write-Host "`n→ Publication…" -ForegroundColor Yellow

$publishArgs = @(
    "publish", $appProj,
    "-c", "Release",
    "-a", "x64",
    "-o", $publish,
    "--nologo",
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true"
)

if ($SelfContained) {
    $publishArgs += "--self-contained"
    $publishArgs += "true"
    $publishArgs += "-r"
    $publishArgs += "win-x64"
    Write-Host "  Mode : autonome (runtime inclus)" -ForegroundColor Gray
} else {
    $publishArgs += "--no-self-contained"
    $publishArgs += "-r"
    $publishArgs += "win-x64"
    Write-Host "  Mode : nécessite .NET 9 Runtime installé" -ForegroundColor Gray
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publication échouée" }

# ── Résultat publish ──────────────────────────────────────────────────────────
$exePath = "$publish\WinBack.exe"
if (Test-Path $exePath) {
    $size = (Get-Item $exePath).Length / 1MB
    Write-Host "`n✓ Build réussi !" -ForegroundColor Green
    Write-Host "  Fichier  : $exePath" -ForegroundColor Green
    Write-Host "  Taille   : $([math]::Round($size, 1)) Mo" -ForegroundColor Green
} else {
    throw "Exécutable introuvable dans $publish"
}

# ── 5. Installateur Inno Setup (optionnel) ────────────────────────────────────
if ($Installer) {
    Write-Host "`n→ Compilation de l'installateur…" -ForegroundColor Yellow

    # ── Recherche de ISCC.exe ─────────────────────────────────────────────────
    # Ordre de priorité :
    #   1. Dossier local .tools\innosetup\ (cache du build, gitignorée)
    #   2. PATH et emplacements d'installation globale
    #   3. Téléchargement automatique depuis jrsoftware.org

    $isVersion  = "6.3.3"
    $toolsIsDir = "$root\.tools\innosetup"
    $isccLocal  = "$toolsIsDir\ISCC.exe"

    $isccCandidates = @(
        $isccLocal,
        "iscc.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    $iscc = $null
    foreach ($candidate in $isccCandidates) {
        if (Test-Path $candidate -ErrorAction SilentlyContinue) {
            $iscc = $candidate; break
        }
        if (Get-Command $candidate -ErrorAction SilentlyContinue) {
            $iscc = $candidate; break
        }
    }

    # ── Téléchargement automatique si introuvable ─────────────────────────────
    if (-not $iscc) {
        Write-Host "  Inno Setup introuvable — téléchargement automatique…" -ForegroundColor Yellow
        Write-Host "  Version : $isVersion  (https://jrsoftware.org)" -ForegroundColor DarkGray

        $isUrl       = "https://files.jrsoftware.org/is/$isVersion/innosetup-$isVersion.exe"
        $isInstaller = "$env:TEMP\innosetup-$isVersion.exe"

        try {
            Invoke-WebRequest -Uri $isUrl -OutFile $isInstaller -UseBasicParsing
        } catch {
            Write-Host ""
            Write-Host "  ✗ Téléchargement échoué : $_" -ForegroundColor Red
            Write-Host "    Installez Inno Setup manuellement : https://jrsoftware.org/isinfo.php" -ForegroundColor Red
            exit 1
        }

        Write-Host "  Installation dans .tools\innosetup\ (pas de droits admin requis)…" -ForegroundColor DarkGray
        New-Item -ItemType Directory -Force -Path $toolsIsDir | Out-Null

        # Installation silencieuse dans le dossier local du projet
        & $isInstaller /VERYSILENT /SP- /SUPPRESSMSGBOXES /NORESTART "/DIR=$toolsIsDir"
        if ($LASTEXITCODE -ne 0) { throw "Installation d'Inno Setup échouée" }

        Remove-Item $isInstaller -Force -ErrorAction SilentlyContinue

        if (Test-Path $isccLocal) {
            $iscc = $isccLocal
            Write-Host "  ✓ Inno Setup installé dans .tools\innosetup\" -ForegroundColor Green
        } else {
            throw "ISCC.exe introuvable après installation dans $toolsIsDir"
        }
    }

    Write-Host "  Compilateur : $iscc" -ForegroundColor DarkGray
    Write-Host "  Script      : $issFile" -ForegroundColor DarkGray

    & $iscc $issFile
    if ($LASTEXITCODE -ne 0) { throw "Compilation de l'installateur échouée" }

    $setupPath = "$root\installer\output\WinBack-0.1.0-Setup.exe"
    if (Test-Path $setupPath) {
        $setupSize = (Get-Item $setupPath).Length / 1MB
        Write-Host "`n✓ Installateur généré !" -ForegroundColor Green
        Write-Host "  Fichier  : $setupPath" -ForegroundColor Green
        Write-Host "  Taille   : $([math]::Round($setupSize, 1)) Mo" -ForegroundColor Green
    }
}
