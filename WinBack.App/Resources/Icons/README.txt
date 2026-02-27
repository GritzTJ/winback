Icônes requises pour la compilation :

  winback.ico        — Icône principale de l'application (fenêtre + Alt+Tab)
  winback_tray.ico   — Icône barre système : état normal (gris/blanc)
  winback_busy.ico   — Icône barre système : sauvegarde en cours (bleu animé)
  winback_error.ico  — Icône barre système : erreur (rouge)

Format requis : .ico multi-résolution (16x16, 32x32, 48x48, 256x256)

Outils recommandés :
  - IcoFX (Windows)
  - https://realfavicongenerator.net (conversion PNG → ICO)
  - Inkscape (création SVG puis export ICO)

En attendant les vraies icônes, placez des fichiers .ico de substitution
(même un fichier ICO vide 16x16 suffit pour compiler).

Pour créer une icône vide rapidement via PowerShell :
  Add-Type -AssemblyName System.Drawing
  $bmp = New-Object System.Drawing.Bitmap(16,16)
  $ico = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
  $fs = [System.IO.File]::OpenWrite("winback_tray.ico")
  $ico.Save($fs)
  $fs.Close()
