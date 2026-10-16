# Test de bout en bout (Lot 7) : import d'un message EDIFACT PAXLST (fichier d'exemple),
# consultation des passagers rapprochés, contrôles externes simulés (SIMIEC / Belize /
# système aérien), journal des échanges et configuration d'un connecteur.
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class L7Win {
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

# Fichier d'exemple : message PAXLST (1 passager rapprochable + 1 membre d'équipage).
$sampleDir = Join-Path $env:TEMP 'bn-lot7'
New-Item -ItemType Directory -Force -Path $sampleDir | Out-Null
$samplePath = Join-Path $sampleDir 'AF100.paxlst'
$sample = @(
  "UNA:+.? '"
  "UNB+UNOA:1+AIRLINE+SITE2+261015:1430+000000777'"
  "UNH+1+PAXLST:D:05B:UN:IATA'"
  "BGM+745+1+9'"
  "TDT+20+AF100+AF:Air France'"
  "LOC+87+ORY'"
  "LOC+125+FIH'"
  "DTM+189:202610151430:203'"
  "DTM+232:202610152230:203'"
  "NAD+MS+++CARTE D ILE MAURICE'"
  "NAD+FL+++KABEYA:JOSEPH:PAUL'"
  "DTM+329:19850312:102'"
  "ATT+2+M'"
  "NAT+2:CD'"
  "DOC+P+OP445566+CD:20240110:20300109'"
  "NAD+FM+++MUKENDI:ANTOINE'"
  "DTM+174:19790101:102'"
  "ATT+2+1'"
  "CNT+41:1'"
  "CNT+42:1'"
  "UNT+19+1'"
  "UNZ+1+000000777'"
) -join "`r`n"
[System.IO.File]::WriteAllText($samplePath, $sample, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "Fichier d'exemple : $samplePath"

# Nettoyage : aucune instance résiduelle ne doit capter le focus clavier.
Get-Process -Name 'BorderNext.UI' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

$proc = Start-Process -FilePath $exe -PassThru
Write-Output "PID=$($proc.Id)"

function Wait-Window([string]$titlePart, [int]$timeoutSec) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) { throw "Processus arrete (attendu: $titlePart)" }
    $handles = [L7Win]::Handles([uint32]$proc.Id, $titlePart)
    if ($handles.Count -gt 0) { return $handles[0] }
    Start-Sleep -Milliseconds 300
  }
  throw "Timeout '$titlePart'"
}

function Save-WindowShot([IntPtr]$hwnd, [string]$path) {
  $rect = New-Object L7Win+RECT
  [L7Win]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
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

function Invoke-ButtonById($root, [string]$automationId) {
  $button = Find-ById $root $automationId
  if (-not $button) { throw "Bouton introuvable (id): $automationId" }
  $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Write-Output "  clic (id): $automationId"
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

function Set-Text($root, [string]$automationId, [string]$value) {
  $element = Find-ById $root $automationId
  if (-not $element) { throw "Champ introuvable : $automationId" }
  $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
  Write-Output "  $automationId = '$value'"
}

function Count-Rows($root, [string]$gridId) {
  $grid = Find-ById $root $gridId
  if (-not $grid) { throw "Grille introuvable : $gridId" }
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::DataItem)
  return $grid.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond).Count
}

function Select-FirstRow($root, [string]$gridId) {
  $grid = Find-ById $root $gridId
  if (-not $grid) { throw "Grille introuvable : $gridId" }
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::DataItem)
  $rows = $grid.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  if ($rows.Count -eq 0) { throw "Aucune ligne de donnees dans $gridId" }
  $rows[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 800
  Write-Output "  ligne selectionnee dans $gridId"
}

