# Installation

Application de bureau Windows (.NET 8 WPF) avec base SQL Server. Ce guide couvre
l'installation sur un poste frontière, la configuration des secrets et la mise à jour.

## 1. Prérequis

| Élément | Version | Remarque |
|---|---|---|
| Windows | 10 / 11 / Server 2019+ | Poste de travail ou serveur de poste |
| .NET | 8.0 (runtime de bureau) | Inclus dans la publication autonome (aucune installation requise) |
| SQL Server | 2019+ (Express accepté) | Base `BorderNext`, collation française recommandée |
| Écran | 1280 × 800 minimum | L'application est conçue pour un poste de travail plein écran |

Le poste n'a besoin d'aucune police particulière : les documents PDF embarquent leur police.

## 2. Base de données

1. Créer une base vide `BorderNext` (droits `db_owner` pour le compte de service).
2. Renseigner la chaîne de connexion, par ordre de priorité :
   - variable d'environnement **`BORDERNEXT_CONNECTION_STRING`** (recommandé : aucun secret en fichier) ;
   - à défaut, `src/BorderNext.UI/appsettings.json` (`ConnectionStrings:BorderNext`).

```powershell
# Exemple : compte de service SQL (recommandé en production)
$env:BORDERNEXT_CONNECTION_STRING = 'Server=SRV-SQL;Database=BorderNext;User Id=bordernext;Password=***;TrustServerCertificate=True'
```

Les migrations de schéma sont appliquées automatiquement au démarrage, ainsi que le semis
des référentiels (nomenclatures, pays, villes, postes frontières, droits par défaut) —
idempotent : un redémarrage ne réécrit rien.

## 3. Mot de passe du compte d'administration initial

Le mot de passe n'est **jamais** écrit dans un fichier de configuration. À la première
installation, il est résolu dans cet ordre :

1. variable d'environnement **`BORDERNEXT_BOOTSTRAP_PASSWORD`** (recommandé, déploiement automatisé) ;
2. fichier protégé par DPAPI `%ProgramData%\BorderNext\bootstrap.secret` (créé au premier démarrage) ;
3. à défaut : mot de passe aléatoire de 20 caractères, enregistré protégé **et** déposé une
   seule fois dans `%ProgramData%\BorderNext\mot-de-passe-initial.txt`.

Le changement de mot de passe est **imposé** à la première connexion. Supprimer
`mot-de-passe-initial.txt` après cette première connexion.

```powershell
# Déploiement automatisé : fournir le mot de passe, aucun fichier ne sera créé
$env:BORDERNEXT_BOOTSTRAP_PASSWORD = 'MotDePasse!Initial2026'
```

## 4. Installation de l'application

### Publication et copie

```powershell
# Publication autonome (runtime inclus) dans dist/BorderNext
powershell -ExecutionPolicy Bypass -File tools/publish.ps1

# Puis copier dist/BorderNext sur le poste (ex. C:\BorderNext) et créer un raccourci
# vers BorderNext.UI.exe.
```

### Installateur (optionnel)

`tools/publish.ps1 -Installer` produit en plus une archive `BorderNext-<version>.zip`
prête à être empaquetée (MSIX avec un certificat de signature, ou script Inno Setup/WiX
selon la politique du parc). La publication autonome ne nécessite aucun prérequis
logiciel sur le poste : c'est la voie recommandée pour un déploiement manuel.

## 5. Premier démarrage

1. Lancer `BorderNext.UI.exe`.
2. Se connecter avec `admin` et le mot de passe d'amorçage.
3. Changer le mot de passe (obligatoire).
4. **Administration → Postes frontières** : sélectionner le poste du lieu.
5. **Administration → Paramètres** : vérifier le pays, la devise, le terminal, le sens de
   passage par défaut, la durée de conservation des fichiers.
6. **Échanges → Connecteurs** : renseigner les points d'accès réels et les jetons
   (chiffrés au repos) le moment venu ; en attendant, le mode simulé est actif.

## 6. Langue

Français par défaut (`App:DefaultCulture` dans `appsettings.json`), anglais disponible
depuis l'écran de connexion (sélecteur de langue). Le choix est mémorisé par session de
connexion.

## 7. Mise à jour

1. Arrêter l'application sur le poste.
2. Sauvegarder la base (voir `exploitation.md`).
3. Remplacer le contenu du répertoire d'installation par la nouvelle publication
   (le fichier `appsettings.json` peut être conservé).
4. Relancer : les migrations de schéma et les semis manquants s'appliquent au démarrage.

Les réglages créés par l'installation (paramètres, connecteurs, jetons) sont dans la base :
ils suivent la sauvegarde, pas le répertoire d'installation.
