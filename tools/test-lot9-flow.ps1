# Test de bout en bout (Lot 9) : reprise de l'historique (base d'essai synthétique),
# durcissement (mot de passe d'amorçage, changement imposé, verrouillage, réinitialisation),
# jeu de démonstration, mesure des temps de réponse et purge des fichiers.
#
# La partie outil (CLI) est déterministe ; la partie interface pilote l'application réelle
# et produit des captures.
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class L9Win {
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

# La sortie de l'outil est en UTF-8 : la décoder correctement dans cette session.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$repo = 'D:\PROGRAMME\PROGRAMMES\BorderNext'
$outDir = Join-Path $repo 'tools'
$exe = Join-Path $repo 'src\BorderNext.UI\bin\Debug\net8.0-windows\BorderNext.UI.exe'
$tool = Join-Path $repo 'src\BorderNext.Migration\bin\Debug\net8.0\BorderNext.Migration.exe'
$sql = 'Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True'
$fixture = "$sql;Database=LegacyFixture"
$migrated = "$sql;Database=BorderNext_Migration"
$firstRun = "$sql;Database=BorderNext_Lot9"

$bootstrapPassword = 'Bootstrap!E2E2026'
$changedPassword = 'Nouveau!E2E2026'
$resetPassword = 'Reprise!E2E2026'

function Invoke-Tool([string[]]$arguments) {
  $output = & $tool @arguments 2>&1 | Out-String
  return $output
}

function Start-App([string]$connectionString, [string]$bootstrap) {
  Get-Process -Name 'BorderNext.UI' -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 500
  $env:BORDERNEXT_CONNECTION_STRING = $connectionString
  if ($bootstrap) { $env:BORDERNEXT_BOOTSTRAP_PASSWORD = $bootstrap }
  $script:proc = Start-Process -FilePath $exe -PassThru
  Write-Output "  application démarrée (PID $($script:proc.Id)) sur $($connectionString.Split(';')[-1])"
}

function Wait-Window([string]$titlePart, [int]$timeoutSec) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $script:proc.Id -ErrorAction SilentlyContinue)) { throw "Processus arrêté (attendu : $titlePart)" }
    $handles = [L9Win]::Handles([uint32]$script:proc.Id, $titlePart)
    if ($handles.Count -gt 0) { return $handles[0] }
    Start-Sleep -Milliseconds 300
  }
  throw "Timeout '$titlePart'"
}

