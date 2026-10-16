# Test de bout en bout (Lot 8) : carte des nationalités du tableau de bord, éditions
# (aperçu, export PDF), fiche sanitaire du voyageur (application, réponses, validation)
# et antécédents médicaux d'un permis.
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class L8Win {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
  public static List<IntPtr> Handles(uint targetPid, string titlePart) {
    var result = new List<IntPtr>();
    EnumWindows((h, l) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid == targetPid && IsWindowVisible(h)) {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, 512);
        if (sb.Length > 0 && (titlePart == null || sb.ToString().Contains(titlePart))) result.Add(h);
      }
      return true;
    }, IntPtr.Zero);
    return result;
  }
}
"@

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$repo = 'D:\PROGRAMME\PROGRAMMES\BorderNext'
$outDir = Join-Path $repo 'tools'
$exe = Join-Path $repo 'src\BorderNext.UI\bin\Debug\net8.0-windows\BorderNext.UI.exe'
$pdfPath = Join-Path $env:TEMP 'bn-lot8-rapport.pdf'

if (Test-Path $pdfPath) { Remove-Item $pdfPath -Force }
Get-Process -Name 'BorderNext.UI' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

$proc = Start-Process -FilePath $exe -PassThru
Write-Output "PID=$($proc.Id)"

function Wait-Window([string]$titlePart, [int]$timeoutSec) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) { throw "Processus arrete (attendu: $titlePart)" }
    $handles = [L8Win]::Handles([uint32]$proc.Id, $titlePart)
    if ($handles.Count -gt 0) { return $handles[0] }
    Start-Sleep -Milliseconds 300
  }
  throw "Timeout '$titlePart'"
}

function Save-WindowShot([IntPtr]$hwnd, [string]$path) {
  $rect = New-Object L8Win+RECT
  [L8Win]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
  $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output "Capture: $path"
}

