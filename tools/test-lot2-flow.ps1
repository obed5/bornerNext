# Test de bout en bout (Lot 2) : connexion -> F5 (enregistrement passeport) ->
# saisie d'un document + mouvement -> enregistrement réel -> fenêtre Visa -> captures.
# S'appuie sur UI Automation (AutomationId = x:Name) pour un remplissage robuste.
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class Lots2Win {
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
    $handles = [Lots2Win]::Handles([uint32]$proc.Id, $titlePart)
    if ($handles.Count -gt 0) { return $handles[0] }
    Start-Sleep -Milliseconds 300
  }
  throw "Timeout '$titlePart'"
}

function Save-WindowShot([IntPtr]$hwnd, [string]$path) {
  $rect = New-Object Lots2Win+RECT
  [Lots2Win]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
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

function Set-Text($root, [string]$automationId, [string]$value) {
  $element = Find-ById $root $automationId
  if (-not $element) { throw "Champ introuvable : $automationId" }
  $pattern = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
  $pattern.SetValue($value)
  Write-Output "  $automationId = '$value'"
}

function Set-ComboByName($root, [string]$automationId, [string]$itemNamePart) {
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
  $sel = $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
  $sel.Select(); Start-Sleep -Milliseconds 300
  $expand.Collapse()
  Write-Output "  $automationId -> '$itemNamePart'"
}

# --- 1. Connexion --------------------------------------------------------------
$login = Wait-Window 'Connexion' 30
[Lots2Win]::SetForegroundWindow($login) | Out-Null
Start-Sleep -Milliseconds 800
[System.Windows.Forms.SendKeys]::SendWait('admin{TAB}ChangeMe!2026{ENTER}')
$main = Wait-Window 'Poste de contr' 30
Write-Output "OK fenetre principale"

# --- 2. Enregistrement passeport (F5) ------------------------------------------
[Lots2Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('{F5}')
$passport = Wait-Window 'Enregistrement passeport' 20
Write-Output "OK fenetre passeport"
Start-Sleep -Seconds 2

# --- 3. Saisie (UI Automation) -------------------------------------------------
$root = [System.Windows.Automation.AutomationElement]::FromHandle($passport)
Set-Text $root 'DocNumberBox' 'PA-TEST-0001'
Set-Text $root 'SurnameBox' 'Kabila'
Set-Text $root 'GivenNameBox' 'Jeanne'
Set-ComboByName $root 'CitizenshipCombo' 'CONGO'

# Date de naissance : focus + saisie clavier (20/05/1990)
$birth = Find-ById $root 'BirthDatePicker'
$birth.SetFocus(); Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait('20/05/1990')
Start-Sleep -Milliseconds 400
Write-Output "  BirthDatePicker = 20/05/1990"

Save-WindowShot $passport (Join-Path $outDir 'e2e-lot2-1-passeport-saisie.png')

# --- 4. Enregistrement (bouton) ------------------------------------------------
$saveCond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::Button)
$buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $saveCond)
$saveBtn = $null
foreach ($b in $buttons) { if ($b.Current.Name -like 'Enregistrer*') { $saveBtn = $b; break } }
if (-not $saveBtn) { throw "Bouton Enregistrer introuvable" }
$invoke = $saveBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$invoke.Invoke()
Write-Output "OK enregistrement declenche"
Start-Sleep -Seconds 3
Save-WindowShot $passport (Join-Path $outDir 'e2e-lot2-2-passeport-enregistre.png')

# --- 5. Fenêtre Visas (Alt+M puis V) -------------------------------------------
[Lots2Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('%m')
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('v')
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
$visa = Wait-Window 'Visas' 20
Write-Output "OK fenetre visas"
Start-Sleep -Seconds 2
Save-WindowShot $visa (Join-Path $outDir 'e2e-lot2-3-visas.png')

Start-Sleep -Seconds 1
Stop-Process -Id $proc.Id -Force
Write-Output "TEST E2E LOT2: SUCCES"
