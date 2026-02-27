#Requires -Version 5.1
<#
.SYNOPSIS
    Crée des icônes placeholder pour WinBack (développement local uniquement).

.DESCRIPTION
    Génère 4 fichiers .ico 32x32 colorés dans le dossier courant.
    Ces fichiers sont ignorés par Git (.gitignore) — ils ne sont pas destinés
    à la distribution finale.

    À exécuter une seule fois avant la première compilation :
        cd WinBack.App\Resources\Icons
        .\create_placeholder_icons.ps1

.NOTES
    Utilise System.Drawing (.NET Framework / .NET 9 sur Windows).
    Nécessite Windows (GDI+).
#>

Add-Type -AssemblyName System.Drawing

$dir = $PSScriptRoot

# Icône  →  (couleur de fond,   lettre affichée)
$icons = @(
    [pscustomobject]@{ File = "winback.ico";       R = 0;   G = 120; B = 212; Label = "W" }  # bleu accent
    [pscustomobject]@{ File = "winback_tray.ico";  R = 80;  G = 80;  B = 80;  Label = "W" }  # gris neutre
    [pscustomobject]@{ File = "winback_busy.ico";  R = 255; G = 140; B = 0;   Label = "W" }  # orange actif
    [pscustomobject]@{ File = "winback_error.ico"; R = 197; G = 15;  B = 31;  Label = "!" }  # rouge erreur
)

foreach ($ico in $icons) {
    $path = Join-Path $dir $ico.File

    if (Test-Path $path) {
        Write-Host "  Existe déjà : $($ico.File)" -ForegroundColor DarkGray
        continue
    }

    $size  = 32
    $bmp   = New-Object System.Drawing.Bitmap($size, $size)
    $g     = [System.Drawing.Graphics]::FromImage($bmp)

    try {
        $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

        # Fond coloré
        $bg = [System.Drawing.Color]::FromArgb($ico.R, $ico.G, $ico.B)
        $g.Clear($bg)

        # Lettre centrée
        $font  = New-Object System.Drawing.Font("Segoe UI", 17, [System.Drawing.FontStyle]::Bold)
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $sf    = New-Object System.Drawing.StringFormat
        $sf.Alignment     = [System.Drawing.StringAlignment]::Center
        $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
        $rect  = [System.Drawing.RectangleF]::new(0, 0, $size, $size)
        $g.DrawString($ico.Label, $font, $brush, $rect, $sf)
        $font.Dispose()
        $brush.Dispose()
    } finally {
        $g.Dispose()
    }

    try {
        $hIcon = $bmp.GetHicon()
        $icon  = [System.Drawing.Icon]::FromHandle($hIcon)
        $stream = [System.IO.FileStream]::new($path, [System.IO.FileMode]::Create)
        try {
            $icon.Save($stream)
        } finally {
            $stream.Dispose()
        }
        $icon.Dispose()
    } finally {
        $bmp.Dispose()
    }

    Write-Host "  Créé : $($ico.File)" -ForegroundColor Green
}

Write-Host ""
Write-Host "Icônes placeholder prêtes." -ForegroundColor Cyan
Write-Host "Remplacez-les par de vraies icônes avant la distribution." -ForegroundColor DarkGray
