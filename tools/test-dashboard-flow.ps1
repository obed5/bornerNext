# Vérification du tableau de bord (écran d'accueil) : les cartes doivent afficher
# les mêmes valeurs que la base (contrôle croisé SQL fait hors script).
$ErrorActionPreference = 'Stop'

Get-Process BorderNext.UI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class DbWin {
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
    $handles = [DbWin]::Handles([uint32]$proc.Id, $titlePart)
    if ($handles.Count -gt 0) { return $handles[0] }
    Start-Sleep -Milliseconds 300
  }
  throw "Timeout '$titlePart'"
}

function Save-WindowShot([IntPtr]$hwnd, [string]$path) {
  $rect = New-Object DbWin+RECT
  [DbWin]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
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

function Get-TextById($root, [string]$automationId) {
  $element = Find-ById $root $automationId
  if (-not $element) { return $null }
  return $element.Current.Name
}

# --- Connexion ---------------------------------------------------------------
$login = Wait-Window 'Connexion' 60
[DbWin]::SetForegroundWindow($login) | Out-Null
Start-Sleep -Milliseconds 1500
[System.Windows.Forms.SendKeys]::SendWait('admin{TAB}ChangeMe!2026{ENTER}')
$main = Wait-Window 'Poste de contr' 30
Start-Sleep -Seconds 3
$root = [System.Windows.Automation.AutomationElement]::FromHandle($main)

# --- Lecture des cartes ------------------------------------------------------
$cards = [ordered]@{
  'Poste'            = 'DbStationText'
  'Transactions'     = 'DbTransactionsValue'
  'DetailTrans'      = 'DbTransactionsDetail'
  'AnPasse'          = 'DbYearAgoValue'
  'DetailAnPasse'    = 'DbYearAgoDetail'
  'Douteux'          = 'DbDoubtfulValue'
  'DetailDouteux'    = 'DbDoubtfulDetail'
  'Voyages'          = 'DbTripsValue'
  'DetailVoyages'    = 'DbTripsDetail'
  'Inspecteurs'      = 'DbInspectorsValue'
  'DetailInspect'    = 'DbInspectorsDetail'
  'Refus'            = 'DbRefusalsValue'
  'DetailRefus'      = 'DbRefusalsDetail'
}
foreach ($entry in $cards.GetEnumerator()) {
  $value = Get-TextById $root $entry.Value
  Write-Output ("  {0} [{1}] = '{2}'" -f $entry.Key, $entry.Value, $value)
  if ($null -eq $value) { throw "Carte introuvable : $($entry.Value)" }
}

# --- Grille par poste + nationalités ----------------------------------------
$grid = Find-ById $root 'BorderPointGrid'
if (-not $grid) { throw 'Grille BorderPointGrid introuvable' }
$cond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::DataItem)
$rows = $grid.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
Write-Output "  lignes par poste: $($rows.Count)"

$nat = Find-ById $root 'NationalityList'
if (-not $nat) { throw 'Liste NationalityList introuvable' }
$natItems = $nat.FindAll([System.Windows.Automation.TreeScope]::Descendants,
  (New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::ListItem)))
Write-Output "  nationalites listees: $($natItems.Count)"

Save-WindowShot $main (Join-Path $outDir 'e2e-dashboard.png')

Start-Sleep -Seconds 1
Stop-Process -Id $proc.Id -Force
Write-Output 'TEST TABLEAU DE BORD: SUCCES'
