# ---------------------------------------------------------------------------
# Publication de BorderNext pour un poste Windows (Lot 9).
#
#   powershell -ExecutionPolicy Bypass -File tools/publish.ps1
#   powershell -ExecutionPolicy Bypass -File tools/publish.ps1 -Version 1.0.0 -Zip
#
# Produit :
#   dist/BorderNext/               application autonome (runtime inclus)
#   dist/BorderNext-<version>.zip   archive prête à copier sur le poste (option -Zip)
#
# L'application publiée n'exige aucun prérequis logiciel sur le poste : ni .NET,
# ni police particulière. Seule la base SQL Server est nécessaire (voir docs/installation.md).
# ---------------------------------------------------------------------------
param(
    [string]$Version = '1.0.0',
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$Zip,
    [switch]$SkipTool
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo 'dist'
$output = Join-Path $dist 'BorderNext'

if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
if (Test-Path $output) { Remove-Item -Recurse -Force $output }

Write-Output "Publication de l'application ($Configuration, $Runtime)…"
dotnet publish (Join-Path $repo 'src/BorderNext.UI/BorderNext.UI.csproj') `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $output `
    -p:Version=$Version `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "La publication de l'application a échoué." }

if (-not $SkipTool) {
    Write-Output "Publication de l'outil d'exploitation…"
    dotnet publish (Join-Path $repo 'src/BorderNext.Migration/BorderNext.Migration.csproj') `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained true `
        --output (Join-Path $output 'tools') `
        -p:Version=$Version `
        -p:DebugType=none `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "La publication de l'outil a échoué." }
}

# Documentation livrée avec l'installation (installation, exploitation, guides).
$docs = Join-Path $output 'docs'
New-Item -ItemType Directory -Path $docs -Force | Out-Null
Copy-Item (Join-Path $repo 'docs/*.md') $docs -Force
Copy-Item (Join-Path $repo 'docs/guide') $docs -Recurse -Force
Copy-Item (Join-Path $repo 'README.md') $output -Force

# Fichier de version (support d'exploitation).
@"
BorderNext $Version
Publié le $(Get-Date -Format 'yyyy-MM-dd HH:mm')
Configuration : $Configuration / $Runtime
Base de données : voir docs/installation.md (chaîne de connexion par variable d'environnement)
"@ | Set-Content -Path (Join-Path $output 'VERSION.txt') -Encoding UTF8

$files = (Get-ChildItem -Recurse -File $output | Measure-Object).Count
$size = [math]::Round(((Get-ChildItem -Recurse -File $output | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Output "Publication terminée : $output ($files fichiers, $size Mo)"

if ($Zip) {
    $archive = Join-Path $dist "BorderNext-$Version.zip"
    if (Test-Path $archive) { Remove-Item -Force $archive }
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive
    Write-Output "Archive : $archive"
}

Write-Output "Rappel : le mot de passe d'amorçage se fournit par variable d'environnement BORDERNEXT_BOOTSTRAP_PASSWORD."