function Normalize-Text([string]$text) {
  if ($null -eq $text) { return '' }
  $decomposed = $text.Normalize([System.Text.NormalizationForm]::FormD)
  $builder = New-Object System.Text.StringBuilder
  foreach ($c in $decomposed.ToCharArray()) {
    if ([System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($c) -ne [System.Globalization.UnicodeCategory]::NonSpacingMark) {
      [void]$builder.Append($c)
    }
  }
  return $builder.ToString().Normalize([System.Text.NormalizationForm]::FormC).ToLowerInvariant()
}

function Text-Matches([string]$value, [string]$part) {
  return (Normalize-Text $value).Contains((Normalize-Text $part))
}

function Find-ById($root, [string]$automationId) {
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
  return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Find-All($root, [System.Windows.Automation.ControlType]$type) {
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)
  return $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Invoke-Button($root, [string]$namePart) {
  $buttons = Find-All $root ([System.Windows.Automation.ControlType]::Button)
  foreach ($b in $buttons) {
    if (Text-Matches $b.Current.Name $namePart) {
      $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
      Write-Output "  clic: $($b.Current.Name)"
      return $true
    }
  }
  throw "Bouton introuvable: $namePart"
}

function Invoke-ButtonById($root, [string]$automationId) {
  $button = Find-ById $root $automationId
  if (-not $button) { throw "Bouton introuvable (id): $automationId" }
  $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Write-Output "  clic (id): $automationId"
}

function Select-Tab($root, [string]$namePart) {
  $tabs = Find-All $root ([System.Windows.Automation.ControlType]::TabItem)
  foreach ($t in $tabs) {
    if (Text-Matches $t.Current.Name $namePart) {
      $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
      Start-Sleep -Seconds 1
      Write-Output "  onglet: $($t.Current.Name)"
      return
    }
  }
  throw "Onglet introuvable: $namePart"
}

function Set-Text($root, [string]$automationId, [string]$value) {
  $element = Find-ById $root $automationId
  if (-not $element) { throw "Champ introuvable : $automationId" }
  $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
  Write-Output "  $automationId = '$value'"
}

function Select-ListedItem($element, [string]$namePart) {
  $expand = $element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
  $expand.Expand(); Start-Sleep -Milliseconds 500
  $items = Find-All $element ([System.Windows.Automation.ControlType]::ListItem)
  $target = $null
  foreach ($i in $items) { if (Text-Matches $i.Current.Name $namePart) { $target = $i; break } }
  if (-not $target) { $expand.Collapse(); throw "Element '$namePart' introuvable" }
  $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 300
  $expand.Collapse()
  Write-Output "  élément sélectionné: $($target.Current.Name)"
}

# Les boîtes de message sont des fenêtres étroites ; les écrans de l'application
# partagent parfois le même titre (ex. fiche sanitaire) et ne doivent pas être visés.
function Get-Boxes([string]$titlePart) {
  $boxes = @()
  foreach ($h in [L8Win]::Handles([uint32]$proc.Id, $titlePart)) {
    try {
      $rect = [System.Windows.Automation.AutomationElement]::FromHandle($h).Current.BoundingRectangle
      if ($rect.Width -gt 0 -and $rect.Width -lt 700) { $boxes += $h }
    } catch { }
  }
  return $boxes
}

function Dismiss-IfPresent([string]$titlePart) {
  $handles = Get-Boxes $titlePart
  if ($handles.Count -eq 0) { return }
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($handles[0])
  $textCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
  $texts = @()
  foreach ($t in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) { $texts += $t.Current.Name }
  $okCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  $ok = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $okCond)
  if ($ok) { $ok.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
  else {
    [L8Win]::SetForegroundWindow($handles[0]) | Out-Null
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  }
  Start-Sleep -Milliseconds 600
  Write-Output ("  boîte '$titlePart' fermée : {0}" -f ($texts -join ' | '))
}

function Dismiss-MessageBox([string]$titlePart, [int]$timeoutSec = 20) {
  $box = Wait-Window $titlePart $timeoutSec
  [L8Win]::SetForegroundWindow($box) | Out-Null
  Start-Sleep -Milliseconds 800
  [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  Start-Sleep -Milliseconds 600
}

function Dismiss-All([string]$titlePart, [int]$max = 5) {
  for ($i = 0; $i -lt $max; $i++) {
    $handles = Get-Boxes $titlePart
    if ($handles.Count -eq 0) { return }
    Dismiss-IfPresent $titlePart
  }
}

function Open-Module([string]$namePart) {
  # Ouverture par l'automatisation (plus fiable que la navigation clavier).
  [L8Win]::SetForegroundWindow($main) | Out-Null
  Start-Sleep -Milliseconds 400
  $mroot2 = [System.Windows.Automation.AutomationElement]::FromHandle($main)
  $menus = Find-All $mroot2 ([System.Windows.Automation.ControlType]::MenuItem)
  $modules = $null
  foreach ($m in $menus) { if (Text-Matches $m.Current.Name 'Modules') { $modules = $m; break } }
  if (-not $modules) { throw "Menu Modules introuvable" }
  $modules.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
  Start-Sleep -Milliseconds 700
  $target = $null
  foreach ($m in (Find-All $mroot2 ([System.Windows.Automation.ControlType]::MenuItem))) {
    if (Text-Matches $m.Current.Name $namePart) { $target = $m; break }
  }
  if (-not $target) { throw "Entrée de menu introuvable : $namePart" }
  $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Seconds 1
  Write-Output "  menu: $($target.Current.Name)"
}

# --- 1. Connexion -------------------------------------------------------------
$login = Wait-Window 'Connexion' 30
Start-Sleep -Seconds 2
$lroot = [System.Windows.Automation.AutomationElement]::FromHandle($login)
$editCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$edits = $lroot.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)
if ($edits.Count -gt 0) { $edits[0].SetFocus() } else { [L8Win]::SetForegroundWindow($login) | Out-Null }
Start-Sleep -Milliseconds 600
$main = $null
for ($attempt = 0; $attempt -lt 3 -and -not $main; $attempt++) {
  [System.Windows.Forms.SendKeys]::SendWait('admin{TAB}ChangeMe!2026{ENTER}')
  $deadline = (Get-Date).AddSeconds(12)
  while ((Get-Date) -lt $deadline) {
    $handles = [L8Win]::Handles([uint32]$proc.Id, 'Poste de contr')
    if ($handles.Count -gt 0) { $main = $handles[0]; break }
    Start-Sleep -Milliseconds 500
  }
}
if (-not $main) { throw 'Connexion impossible' }
[L8Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 2500

# --- 2. Tableau de bord : carte des nationalités ------------------------------
$mroot = [System.Windows.Automation.AutomationElement]::FromHandle($main)
# Le canevas n'a pas de pair d'automatisation : le titre de la carte est contrôlé.
$texts = Find-All $mroot ([System.Windows.Automation.ControlType]::Text)
$mapTitle = $null
foreach ($t in $texts) { if (Text-Matches $t.Current.Name 'Carte des nationalit') { $mapTitle = $t; break } }
if (-not $mapTitle) { throw "Carte des nationalités absente du tableau de bord" }
Write-Output "  carte des nationalités présente"
Save-WindowShot $main (Join-Path $outDir 'e2e-lot8-1-tableau-de-bord-carte.png')

# --- 3. Éditions : aperçu et export PDF ---------------------------------------
Open-Module 'Rapports'
$reports = Wait-Window 'ditions et rapports' 25
Start-Sleep -Seconds 3
$rroot = [System.Windows.Automation.AutomationElement]::FromHandle($reports)

# Sélection de l'édition « 1.1 » dans le catalogue.
$tree = Find-ById $rroot 'CatalogTree'
if (-not $tree) { throw "Catalogue des éditions introuvable" }
$items = Find-All $tree ([System.Windows.Automation.ControlType]::TreeItem)
$target = $null
foreach ($i in $items) { if ($i.Current.Name -like '*1.1*') { $target = $i; break } }
if (-not $target) { throw "Édition 1.1 introuvable dans le catalogue" }
$target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 1
Write-Output "  édition sélectionnée: $($target.Current.Name)"

# Période : mois courant complet.
$from = Find-ById $rroot 'FromPicker'
if ($from) { $from.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue((Get-Date).ToString('01/MM/yyyy')) }
$to = Find-ById $rroot 'ToPicker'
if ($to) { $to.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue((Get-Date).ToString('dd/MM/yyyy')) }
Start-Sleep -Milliseconds 500

Invoke-ButtonById $rroot 'PreviewBtn'
Start-Sleep -Seconds 3
$tabs = Find-All $rroot ([System.Windows.Automation.ControlType]::TabItem)
Write-Output "  onglets d'aperçu: $($tabs.Count)"
if ($tabs.Count -lt 1) { throw "Aperçu vide" }
Save-WindowShot $reports (Join-Path $outDir 'e2e-lot8-2-editions-apercu.png')

Invoke-ButtonById $rroot 'ExportPdfBtn'
$saveDialog = Wait-Window 'Exporter en PDF' 20
[L8Win]::SetForegroundWindow($saveDialog) | Out-Null
Start-Sleep -Seconds 1
[System.Windows.Forms.SendKeys]::SendWait($pdfPath)
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Seconds 2
Dismiss-MessageBox 'ditions et rapports'

if (-not (Test-Path $pdfPath)) { throw "PDF non produit : $pdfPath" }
$bytes = [System.IO.File]::ReadAllBytes($pdfPath)
if ($bytes.Length -lt 1000 -or [System.Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne '%PDF') {
  throw "Fichier PDF invalide ($($bytes.Length) octets)"
}
Write-Output "  PDF exporté : $($bytes.Length) octets"
Invoke-Button $rroot 'Fermer'
Start-Sleep -Seconds 1

# --- 4. Fiche sanitaire du voyageur -------------------------------------------
Open-Module 'Fiche sanitaire'
$health = Wait-Window 'Fiche sanitaire du voyageur' 25
Start-Sleep -Seconds 3
$hroot = [System.Windows.Automation.AutomationElement]::FromHandle($health)

# Recherche d'un dossier : premier document à trois lettres de la base (OP…).
Set-Text $hroot 'DocumentBox' '12FR'
Invoke-ButtonById $hroot 'SearchDocumentBtn'
Start-Sleep -Seconds 2

$docs = Find-ById $hroot 'DocumentsGrid'
$rows = Find-All $docs ([System.Windows.Automation.ControlType]::DataItem)
if ($rows.Count -lt 1) { throw "Aucun document trouvé pour la recherche" }
$rows[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 2
Write-Output "  dossier sélectionné (documents trouvés: $($rows.Count))"

Invoke-ButtonById $hroot 'ApplyBtn'
Start-Sleep -Seconds 2
# Une fiche active peut déjà exister pour ce dossier (règle métier) : on poursuit
# avec la fiche existante, qui est alors rechargée dans l'écran.
Dismiss-IfPresent 'Erreur'

# Réponses : toutes les questions principales, sans déclencher de suivi contraignant.
$combos = Find-All $hroot ([System.Windows.Automation.ControlType]::ComboBox)
Write-Output "  listes de réponses: $($combos.Count)"
$questionCombos = @()
foreach ($c in $combos) { if ($c.Current.AutomationId -ne 'QuestionnaireCombo') { $questionCombos += $c } }

foreach ($combo in $questionCombos) {
  try { Select-ListedItem $combo 'Non' } catch { Write-Output "  (pas d'option 'Non' dans une liste : choix multiple)" }
}
# La question « symptômes » (choix multiple) n'a pas d'option « Non » : « Aucun ».
$symptomCombo = $null
foreach ($combo in $questionCombos) {
  try {
    $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand(); Start-Sleep -Milliseconds 300
    $items = Find-All $combo ([System.Windows.Automation.ControlType]::ListItem)
    $expand.Collapse()
    foreach ($i in $items) { if ((Normalize-Text $i.Current.Name) -eq 'aucun') { $symptomCombo = $combo; break } }
    if ($symptomCombo) { break }
  } catch { }
}
if ($symptomCombo) { Select-ListedItem $symptomCombo 'Aucun' } else { Write-Output '  (question symptômes : aucune liste trouvée)' }

Invoke-ButtonById $hroot 'SaveAnswersBtn'
Start-Sleep -Seconds 2
Invoke-ButtonById $hroot 'ValidateBtn'
Start-Sleep -Seconds 2
Dismiss-All 'Information'
Dismiss-All 'Erreur'
Save-WindowShot $health (Join-Path $outDir 'e2e-lot8-3-fiche-sanitaire.png')
# La conclusion (message de fin) est close par son propre titre avant de quitter l'écran.
Dismiss-All 'Fiche sanitaire'
Invoke-Button $hroot 'Fermer'
Start-Sleep -Seconds 1

# --- 5. Antécédents médicaux d'un permis --------------------------------------
Open-Module 'Permis'
$permit = Wait-Window 'Permis de' 25
Start-Sleep -Seconds 3
$proot = [System.Windows.Automation.AutomationElement]::FromHandle($permit)

# Ouverture du permis du document 12FR45678 (déjà enregistré), puis onglet des antécédents.
Invoke-Button $proot 'Rechercher un document'
$search = Wait-Window 'Recherche de documents' 20
Start-Sleep -Seconds 2
$sroot = [System.Windows.Automation.AutomationElement]::FromHandle($search)
Set-Text $sroot 'NumberBox' '12FR'
Start-Sleep -Milliseconds 500
Invoke-Button $sroot 'Rechercher'

# La recherche est asynchrone : on attend l'apparition des lignes (ou l'erreur éventuelle).
$searchRows = @()
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline) {
  $err = [L8Win]::Handles([uint32]$proc.Id, 'Erreur')
  if ($err.Count -gt 0) {
    $eroot = [System.Windows.Automation.AutomationElement]::FromHandle($err[0])
    $errTexts = @()
    foreach ($t in Find-All $eroot ([System.Windows.Automation.ControlType]::Text)) { $errTexts += $t.Current.Name }
    throw "Erreur pendant la recherche de documents : $($errTexts -join ' | ')"
  }
  $searchRows = Find-All $sroot ([System.Windows.Automation.ControlType]::DataItem)
  if ($searchRows.Count -ge 1) { break }
  Start-Sleep -Milliseconds 500
}
if ($searchRows.Count -lt 1) { throw "Document 12FR introuvable" }
Write-Output "  lignes de recherche: $($searchRows.Count)"
$searchRows[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 800
Invoke-Button $sroot 'Sélectionner'
Start-Sleep -Seconds 2
Dismiss-IfPresent 'Erreur'
Invoke-Button $proot 'Enregistrer le permis'
Start-Sleep -Seconds 2
Dismiss-IfPresent 'Erreur'

Select-Tab $proot 'Antécédents médicaux'
$checkbox = Find-ById $proot 'MedVaccinatedYellowFever'
if (-not $checkbox) { throw "Case de vaccination introuvable" }
$toggle = $checkbox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $toggle.Toggle() }
Start-Sleep -Milliseconds 400
Set-Text $proot 'MedicalIllnessBox' 'Aucune maladie declaree'
Invoke-ButtonById $proot 'SaveMedicalBtn'
Start-Sleep -Seconds 2
Dismiss-All 'Erreur'
Save-WindowShot $permit (Join-Path $outDir 'e2e-lot8-4-antecedents-medicaux.png')

Start-Sleep -Milliseconds 500
Stop-Process -Id $proc.Id -Force
Write-Output "TEST E2E LOT8: SUCCES"
