# Test de bout en bout (Lot 6) : référentiels transport -> création d'un vol (AF100 ORY->FIH)
# -> enregistrement d'un passager sur le passage ouvert -> réaffectation -> fermeture.
$ErrorActionPreference = 'Stop'

# Les instances residuelles volent le focus clavier : on les ferme avant de lancer.
Get-Process BorderNext.UI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class L6Win {
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

$outDir = 'D:\PROGRAMME\PROGRAMMES\BorderNext\tools'
$exe = 'D:\PROGRAMME\PROGRAMMES\BorderNext\src\BorderNext.UI\bin\Debug\net8.0-windows\BorderNext.UI.exe'
$proc = Start-Process -FilePath $exe -PassThru
Write-Output "PID=$($proc.Id)"
Start-Sleep -Seconds 6

function Wait-Window([string]$titlePart, [int]$timeoutSec) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) { throw "Processus arrete (attendu: $titlePart)" }
    $handles = [L6Win]::Handles([uint32]$proc.Id, $titlePart)
    if ($handles.Count -gt 0) { return $handles[0] }
    Start-Sleep -Milliseconds 300
  }
  throw "Timeout '$titlePart'"
}

function Save-WindowShot([IntPtr]$hwnd, [string]$path) {
  $rect = New-Object L6Win+RECT
  [L6Win]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
  $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output "Capture: $path"
}

function Find-ById($root, [string]$automationId) {
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
  return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Invoke-Button($root, [string]$namePart) {
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
  $buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  foreach ($b in $buttons) {
    if ($b.Current.Name -like "*$namePart*") {
      $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
      Write-Output "  clic: $($b.Current.Name)"
      return $true
    }
  }
  throw "Bouton introuvable: $namePart"
}

function Select-ComboByName($root, [string]$automationId, [string]$itemNamePart) {
  $combo = Find-ById $root $automationId
  if (-not $combo) { throw "Combo introuvable : $automationId" }
  $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
  $expand.Expand(); Start-Sleep -Milliseconds 600
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::ListItem)
  $items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  $target = $null
  foreach ($i in $items) { if ($i.Current.Name -like "*$itemNamePart*") { $target = $i; break } }
  if (-not $target) { $expand.Collapse(); throw "Element '$itemNamePart' introuvable dans $automationId" }
  $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 300
  $expand.Collapse()
  Write-Output "  $automationId -> '$itemNamePart'"
}

function Select-Tab($root, [string]$namePart) {
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::TabItem)
  $tabs = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  foreach ($t in $tabs) {
    if ($t.Current.Name -like "*$namePart*") {
      $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
      Start-Sleep -Seconds 1
      Write-Output "  onglet: $($t.Current.Name)"
      return
    }
  }
  throw "Onglet introuvable: $namePart"
}

function Invoke-ButtonById($root, [string]$automationId) {
  $element = Find-ById $root $automationId
  if (-not $element) { throw "Bouton introuvable (id) : $automationId" }
  $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Write-Output "  clic (id): $automationId"
}

function Select-RowByText($root, [string]$automationId, [string]$textPart) {
  $grid = Find-ById $root $automationId
  if (-not $grid) { throw "Grille introuvable : $automationId" }
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::DataItem)
  $rows = $grid.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  $textCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Text)
  foreach ($row in $rows) {
    $texts = $row.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)
    foreach ($t in $texts) {
      if ($t.Current.Name -like "*$textPart*") {
        $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 800
        Write-Output "  ligne '$textPart' selectionnee dans $automationId"
        return
      }
    }
  }
  throw "Ligne '$textPart' introuvable dans $automationId"
}

function Select-FirstRowIn($root, [string]$automationId) {
  $grid = Find-ById $root $automationId
  if (-not $grid) { throw "Grille introuvable : $automationId" }
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::DataItem)
  $rows = $grid.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  if ($rows.Count -eq 0) { throw "Aucune ligne de donnees dans $automationId" }
  $rows[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 800
  Write-Output "  ligne selectionnee dans $automationId"
}

function Set-Text($root, [string]$automationId, [string]$value) {
  $element = Find-ById $root $automationId
  if (-not $element) { throw "Champ introuvable : $automationId" }
  $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
  Write-Output "  $automationId = '$value'"
}

function Get-TextById($root, [string]$automationId) {
  $element = Find-ById $root $automationId
  if (-not $element) { return $null }
  return $element.Current.Name
}

function Login-AsAdmin {
  for ($attempt = 1; $attempt -le 3; $attempt++) {
    $login = Wait-Window 'Connexion' 60
    [L6Win]::SetForegroundWindow($login) | Out-Null
    Start-Sleep -Milliseconds 1500
    [System.Windows.Forms.SendKeys]::SendWait('admin{TAB}ChangeMe!2026{ENTER}')
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline) {
      if ([L6Win]::Handles([uint32]$proc.Id, 'Poste de contr').Count -gt 0) { return }
      Start-Sleep -Milliseconds 400
    }
  }
  throw "Connexion impossible"
}

