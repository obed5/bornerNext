# Test de bout en bout (Lot 3) : biométrie et appareils simulés.
# 1) lecture de document (lecteur simulé) + capture photo (caméra simulée) + enregistrement
# 2) capture d'une empreinte voyageur (dialogue capteur, spécimen SIM-1)
# 3) enrôlement biométrique de l'opérateur admin
# 4) déconnexion puis reconnexion PAR EMPREINTE (contrôle hérité CheckFingerAgainstUsers)
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class L3Win {
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
    $handles = [L3Win]::Handles([uint32]$proc.Id, $titlePart)
    if ($handles.Count -gt 0) { return $handles[0] }
    Start-Sleep -Milliseconds 300
  }
  throw "Timeout '$titlePart'"
}

function Save-WindowShot([IntPtr]$hwnd, [string]$path) {
  $rect = New-Object L3Win+RECT
  [L3Win]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
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
      Start-Sleep -Seconds 2
      Write-Output "  onglet: $($t.Current.Name)"
      return
    }
  }
  throw "Onglet introuvable: $namePart"
}

# --- 1. Connexion et enregistrement passeport ----------------------------------
function Login-AsAdmin {
  for ($attempt = 1; $attempt -le 3; $attempt++) {
    $login = Wait-Window 'Connexion' 30
    [L3Win]::SetForegroundWindow($login) | Out-Null
    Start-Sleep -Milliseconds 1500
    [System.Windows.Forms.SendKeys]::SendWait('admin{TAB}ChangeMe!2026{ENTER}')
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline) {
      if ([L3Win]::Handles([uint32]$proc.Id, 'Poste de contr').Count -gt 0) { return }
      Start-Sleep -Milliseconds 400
    }
    Write-Output "  tentative de connexion $attempt sans effet, nouvelle tentative"
  }
  throw "Connexion impossible apres 3 tentatives"
}

Login-AsAdmin
$main = Wait-Window 'Poste de contr' 20
[L3Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('{F5}')
$passport = Wait-Window 'Enregistrement passeport' 20
Start-Sleep -Seconds 2
Write-Output "OK fenetres principales"

# --- 2. Lecteur de documents simulé (spécimen MBALA Jean) ----------------------
$proot = [System.Windows.Automation.AutomationElement]::FromHandle($passport)
Invoke-Button $proot 'Lire le document'
Start-Sleep -Seconds 2
Invoke-Button $proot 'Capturer la photo'
Start-Sleep -Seconds 2
Save-WindowShot $passport (Join-Path $outDir 'e2e-lot3-1-lecture-photo.png')

# --- 3. Enregistrement du mouvement (document + photo persistés) ---------------
Invoke-Button $proot 'Enregistrer'
Start-Sleep -Seconds 3
Save-WindowShot $passport (Join-Path $outDir 'e2e-lot3-2-enregistre.png')

# --- 4. Capture d'une empreinte du voyageur ------------------------------------
Invoke-Button $proot 'Empreintes'
$fingerprints = Wait-Window 'Empreintes digitales' 20
Start-Sleep -Seconds 2
$froot = [System.Windows.Automation.AutomationElement]::FromHandle($fingerprints)

# Sélectionne la première ligne (doigt 1) du tableau des empreintes
$fpRowCond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::DataItem)
$fpRows = $froot.FindAll([System.Windows.Automation.TreeScope]::Descendants, $fpRowCond)
if ($fpRows.Count -gt 0) { $fpRows[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
Start-Sleep -Seconds 1

Invoke-Button $froot 'Capturer'
$scan = Wait-Window 'Capture d' 20
Start-Sleep -Seconds 1
$sroot = [System.Windows.Automation.AutomationElement]::FromHandle($scan)
Select-ComboByName $sroot 'IdentityCombo' 'SIM-1'
Invoke-Button $sroot 'Capturer'
Start-Sleep -Seconds 2
Save-WindowShot $scan (Join-Path $outDir 'e2e-lot3-3-capture-empreinte.png')
Invoke-Button $sroot 'Sélectionner'
Start-Sleep -Seconds 2
Save-WindowShot $fingerprints (Join-Path $outDir 'e2e-lot3-4-empreintes.png')
Invoke-Button $froot 'Fermer'
Start-Sleep -Seconds 1

# --- 5. Enrôlement biométrique de l'opérateur admin ----------------------------
[L3Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('%m'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('a'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
$admin = Wait-Window 'Administration' 20
Start-Sleep -Seconds 3
$aroot = [System.Windows.Automation.AutomationElement]::FromHandle($admin)
Select-Tab $aroot 'Utilisateurs'

# Sélectionne la première ligne d'utilisateur (ADM001)
$rowCond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::DataItem)
$rows = $aroot.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rowCond)
if ($rows.Count -eq 0) { throw "Aucune ligne utilisateur" }
$rows[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 2

Invoke-Button $aroot 'Enrôler empreinte 1'
$scan2 = Wait-Window 'Capture d' 20
Start-Sleep -Seconds 1
$s2root = [System.Windows.Automation.AutomationElement]::FromHandle($scan2)
Select-ComboByName $s2root 'IdentityCombo' 'SIM-1'
Invoke-Button $s2root 'Capturer'
Start-Sleep -Seconds 2
Invoke-Button $s2root 'Sélectionner'
Start-Sleep -Seconds 2
Save-WindowShot $admin (Join-Path $outDir 'e2e-lot3-5-erolement-operateur.png')
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

# --- 6. Déconnexion puis reconnexion par empreinte -----------------------------
[L3Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('%f'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('d'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
$login2 = Wait-Window 'Connexion' 20
Write-Output "OK deconnexion"
Start-Sleep -Seconds 1

$lroot = [System.Windows.Automation.AutomationElement]::FromHandle($login2)
Invoke-Button $lroot 'Empreinte'
$scan3 = Wait-Window 'Capture d' 20
Start-Sleep -Seconds 1
$s3root = [System.Windows.Automation.AutomationElement]::FromHandle($scan3)
Select-ComboByName $s3root 'IdentityCombo' 'SIM-1'
Invoke-Button $s3root 'Capturer'
Start-Sleep -Seconds 2
Save-WindowShot $scan3 (Join-Path $outDir 'e2e-lot3-6-connexion-empreinte.png')
Invoke-Button $s3root 'Sélectionner'
$main2 = Wait-Window 'Poste de contr' 20
Write-Output "OK reconnexion par empreinte"
Start-Sleep -Seconds 2
Save-WindowShot $main2 (Join-Path $outDir 'e2e-lot3-7-session-biometrique.png')

Start-Sleep -Seconds 1
Stop-Process -Id $proc.Id -Force
Write-Output "TEST E2E LOT3: SUCCES"
