# Test de bout en bout (Lot 5) : liste d'alerte personnes -> simulateur de correspondance
# -> enregistrement passeport (document lu) -> alarme -> personne douteuse -> décision du superviseur.
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class L5Win {
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
Start-Sleep -Seconds 6   # laisser le demarrage (migrations/semis) se stabiliser

function Wait-Window([string]$titlePart, [int]$timeoutSec) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) { throw "Processus arrete (attendu: $titlePart)" }
    $handles = [L5Win]::Handles([uint32]$proc.Id, $titlePart)
    if ($handles.Count -gt 0) { return $handles[0] }
    Start-Sleep -Milliseconds 300
  }
  throw "Timeout '$titlePart'"
}

function Save-WindowShot([IntPtr]$hwnd, [string]$path) {
  $rect = New-Object L5Win+RECT
  [L5Win]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
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

function Get-TextById($root, [string]$automationId) {
  $element = Find-ById $root $automationId
  if (-not $element) { return $null }
  return $element.Current.Name
}

# --- 1. Connexion puis ouverture de la surveillance ---------------------------
function Login-AsAdmin {
  for ($attempt = 1; $attempt -le 3; $attempt++) {
    $login = Wait-Window 'Connexion' 60
    [L5Win]::SetForegroundWindow($login) | Out-Null
    Start-Sleep -Milliseconds 1500
    [System.Windows.Forms.SendKeys]::SendWait('admin{TAB}ChangeMe!2026{ENTER}')
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline) {
      if ([L5Win]::Handles([uint32]$proc.Id, 'Poste de contr').Count -gt 0) { return }
      Start-Sleep -Milliseconds 400
    }
  }
  throw "Connexion impossible"
}
Login-AsAdmin
$main = Wait-Window 'Poste de contr' 20
[L5Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('%m'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('s'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
$watch = Wait-Window 'Surveillance' 20
Start-Sleep -Seconds 3
$wroot = [System.Windows.Automation.AutomationElement]::FromHandle($watch)

# --- 2. Contrôle automatique des douteux (réglage hérité CheckInDoubtablePersons)
Select-Tab $wroot 'douteuses'
$auto = Find-ById $wroot 'AutoCheckBox'
$toggle = $auto.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) {
  $toggle.Toggle(); Start-Sleep -Milliseconds 800
  Write-Output '  controle automatique des douteux active'
}

# --- 3. Entrée de liste d'alerte personnes (spécimen MBALA, passeport OP0321458)
Select-Tab $wroot 'Liste personnes'
Invoke-Button $wroot 'Nouvelle ent'
Start-Sleep -Milliseconds 500
Set-Text $wroot 'PersonSurnameEdit' 'MBALA'
Set-Text $wroot 'PersonGivenNameEdit' 'JEAN PIERRE'
Set-Text $wroot 'PersonPersonalNumberEdit' 'PN-0001'
Set-Text $wroot 'PersonDocumentNumberEdit' 'OP0321458'
Select-ComboByName $wroot 'PersonCitizenshipCombo' 'DEMOCRATIQUE'
Select-ComboByName $wroot 'PersonSourceCombo' 'Police'
Select-ComboByName $wroot 'PersonActionCombo' 'ZP100'
Invoke-Button $wroot 'Enregistrer'
Start-Sleep -Seconds 2
Save-WindowShot $watch (Join-Path $outDir 'e2e-lot5-1-liste-personnes.png')

# --- 4. Simulateur : type 2 (numéro de document exact) ------------------------
Select-Tab $wroot 'Simulateur'
Select-ComboByName $wroot 'SimTypeCombo' 'Num'
Set-Text $wroot 'SimDocument' 'OP0321458'
Select-ComboByName $wroot 'SimCitizenship' 'DEMOCRATIQUE'
Invoke-Button $wroot 'Simuler'
Start-Sleep -Seconds 2
$simStatus = Get-TextById $wroot 'StatusText'
Write-Output "  simulateur: $simStatus"
if ($simStatus -notlike '*1 correspondance*') { throw "Simulateur: resultat inattendu ($simStatus)" }
Save-WindowShot $watch (Join-Path $outDir 'e2e-lot5-2-simulateur.png')

# --- 5. Enregistrement passeport : alerte + personne douteuse -----------------
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1
[L5Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('{F5}')
$passport = Wait-Window 'Enregistrement passeport' 20
Start-Sleep -Seconds 2
$proot = [System.Windows.Automation.AutomationElement]::FromHandle($passport)

Invoke-Button $proot 'Lire le document'
Start-Sleep -Seconds 2
Invoke-Button $proot 'Enregistrer'
Start-Sleep -Seconds 3

# Alarme de liste d'alerte : boîte modale à valider
$alarm = Wait-Window 'ALERTE LISTE' 20
[L5Win]::SetForegroundWindow($alarm) | Out-Null
Start-Sleep -Milliseconds 1200
Save-WindowShot $alarm (Join-Path $outDir 'e2e-lot5-3-alarme.png')
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Seconds 2
$doubtStatus = Get-TextById $proot 'StatusText'
Write-Output "  enregistrement: $doubtStatus"
if ($doubtStatus -notlike '*douteuse*') { throw "Personne douteuse non inscrite ($doubtStatus)" }
Save-WindowShot $passport (Join-Path $outDir 'e2e-lot5-4-enregistre.png')
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

# --- 6. Seconde inspection : décision du superviseur --------------------------
[L5Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('%m'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('s'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
$watch2 = Wait-Window 'Surveillance' 20
Start-Sleep -Seconds 3
$w2root = [System.Windows.Automation.AutomationElement]::FromHandle($watch2)

Select-Tab $w2root 'douteuses'
Invoke-Button $w2root 'En attente seulement'
Start-Sleep -Seconds 2
Select-FirstRow $w2root
Select-ComboByName $w2root 'DoubtActionCombo' 'Refuse'
Set-Text $w2root 'SupervisorNoteBox' 'Correspondance liste d''alerte confirmee - refus en seconde inspection (E2E)'
Save-WindowShot $watch2 (Join-Path $outDir 'e2e-lot5-5-douteux.png')
Invoke-Button $w2root 'Traiter'
Start-Sleep -Seconds 2
Save-WindowShot $watch2 (Join-Path $outDir 'e2e-lot5-6-superviseur.png')

Start-Sleep -Seconds 1
Stop-Process -Id $proc.Id -Force
Write-Output "TEST E2E LOT5: SUCCES"