function Dismiss-MessageBox([int]$timeoutSec = 20) {
  $box = Wait-Window 'changes frontaliers' $timeoutSec
  [L7Win]::SetForegroundWindow($box) | Out-Null
  Start-Sleep -Milliseconds 900
  [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  Start-Sleep -Milliseconds 600
}

# --- 1. Connexion -------------------------------------------------------------
$login = Wait-Window 'Connexion' 30
[L7Win]::SetForegroundWindow($login) | Out-Null
Start-Sleep -Milliseconds 1500
[System.Windows.Forms.SendKeys]::SendWait('admin{TAB}ChangeMe!2026{ENTER}')
$main = Wait-Window 'Poste de contr' 25
[L7Win]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 800

# --- 2. Ouverture du module Échanges (alt+M puis h) ---------------------------
[System.Windows.Forms.SendKeys]::SendWait('%m'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('h'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
$ex = Wait-Window 'changes frontaliers' 25
Start-Sleep -Seconds 3
$root = [System.Windows.Automation.AutomationElement]::FromHandle($ex)
Write-Output 'Module Échanges ouvert'

# --- 3. Import PAXLST ---------------------------------------------------------
Invoke-ButtonById $root 'ImportPaxlstBtn'
$fileDialog = Wait-Window 'Importer un message' 20
[L7Win]::SetForegroundWindow($fileDialog) | Out-Null
Start-Sleep -Seconds 1
[System.Windows.Forms.SendKeys]::SendWait($samplePath)
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Seconds 3

# Récapitulatif d'import puis relecture du message.
Dismiss-MessageBox
Start-Sleep -Seconds 2
$messageRows = Count-Rows $root 'MessagesGrid'
if ($messageRows -lt 1) { throw "Aucun message APIS apres import" }
Write-Output "  messages APIS: $messageRows"

Select-FirstRow $root 'MessagesGrid'
$passengerRows = Count-Rows $root 'PassengersGrid'
if ($passengerRows -lt 2) { throw "Passagers du message non affiches ($passengerRows)" }
Write-Output "  passagers du message: $passengerRows"
Save-WindowShot $ex (Join-Path $outDir 'e2e-lot7-1-messages.png')

# --- 4. Contrôles externes simulés -------------------------------------------
Select-Tab $root 'Contr'
Set-Text $root 'CheckDocumentBox' 'OP123456'
Set-Text $root 'CheckSurnameBox' 'KABEYA'
Invoke-ButtonById $root 'CheckAllBtn'
Start-Sleep -Seconds 3
$checkRows = Count-Rows $root 'CheckResultsGrid'
if ($checkRows -lt 3) { throw "Contrôles externes incomplets ($checkRows)" }
Write-Output "  contrôles exécutés: $checkRows"
Save-WindowShot $ex (Join-Path $outDir 'e2e-lot7-2-controles.png')

# Opposition SIMIEC : titre signalé retiré.
Set-Text $root 'CheckDocumentBox' 'OPEXP999'
Invoke-ButtonById $root 'CheckSimiecBtn'
Start-Sleep -Seconds 3
$checkRows2 = Count-Rows $root 'CheckResultsGrid'
if ($checkRows2 -lt 4) { throw "Contrôle SIMIEC d'opposition non exécuté" }
Write-Output "  contrôles après opposition SIMIEC: $checkRows2"
Save-WindowShot $ex (Join-Path $outDir 'e2e-lot7-3-opposition-simiec.png')

# --- 5. Export XML puis journal ----------------------------------------------
Select-Tab $root 'Messages'
Invoke-ButtonById $root 'ExportPaxlstBtn'
Start-Sleep -Seconds 3
Dismiss-MessageBox
Start-Sleep -Seconds 2

Select-Tab $root 'Journal'
$runRows = Count-Rows $root 'RunsGrid'
if ($runRows -lt 4) { throw "Journal des echanges incomplet ($runRows executions)" }
Select-FirstRow $root 'RunsGrid'
Write-Output "  exécutions journalisées: $runRows"
Save-WindowShot $ex (Join-Path $outDir 'e2e-lot7-4-journal.png')

# --- 6. Configuration d'un connecteur ----------------------------------------
Select-Tab $root 'Connecteurs'
$connectorRows = Count-Rows $root 'ConnectorsGrid'
if ($connectorRows -lt 3) { throw "Connecteurs non listes ($connectorRows)" }
Select-FirstRow $root 'ConnectorsGrid'
Set-Text $root 'EndpointBox' 'https://exemple.invalid/simiec'
Invoke-ButtonById $root 'SaveConnectorBtn'
Start-Sleep -Seconds 2
Write-Output "  connecteurs: $connectorRows"
Save-WindowShot $ex (Join-Path $outDir 'e2e-lot7-5-connecteurs.png')

# Retour en mode simulé (endpoint vidé) : l'état de démonstration reste propre.
Select-FirstRow $root 'ConnectorsGrid'
Set-Text $root 'EndpointBox' ''
Invoke-ButtonById $root 'SaveConnectorBtn'
Start-Sleep -Seconds 1

Stop-Process -Id $proc.Id -Force
Write-Output "TEST E2E LOT7: SUCCES"