Login-AsAdmin
$main = Wait-Window 'Poste de contr' 20
[L6Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600

# --- 1. Ouverture des opérations frontière (Alt+M, t) -------------------------
[System.Windows.Forms.SendKeys]::SendWait('%m'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('t'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
$ops = Wait-Window 'Voyages et transports' 20
Start-Sleep -Seconds 3
$oroot = [System.Windows.Automation.AutomationElement]::FromHandle($ops)

# --- 2. Création du vol AF100 (ORY -> FIH, arrivée) ---------------------------
Invoke-Button $oroot 'Nouveau voyage'
Start-Sleep -Milliseconds 500
Set-Text $oroot 'TripNameEdit' 'AF100'
Select-ComboByName $oroot 'TripFromTerminal' 'ORY'
Select-ComboByName $oroot 'TripToTerminal' 'FIH'
Select-ComboByName $oroot 'TripCarrierEdit' 'Air France'
Set-Text $oroot 'TripScheduledEdit' '14:30'
Set-Text $oroot 'TripExternalEdit' 'AF0100'
Set-Text $oroot 'TripDescriptionEdit' 'Paris — Kinshasa (E2E)'
Invoke-Button $oroot 'Enregistrer'
Start-Sleep -Seconds 2
Save-WindowShot $ops (Join-Path $outDir 'e2e-lot6-1-voyage.png')

# Le passage doit apparaître ouvert dans la liste
Select-FirstRowIn $oroot 'TripsGrid'
$openLabel = Get-TextById $oroot 'OpenTripBtn'
Write-Output "  bouton ouverture: $openLabel"
if ($openLabel -notlike '*Fermer*') { throw "Passage non ouvert ($openLabel)" }

# --- 3. Second vol pour la réaffectation (XX200 FIH -> ORY, départ) -----------
Invoke-Button $oroot 'Nouveau voyage'
Start-Sleep -Milliseconds 500
Set-Text $oroot 'TripNameEdit' 'XX200'
Select-ComboByName $oroot 'TripDirectionEdit' 'Départ'
Select-ComboByName $oroot 'TripFromTerminal' 'FIH'
Select-ComboByName $oroot 'TripToTerminal' 'ORY'
Select-ComboByName $oroot 'TripCarrierEdit' 'Congo Airways'
Invoke-Button $oroot 'Enregistrer'
Start-Sleep -Seconds 2

# --- 4. Enregistrement d'un passager sur le passage AF100 --------------------
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1
[L6Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('{F5}')
$passport = Wait-Window 'Enregistrement passeport' 20
Start-Sleep -Seconds 2
$proot = [System.Windows.Automation.AutomationElement]::FromHandle($passport)

Invoke-Button $proot 'Lire le document'
Start-Sleep -Seconds 2
Select-ComboByName $proot 'TripCombo' 'AF100'
Invoke-Button $proot 'Enregistrer'
Start-Sleep -Seconds 3
$saved = Get-TextById $proot 'StatusText'
Write-Output "  enregistrement: $saved"
if ($saved -notlike '*Enregistrement effectu*') { throw "Enregistrement du passager echoue ($saved)" }
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

# --- 5. Contrôle du passager puis réaffectation vers XX200 --------------------
[L6Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('%m'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('t'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
$ops2 = Wait-Window 'Voyages et transports' 20
Start-Sleep -Seconds 3
$o2root = [System.Windows.Automation.AutomationElement]::FromHandle($ops2)

Invoke-Button $o2root 'Rechercher'
Start-Sleep -Seconds 2
# Sélectionner le passage AF100 (la grille est triée du plus récent au plus ancien)
Select-RowByText $o2root 'TripsGrid' 'AF100'
Save-WindowShot $ops2 (Join-Path $outDir 'e2e-lot6-2-passagers.png')

Select-FirstRowIn $o2root 'PassengersGrid'
Select-ComboByName $o2root 'ReassignTarget' 'XX200'
Invoke-Button $o2root 'Réaffecter'
Start-Sleep -Seconds 2
$reassigned = Get-TextById $o2root 'StatusText'
Write-Output "  reaffectation: $reassigned"
Save-WindowShot $ops2 (Join-Path $outDir 'e2e-lot6-3-reaffectation.png')

# --- 6. Navires, transporteurs, terminaux ------------------------------------
Select-Tab $o2root 'Navires'
Invoke-Button $o2root 'Nouvelle ent'
Start-Sleep -Milliseconds 500
Set-Text $o2root 'VesselNameEdit' 'Shamis E2E'
Set-Text $o2root 'VesselRegistrationEdit' 'QW12-E2E'
Select-ComboByName $o2root 'VesselTypeEdit' 'Bac'
Select-ComboByName $o2root 'VesselNationalityEdit' 'DEMOCRATIQUE'
Invoke-Button $o2root 'Enregistrer'
Start-Sleep -Seconds 2
Save-WindowShot $ops2 (Join-Path $outDir 'e2e-lot6-4-navire.png')

Select-Tab $o2root 'Transporteurs'
Set-Text $o2root 'CarrierSearch' 'Air France'
Invoke-Button $o2root 'Rechercher'
Start-Sleep -Seconds 2
Save-WindowShot $ops2 (Join-Path $outDir 'e2e-lot6-5-transporteurs.png')

Select-Tab $o2root 'Terminaux'
Set-Text $o2root 'TerminalSearch' 'NDjili'
Invoke-Button $o2root 'Rechercher'
Start-Sleep -Seconds 2
Save-WindowShot $ops2 (Join-Path $outDir 'e2e-lot6-6-terminaux.png')

# --- 7. Fermeture du passage AF100 -------------------------------------------
Select-Tab $o2root 'Voyages'
Invoke-Button $o2root 'Rechercher'
Start-Sleep -Seconds 2
Select-RowByText $o2root 'TripsGrid' 'AF100'
Invoke-ButtonById $o2root 'OpenTripBtn'
Start-Sleep -Seconds 2
Save-WindowShot $ops2 (Join-Path $outDir 'e2e-lot6-7-fermeture.png')

Start-Sleep -Seconds 1
Stop-Process -Id $proc.Id -Force
Write-Output "TEST E2E LOT6: SUCCES"
