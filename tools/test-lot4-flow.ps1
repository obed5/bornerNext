# Test de bout en bout (Lot 4) : refus d'entrée -> base légale -> ordre de reconduite -> amende,
# puis création d'un permis depuis le document lu par le lecteur simulé.
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class L4Win {
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

function Wait-Window([string]$titlePart, [int]$timeoutSec) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) { throw "Processus arrete (attendu: $titlePart)" }
    $handles = [L4Win]::Handles([uint32]$proc.Id, $titlePart)
    if ($handles.Count -gt 0) { return $handles[0] }
    Start-Sleep -Milliseconds 300
  }
  throw "Timeout '$titlePart'"
}

function Save-WindowShot([IntPtr]$hwnd, [string]$path) {
  $rect = New-Object L4Win+RECT
  [L4Win]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
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

function Select-FirstRow($root) {
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::DataItem)
  $rows = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  if ($rows.Count -eq 0) { throw "Aucune ligne de donnees" }
  $rows[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 800
  Write-Output "  ligne selectionnee"
}

function Set-Text($root, [string]$automationId, [string]$value) {
  $element = Find-ById $root $automationId
  if (-not $element) { throw "Champ introuvable : $automationId" }
  $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
  Write-Output "  $automationId = '$value'"
}

# --- 1. Connexion + refus d'entrée au formulaire passeport ---------------------
function Login-AsAdmin {
  for ($attempt = 1; $attempt -le 3; $attempt++) {
    $login = Wait-Window 'Connexion' 30
    [L4Win]::SetForegroundWindow($login) | Out-Null
    Start-Sleep -Milliseconds 1500
    [System.Windows.Forms.SendKeys]::SendWait('admin{TAB}ChangeMe!2026{ENTER}')
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline) {
      if ([L4Win]::Handles([uint32]$proc.Id, 'Poste de contr').Count -gt 0) { return }
      Start-Sleep -Milliseconds 400
    }
  }
  throw "Connexion impossible"
}
Login-AsAdmin
$main = Wait-Window 'Poste de contr' 20
[L4Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('{F5}')
$passport = Wait-Window 'Enregistrement passeport' 20
Start-Sleep -Seconds 2
$proot = [System.Windows.Automation.AutomationElement]::FromHandle($passport)

Invoke-Button $proot 'Lire le document'
Start-Sleep -Seconds 2
# Refus d'entrée : décoche « Passage accordé »
$granted = Find-ById $proot 'GrantedCheck'
$toggle = $granted.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off) { $toggle.Toggle() }
Write-Output "  Passage accorde -> decoche (refus)"
Start-Sleep -Milliseconds 500
Invoke-Button $proot 'Enregistrer'
Start-Sleep -Seconds 3
Save-WindowShot $passport (Join-Path $outDir 'e2e-lot4-1-refus.png')
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

# --- 2. Écran Refus & sanctions : base légale, reconduite, amende --------------
[L4Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('%m'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('r'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
$enforcement = Wait-Window 'Refus et sanctions' 20
Start-Sleep -Seconds 3
$eroot = [System.Windows.Automation.AutomationElement]::FromHandle($enforcement)
Select-FirstRow $eroot

Select-ComboByName $eroot 'JustificationCombo' 'Has no valid visa'
Invoke-Button $eroot 'Appliquer la base'
Start-Sleep -Seconds 2

# Ordre de reconduite : dialogue d'information puis création
Invoke-Button $eroot 'Ordre de reconduite'
$input = Wait-Window 'Information' 20
Start-Sleep -Milliseconds 800
$iroot = [System.Windows.Automation.AutomationElement]::FromHandle($input)
Set-Text $iroot 'ValueBox' 'Refus d''entree : absence de visa valide (base legale appliquee)'
Invoke-Button $iroot 'OK'
Start-Sleep -Seconds 2

# Amende : motif + montant + durée
Invoke-Button $eroot 'Amende'
$penalty = Wait-Window 'Nouvelle amende' 20
Start-Sleep -Milliseconds 800
$penroot = [System.Windows.Automation.AutomationElement]::FromHandle($penalty)
Set-Text $penroot 'AmountBox' '250'
Set-Text $penroot 'DurationBox' '15'
Invoke-Button $penroot 'Enregistrer'
Start-Sleep -Seconds 2

Save-WindowShot $enforcement (Join-Path $outDir 'e2e-lot4-2-refus-sanctions.png')
Select-Tab $eroot 'Ordres de reconduite'
Save-WindowShot $enforcement (Join-Path $outDir 'e2e-lot4-3-reconduites.png')
Select-Tab $eroot 'Amendes'
Save-WindowShot $enforcement (Join-Path $outDir 'e2e-lot4-4-amendes.png')
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

# --- 3. Permis depuis le document lu ------------------------------------------
[L4Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('%m'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('p'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
$permits = Wait-Window 'Permis de' 20
Start-Sleep -Seconds 3
$pemroot = [System.Windows.Automation.AutomationElement]::FromHandle($permits)

Invoke-Button $pemroot 'Rechercher un document'
$search = Wait-Window 'Recherche de documents' 20
Start-Sleep -Seconds 1
$sroot = [System.Windows.Automation.AutomationElement]::FromHandle($search)
Set-Text $sroot 'NumberBox' '12FR'   # critere obligatoire (garde-fou anti-extraction)
Invoke-Button $sroot 'Rechercher'
Start-Sleep -Seconds 2
Select-FirstRow $sroot
Invoke-Button $sroot 'Sélectionner'
Start-Sleep -Seconds 2

Invoke-Button $pemroot 'Enregistrer le permis'
Start-Sleep -Seconds 3
Save-WindowShot $permits (Join-Path $outDir 'e2e-lot4-5-permis.png')

Start-Sleep -Seconds 1
Stop-Process -Id $proc.Id -Force
Write-Output "TEST E2E LOT4: SUCCES"
