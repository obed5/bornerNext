# Test de bout en bout du flux de connexion BorderNext (v2)
# - énumère TOUTES les fenêtres du processus (y compris les dialogues)
# - connexion admin/ChangeMe!2026 -> dialogue de changement de MDP -> fenêtre principale
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class WinEnum2 {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
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

$exe = 'D:\PROGRAMME\PROGRAMMES\BorderNext\src\BorderNext.UI\bin\Debug\net8.0-windows\BorderNext.UI.exe'
$proc = Start-Process -FilePath $exe -PassThru
Write-Output "PID=$($proc.Id)"

function Wait-Window([string]$titlePart, [int]$timeoutSec) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    $p = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    if (-not $p) { throw "Le processus s'est arrete (attendu: $titlePart)" }
    $handles = [WinEnum2]::Handles([uint32]$proc.Id, $titlePart)
    if ($handles.Count -gt 0) { return $handles[0] }
    Start-Sleep -Milliseconds 300
  }
  $all = [WinEnum2]::Handles([uint32]$proc.Id, $null).Count
  throw "Timeout '$titlePart'. Nb fenetres du processus: $all"
}

Add-Type -AssemblyName System.Windows.Forms

# 1. Fenetre de connexion
$login = Wait-Window 'Connexion' 30
[WinEnum2]::SetForegroundWindow($login) | Out-Null
Start-Sleep -Milliseconds 800
Write-Output "OK fenetre connexion"

# 2. Identifiants + validation (focus initial sur le nom d'utilisateur)
[System.Windows.Forms.SendKeys]::SendWait('admin{TAB}ChangeMe!2026{ENTER}')
Write-Output "OK identifiants envoyes"
Start-Sleep -Seconds 2

# 3. Dialogue de changement de mot de passe (obligatoire a la 1re connexion)
$dlg = Wait-Window 'Changement de mot de passe' 20
[WinEnum2]::SetForegroundWindow($dlg) | Out-Null
Start-Sleep -Milliseconds 800
Write-Output "OK fenetre changement MDP"

# 4. Mot de passe courant -> nouveau -> confirmation -> Valider
[System.Windows.Forms.SendKeys]::SendWait('ChangeMe!2026{TAB}ChangeMe!2026{TAB}ChangeMe!2026{ENTER}')
Write-Output "OK formulaire MDP envoye"

# 5. La fenetre principale doit apparaitre
$main = Wait-Window 'Poste de contr' 20
Write-Output "OK fenetre principale affichee"

Start-Sleep -Seconds 2
Stop-Process -Id $proc.Id -Force
Write-Output "TEST E2E: SUCCES"