function Save-WindowShot([IntPtr]$hwnd, [string]$path) {
  $rect = New-Object L9Win+RECT
  [L9Win]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
  $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output "  capture : $path"
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

function Contains-Text([string]$haystack, [string]$needle) {
  return (Normalize-Text $haystack).Contains((Normalize-Text $needle))
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

function Invoke-ButtonById($root, [string]$automationId) {
  $button = Find-ById $root $automationId
  if (-not $button) { throw "Bouton introuvable (id) : $automationId" }
  $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Set-Text($root, [string]$automationId, [string]$value) {
  $element = Find-ById $root $automationId
  if (-not $element) { throw "Champ introuvable : $automationId" }
  $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

function Get-Text([IntPtr]$hwnd) {
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
  $texts = Find-All $root ([System.Windows.Automation.ControlType]::Text)
  $values = @()
  foreach ($t in $texts) { $values += $t.Current.Name }
  return ($values -join ' | ')
}

function Dismiss-IfPresent([string]$titlePart) {
  $handles = [L9Win]::Handles([uint32]$script:proc.Id, $titlePart)
  if ($handles.Count -eq 0) { return }
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($handles[0])
  $okCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  $ok = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $okCond)
  if ($ok) { $ok.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
  else {
    [L9Win]::SetForegroundWindow($handles[0]) | Out-Null
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  }
  Start-Sleep -Milliseconds 400
}

function Connect-App([string]$userName, [string]$password, [int]$timeoutSec = 30) {
  $login = Wait-Window 'Connexion' $timeoutSec
  [L9Win]::SetForegroundWindow($login) | Out-Null
  Start-Sleep -Seconds 2
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($login)

  # Les champs sont vidés avant saisie : un second essai ne doit pas concaténer
  # l'identifiant précédent (sinon l'échec ne porte pas sur le compte visé).
  $edits = Find-All $root ([System.Windows.Automation.ControlType]::Edit)
  if ($edits.Count -gt 0) { $edits[0].SetFocus() }
  Start-Sleep -Milliseconds 400
  [System.Windows.Forms.SendKeys]::SendWait('^a{DEL}')
  Start-Sleep -Milliseconds 200
  [System.Windows.Forms.SendKeys]::SendWait($userName)
  Start-Sleep -Milliseconds 200
  [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
  Start-Sleep -Milliseconds 200
  [System.Windows.Forms.SendKeys]::SendWait('^a{DEL}')
  Start-Sleep -Milliseconds 200
  [System.Windows.Forms.SendKeys]::SendWait("$password{ENTER}")
  Start-Sleep -Milliseconds 500
}

function Complete-PasswordChange([string]$current, [string]$newPassword) {
  # Changement imposé à la première connexion : le mot de passe actuel, puis le nouveau
  # deux fois. La saisie est refaite une fois si la fenêtre reste ouverte (erreur de saisie).
  for ($attempt = 1; $attempt -le 2; $attempt++) {
    $window = Wait-Window 'Changement de mot de passe' 30
    [L9Win]::SetForegroundWindow($window) | Out-Null
    Start-Sleep -Seconds 2
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($window)
    $edits = Find-All $root ([System.Windows.Automation.ControlType]::Edit)
    if ($edits.Count -gt 0) { $edits[0].SetFocus() }
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('^a{DEL}')
    [System.Windows.Forms.SendKeys]::SendWait("$current{TAB}$newPassword{TAB}$newPassword{ENTER}")
    Start-Sleep -Seconds 3

    $stillOpen = ([L9Win]::Handles([uint32]$script:proc.Id, 'Changement de mot de passe')).Count -gt 0
    if (-not $stillOpen) { return }

    # Message d'erreur de la fenêtre (mot de passe trop court, identique, etc.).
    $errorText = ''
    if ($edits.Count -gt 0) {
      $error = Find-ById $root 'ErrorText'
      if ($error) { $errorText = $error.Current.Name }
    }
    Write-Output "  changement refusé (essai $attempt) : $errorText"
    Dismiss-IfPresent 'Erreur'
    Start-Sleep -Milliseconds 500
  }
  throw 'Le changement de mot de passe n''a pas abouti.'
}

function Dump-Dashboard([IntPtr]$hwnd) {
  # Contrôle que l'écran principal est bien affiché (indicateur du tableau de bord).
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
  $texts = Find-All $root ([System.Windows.Automation.ControlType]::Text)
  foreach ($t in $texts) { if (Text-Matches $t.Current.Name 'Transactions du jour') { return } }
  throw 'Tableau de bord non atteint (indicateur « Transactions du jour » absent).'
}

# ===========================================================================
Write-Output '--- 1. Base d''origine synthétique et reprise de l''historique ---'
$create = sqlcmd -S ".\SQLEXPRESS" -E -f 65001 -i (Join-Path $repo 'tools\legacy-fixture.sql') -b 2>&1 | Out-String
if (-not (Contains-Text $create 'Base d''origine synthétique')) { throw "Création de la base d'essai échouée." }
sqlcmd -S ".\SQLEXPRESS" -E -Q "IF DB_ID('BorderNext_Migration') IS NOT NULL BEGIN ALTER DATABASE BorderNext_Migration SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE BorderNext_Migration; END" | Out-Null

$dryRun = Invoke-Tool @('migrate', '--source', $fixture, '--target', $migrated, '--dry-run')
if (-not (Contains-Text $dryRun 'Mode simulation')) { throw 'La simulation n''a pas été reconnue.' }
Write-Output '  simulation : OK'

$report = Join-Path $outDir 'lot9-rapport-migration.json'
$migration = Invoke-Tool @('migrate', '--source', $fixture, '--target', $migrated, '--with-photos', '--report', $report)
if (-not (Contains-Text $migration 'Reprise terminée')) { throw 'Reprise échouée.' }
Write-Output ('  ' + (($migration -split "`n" | Where-Object { Contains-Text $_ 'Reprise terminée' }) -join ''))

if (-not (Test-Path $report)) { throw 'Rapport de reprise absent.' }
$json = Get-Content $report -Raw | ConvertFrom-Json
if ($json.totals.written -lt 15) { throw "Reprises insuffisantes : $($json.totals.written)" }
Write-Output "  rapport : $($json.totals.written) lignes reprises, $($json.totals.rejected) rejetées"

# Idempotence : un second passage ne doit rien réinsérer.
$second = Invoke-Tool @('migrate', '--source', $fixture, '--target', $migrated)
if (-not (Contains-Text $second '0 ligne(s) reprises')) { throw 'Reprise non idempotente.' }
Write-Output '  idempotence : OK (aucune réinsertion au second passage)'

$inspection = Invoke-Tool @('inspect', '--target', $migrated)
$checks = @{
  'personnes            : 3' = 'personnes';
  'documents de voyage  : 3' = 'documents';
  'mouvements           : 4' = 'mouvements';
  'visas                : 1' = 'visas';
  'permis               : 1' = 'permis';
}
foreach ($expected in $checks.Keys) {
  if (-not (Contains-Text $inspection $expected)) { throw "Contrôle manquant : $expected" }
}
Write-Output '  contrôles de reprise : OK (3 personnes, 3 documents, 4 mouvements, 1 visa, 1 permis)'

Write-Output ''
Write-Output '--- 2. Jeu de démonstration et temps de réponse ---'
sqlcmd -S ".\SQLEXPRESS" -E -Q "IF DB_ID('BorderNext_Lot9') IS NOT NULL BEGIN ALTER DATABASE BorderNext_Lot9 SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE BorderNext_Lot9; END" | Out-Null

$demo = Invoke-Tool @('seed-demo', '--target', $firstRun, '--persons', '300', '--movements', '1500', '--seed', '2026')
if (-not (Contains-Text $demo 'Terminé')) { throw 'Jeu de démonstration échoué.' }
Write-Output ('  ' + (($demo -split "`n" | Where-Object { Contains-Text $_ 'Terminé' }) -join ''))
if ($demo -match 'données réelles') { Write-Output '  mention « aucune donnée réelle » : OK' }

$bench = Invoke-Tool @('benchmark', '--target', $firstRun, '--iterations', '3')
if (-not (Contains-Text $bench 'Toutes les mesures sont sous 1000 ms')) {
  throw 'Mesures au-dessus de la cible.'
}
Write-Output '  mesures : OK'
foreach ($line in ($bench -split "`n" | Where-Object { $_ -match 'ms' })) { Write-Output ('  ' + $line.Trim()) }

Write-Output ''
Write-Output '--- 3. Purge des fichiers selon la durée de conservation ---'
$exchangeRoot = Join-Path $repo 'src\BorderNext.Migration\bin\Debug\net8.0\Exchanges'
$messages = Join-Path $exchangeRoot 'Messages'
New-Item -ItemType Directory -Path $messages -Force | Out-Null
$oldFile = Join-Path $messages 'ancien-message.edi'
$newFile = Join-Path $messages 'message-recent.edi'
Set-Content -Path $oldFile -Value 'UNB+ANCIEN'
Set-Content -Path $newFile -Value 'UNB+RECENT'
(Get-Item $oldFile).LastWriteTime = (Get-Date).AddDays(-100)

$purgeDry = Invoke-Tool @('purge-files', '--target', $firstRun, '--days', '30', '--dry-run')
if (-not (Contains-Text $purgeDry 'Simulation')) { throw 'Purge (simulation) : sortie inattendue.' }
if (-not (Test-Path $oldFile)) { throw 'La simulation ne doit rien supprimer.' }
Write-Output '  simulation : OK (aucune suppression)'

$purge = Invoke-Tool @('purge-files', '--target', $firstRun, '--days', '30')
if (-not (Contains-Text $purge 'Purge terminée')) { throw 'Purge : sortie inattendue.' }
if ((Test-Path $oldFile) -or (-not (Test-Path $newFile))) { throw 'Purge incorrecte : mauvais fichier supprimé.' }
Write-Output ('  ' + (($purge -split "`n" | Where-Object { Contains-Text $_ 'Purge terminée' }) -join ''))

Write-Output ''
Write-Output '--- 4. Interface : premier démarrage, mot de passe imposé ---'
Start-App $firstRun $bootstrapPassword
Connect-App 'admin' $bootstrapPassword
Complete-PasswordChange $bootstrapPassword $changedPassword
$main = Wait-Window 'Poste de contr' 30
Start-Sleep -Seconds 3
Save-WindowShot $main (Join-Path $outDir 'e2e-lot9-1-premier-demarrage.png')
Write-Output '  premier démarrage : changement de mot de passe imposé, tableau de bord atteint'

Write-Output ''
Write-Output '--- 5. Interface : verrouillage après échecs répétés ---'
# Cinq tentatives infructueuses sur le compte admin, puis tentative valide : verrouillage.
[L9Win]::SetForegroundWindow($main) | Out-Null
[System.Windows.Forms.SendKeys]::SendWait('%{F4}')   # fermeture de l'application (Alt+F4)
Start-Sleep -Seconds 2
Get-Process -Name 'BorderNext.UI' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Start-App $firstRun $null
for ($attempt = 1; $attempt -le 5; $attempt++) {
  Connect-App 'admin' 'mauvais-mot-de-passe'
  Start-Sleep -Seconds 2
}
Connect-App 'admin' 'mauvais-mot-de-passe'
Start-Sleep -Seconds 3
$login = [L9Win]::Handles([uint32]$script:proc.Id, 'Connexion')[0]
$loginText = Get-Text $login
if (-not (Text-Matches $loginText 'verrouille')) {
  throw "Message de verrouillage absent : $loginText"
}
Save-WindowShot $login (Join-Path $outDir 'e2e-lot9-2-verrouillage.png')
Write-Output '  verrouillage : OK (message affiché après cinq échecs)'

Write-Output ''
Write-Output '--- 6. Exploitation : réinitialisation du mot de passe ---'
Get-Process -Name 'BorderNext.UI' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
$reset = Invoke-Tool @('reset-password', '--target', $firstRun, '--user', 'admin', '--password', $resetPassword)
if (-not (Contains-Text $reset 'réinitialisé')) { throw 'Réinitialisation : sortie inattendue.' }
Write-Output '  mot de passe réinitialisé (changement imposé à la connexion)'

Start-App $firstRun $null
Connect-App 'admin' $resetPassword
Complete-PasswordChange $resetPassword $changedPassword
$main2 = Wait-Window 'Poste de contr' 30
Start-Sleep -Seconds 3
Dump-Dashboard $main2
Save-WindowShot $main2 (Join-Path $outDir 'e2e-lot9-3-connexion-apres-reinitialisation.png')
Write-Output '  reprise de l''accès : OK'

Get-Process -Name 'BorderNext.UI' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-Item Env:\BORDERNEXT_CONNECTION_STRING -ErrorAction SilentlyContinue
Remove-Item Env:\BORDERNEXT_BOOTSTRAP_PASSWORD -ErrorAction SilentlyContinue

Write-Output ''
Write-Output '--- 7. Dossier repris visible dans l''application ---'
Start-App $migrated $bootstrapPassword
Connect-App 'admin' $bootstrapPassword
Complete-PasswordChange $bootstrapPassword $changedPassword
$main3 = Wait-Window 'Poste de contr' 30
Start-Sleep -Seconds 3

# Ouverture de l'enregistrement passeport et recherche du document repris.
$root3 = [System.Windows.Automation.AutomationElement]::FromHandle($main3)
$menuCond = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
foreach ($m in $root3.FindAll([System.Windows.Automation.TreeScope]::Descendants, $menuCond)) {
  if (Text-Matches $m.Current.Name 'Modules') { $m.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); break }
}
Start-Sleep -Milliseconds 800
foreach ($m in $root3.FindAll([System.Windows.Automation.TreeScope]::Descendants, $menuCond)) {
  # Libellé du menu : « _Passeports (F5) » — le tiret souligné porte la lettre d'accès.
  if (Text-Matches $m.Current.Name 'Passeports') { $m.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); break }
}
$passport = Wait-Window 'nregistrement' 30
Start-Sleep -Seconds 3
$rootP = [System.Windows.Automation.AutomationElement]::FromHandle($passport)

# Recherche multicritères : ouverture du dialogue, saisie du numéro, sélection.
foreach ($b in (Find-All $rootP ([System.Windows.Automation.ControlType]::Button))) {
  if (Text-Matches $b.Current.Name 'Rechercher un document') { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); break }
}
$search = Wait-Window 'Recherche de documents' 20
Start-Sleep -Seconds 2
$sroot = [System.Windows.Automation.AutomationElement]::FromHandle($search)
Set-Text $sroot 'NumberBox' 'OP000001'
Start-Sleep -Milliseconds 500
foreach ($b in (Find-All $sroot ([System.Windows.Automation.ControlType]::Button))) {
  if (Text-Matches $b.Current.Name 'Rechercher') { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); break }
}

$rows = @()
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline) {
  $rows = Find-All $sroot ([System.Windows.Automation.ControlType]::DataItem)
  if ($rows.Count -ge 1) { break }
  Start-Sleep -Milliseconds 500
}
if ($rows.Count -lt 1) { throw 'Le dossier repris (OP000001) est introuvable.' }
$rows[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 800
foreach ($b in (Find-All $sroot ([System.Windows.Automation.ControlType]::Button))) {
  if (Text-Matches $b.Current.Name 'Sélectionner') { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); break }
}
Start-Sleep -Seconds 3

# Contrôle du dossier affiché : la valeur des champs de saisie n'apparaît pas dans les
# libellés de l'écran, on lit donc le nom porté par le champ.
$surnameBox = Find-ById $rootP 'SurnameBox'
if (-not $surnameBox) { throw 'Champ Nom introuvable.' }
$surname = $surnameBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
if (-not (Text-Matches $surname 'MARTIN')) { throw "Dossier repris non affiché (nom lu : '$surname')." }
$passportText = Get-Text $passport
if (-not (Text-Matches $passportText 'Document n°OP000001')) {
  throw "En-tête du dossier repris absent : $passportText"
}
Save-WindowShot $passport (Join-Path $outDir 'e2e-lot9-4-dossier-repris.png')
Write-Output '  dossier repris affiché : OK (MARTIN CLAIRE, document OP000001)'

Get-Process -Name 'BorderNext.UI' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Output ''
Write-Output 'TEST E2E LOT9: SUCCES'
