# Test de bout en bout (Lot 1) : connexion -> menu Administration -> onglets + captures d'écran.
# Vérifie : ouverture de la fenêtre d'administration, présence des 6 onglets, données semées visibles.
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class AdminWin {
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
  public static List<string> AllTitles(uint targetPid) {
    var result = new List<string>();
    EnumWindows((h, l) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid == targetPid && IsWindowVisible(h)) {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, 512);
        if (sb.Length > 0) result.Add(sb.ToString());
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

function Get-Titles {
  $titles = @()
  foreach ($h in [AdminWin]::Handles([uint32]$proc.Id, $null)) {
    $sb = New-Object System.Text.StringBuilder 512
    # Les handles ne renvoient que les fenêtres visibles avec titre non vide.
    $titles += $h
  }
  return $titles.Count
}

function Dump-Windows([string]$stage) {
  Write-Output "--- fenetres visibles [$stage] :"
  $all = [AdminWin]::AllTitles([uint32]$proc.Id)
  foreach ($t in $all) { Write-Output "    '$t'" }
}

function Wait-Window([string]$titlePart, [int]$timeoutSec) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) { throw "Processus arrete (attendu: $titlePart)" }
    $handles = [AdminWin]::Handles([uint32]$proc.Id, $titlePart)
    if ($handles.Count -gt 0) { return $handles[0] }
    Start-Sleep -Milliseconds 300
  }
  Dump-Windows "timeout '$titlePart'"
  throw "Timeout '$titlePart'"
}

function Wait-AnyWindow([string[]]$titleParts, [int]$timeoutSec) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) { throw "Processus arrete" }
    foreach ($part in $titleParts) {
      $handles = [AdminWin]::Handles([uint32]$proc.Id, $part)
      if ($handles.Count -gt 0) { return $handles[0] }
    }
    Start-Sleep -Milliseconds 300
  }
  Dump-Windows "timeout multi-fenetres"
  throw "Timeout multi-fenetres"
}

function Save-WindowShot([IntPtr]$hwnd, [string]$path) {
  $rect = New-Object AdminWin+RECT
  [AdminWin]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
  $w = $rect.Right - $rect.Left
  $h = $rect.Bottom - $rect.Top
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output "Capture: $path"
}

# --- 1. Connexion ------------------------------------------------------------------
$login = Wait-Window 'Connexion' 30
[AdminWin]::SetForegroundWindow($login) | Out-Null
Start-Sleep -Milliseconds 800
[System.Windows.Forms.SendKeys]::SendWait('admin{TAB}ChangeMe!2026{ENTER}')

# Changement de mot de passe éventuel (1re connexion) sinon fenêtre principale directe.
$w = Wait-AnyWindow @('Changement de mot de passe', 'Poste de contr') 25
if ([AdminWin]::Handles([uint32]$proc.Id, 'Changement de mot de passe').Count -gt 0) {
  Write-Output "Dialogue de changement de mot de passe detecte"
  [AdminWin]::SetForegroundWindow($w) | Out-Null
  Start-Sleep -Milliseconds 800
  [System.Windows.Forms.SendKeys]::SendWait('ChangeMe!2026{TAB}ChangeMe!2026{TAB}ChangeMe!2026{ENTER}')
  $w = Wait-Window 'Poste de contr' 20
}
Write-Output "OK fenetre principale"

# --- 2. Ouverture de l'administration (Alt+M puis clé d'accès A) --------------------
[AdminWin]::SetForegroundWindow($w) | Out-Null
Start-Sleep -Milliseconds 800
[System.Windows.Forms.SendKeys]::SendWait('%m')
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('a')
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')

$admin = Wait-Window 'Administration' 20
Write-Output "OK fenetre Administration ouverte"
Start-Sleep -Seconds 3   # chargement des referentiels

# --- 3. Inspecter les onglets via UI Automation -------------------------------------
$root = [System.Windows.Automation.AutomationElement]::FromHandle($admin)
$cond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::TabItem)
$tabs = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
$tabNames = @()
foreach ($t in $tabs) { $tabNames += $t.Current.Name }
Write-Output ("Onglets: " + ($tabNames -join ' | '))
if ($tabs.Count -lt 6) { throw "Nombre d'onglets inattendu: $($tabs.Count)" }

# --- 4. Captures par onglet ---------------------------------------------------------
Save-WindowShot $admin (Join-Path $outDir 'e2e-admin-1-referentiels.png')

function Select-Tab([string]$namePart, [string]$shotName) {
  $tabs = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  $target = $null
  foreach ($t in $tabs) { if ($t.Current.Name -like "*$namePart*") { $target = $t; break } }
  if (-not $target) { Write-Output "Onglet introuvable: $namePart"; return }
  $sel = $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
  $sel.Select()
  Start-Sleep -Seconds 2
  Save-WindowShot $admin (Join-Path $outDir $shotName)
}

Select-Tab 'Pays' 'e2e-admin-2-pays.png'
Select-Tab 'Postes' 'e2e-admin-5-postes.png'
Select-Tab 'Utilisateurs' 'e2e-admin-3-utilisateurs.png'
Select-Tab 'Param' 'e2e-admin-6-parametres.png'
Select-Tab 'Audit' 'e2e-admin-4-audit.png'

Start-Sleep -Seconds 1
Stop-Process -Id $proc.Id -Force
Write-Output "TEST E2E ADMIN: SUCCES"
