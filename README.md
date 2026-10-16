# BorderNext

Application de gestion d'un poste frontière.

> Nom provisoire « BorderNext » — renommable ultérieurement sans impact sur le code.

## Architecture

| Projet | Rôle |
|---|---|
| `src/BorderNext.Domain` | Entités métier (aucune dépendance) |
| `src/BorderNext.Application` | Services métier (auth, réglages…), abstractions |
| `src/BorderNext.Infrastructure` | EF Core (SQL Server), sécurité (PBKDF2), services plateforme |
| `src/BorderNext.UI` | Application WPF (connexion, shell, i18n, administration) |
| `tests/BorderNext.Tests` | Tests unitaires et d'intégration (xUnit) |

## Prérequis

- .NET 8 SDK
- SQL Server (dev : instance locale `.\SQLEXPRESS`, base `BorderNext`)

## Démarrage

```bash
dotnet build                          # compilation
dotnet test                           # tests (240 avec le tableau de bord)
dotnet run --project src/BorderNext.UI
```

Au premier lancement :
1. les migrations sont appliquées automatiquement (base `BorderNext` créée si absente) ;
2. un compte administrateur initial est créé : **admin / ChangeMe!2026** ;
3. le changement de mot de passe est **obligatoire** à la première connexion ;
4. les référentiels sont semés (idempotent, section par section) : nomenclatures,
   pays/villes, postes frontières, paramétrage de départ et droits par défaut —
   données réelles des nomenclatures du poste frontière.

Administration : menu **Modules → Administration** (rôles ADMIN/SUPERUSER, ou selon la
matrice des droits `ControlsToRoles`) — 6 onglets : référentiels, pays & géographie,
postes frontières, utilisateurs & droits, paramètres, journal d'audit.

Tests d'intégration UI (PowerShell, pilotage de l'application réelle) :
`tools/test-login-flow.ps1` (connexion) et `tools/test-admin-flow.ps1` (administration + captures).

Configuration : `src/BorderNext.UI/appsettings.json` (chaîne de connexion, culture par
défaut, mot de passe bootstrap — à remplacer en production par un secret protégé).

## Sécurité (exigences fondatrices)

- Mots de passe hachés PBKDF2-HMAC-SHA256 (210 000 itérations, sel aléatoire) — jamais en clair.
- Requêtes exclusivement paramétrées (EF Core) — aucune concaténation SQL.
- Journal d'audit par événements (types hérités `AUDIT_LOG_*`).
- Aucun mot de passe maître ni backdoor (défaut de l'ancien système corrigé).

## Feuille de route (lots)

- [x] **Lot 0 — Socle** : solution, base + migrations, auth/rôles, i18n fr/en, audit, tests, CI.
- [x] **Lot 1 — Référentiels et administration** : 43 nomenclatures (175 éléments fr/en),
      220 pays, 18 villes, 11 postes frontières, 25 paramètres, comptes et droits
      (`ControlsToRoles`), journal d'audit avec export CSV, fenêtre d'administration à 6 onglets.
- [x] **Lot 2 — Passeports et visas** : personnes, documents de voyage (photo + scan),
      mouvements entrée/sortie avec validations héritées (validité du document, motif de
      refus, visa), visas avec numérotation et enfants, recherche documentaire,
      écrans « Enregistrement passeport » (F5) et « Visas ».
- [x] **Lot 3 — Biométrie et documents** : abstraction matériel (caméra, lecteur
      d'empreintes, lecteur de documents, comparateur) avec **simulateurs** (images
      synthétiques, MRZ TD3 ICAO, gabarits déterministes) ; empreintes des voyageurs
      (10 doigts, contrôle de doublon), enrôlement biométrique des opérateurs et
      **connexion par empreinte** ; intégration au formulaire passeport
      (lecture automatique du document, capture photo).
- [x] **Lot 4 — Permis et enforcement** : permis (détails de résidence, contacts,
      permis de conduire, antécédents, voyage, indépendant, personnes à charge),
      refus d'entrée avec base légale, ordres de reconduite / avis
      d'inadmissibilité, amendes avec encaissement — écrans « Permis » et
      « Refus & sanctions ».
- [x] **Lot 5 — Listes de surveillance et personnes douteuses** : listes d'alerte
      personnes et documents (moteur de correspondance hérité à 7 critères
      personnes / 6 critères documents — Levenshtein, découpage des noms,
      variantes, noms inversés, seuil national, priorité d'action), workflow
      douteux 1→2→3 avec décision du superviseur, empreintes de liste d'alerte,
      simulateur de correspondance (types 1–8) et contrôle inverse contre le
      registre — écran « Surveillance » à 4 onglets et alarme à l'enregistrement.
- [x] **Lot 6 — Opérations frontière** : voyages et passages (vols, navires, trains —
      création, ouverture/fermeture de la salle d'enregistrement, suppression),
      personnes rattachées à un passage et réaffectation (PAXChangeFlight), registre
      des navires, référentiels réels des transporteurs (820 compagnies) et terminaux
      (18 opérationnels) — écran « Voyages et transports » à 4 onglets et combo
      « Voyage » au formulaire passeport.
- [x] **Lot 6 — Opérations frontière** : voyages et passages (vols, navires, trains —
      création, ouverture/fermeture de la salle d'enregistrement, suppression),
      personnes rattachées à un passage et réaffectation (PAXChangeFlight), registre
      des navires, référentiels réels des transporteurs (820 compagnies) et terminaux
      (18 opérationnels) — écran « Voyages et transports » à 4 onglets et combo
      « Voyage » au formulaire passeport.
- [x] **Tableau de bord du poste** : écran d'accueil réel (héritage : schéma `dashboard`) —
      transactions du jour avec comparaison sur trois ans, arrivées/départs, inspecteurs
      actifs, personnes douteuses, voyages ouverts et vols, refus et amendes, ventilation
      par poste frontière et nationalités ; réglages de poste semés (BorderPoint, Terminal).
- [ ] Lot 7 — Échanges (EDIFACT, API passagers, connecteurs)
- [ ] Lot 8 — Rapports (QuestPDF) et tableaux de bord
- [ ] Lot 9 — Migration des données et durcissement

## Tests

```bash
dotnet test                           # tous les tests
dotnet test --filter PasswordHasher   # une classe
```

Test E2E du flux de connexion (pilote l'application réelle : connexion → changement de
mot de passe → fenêtre principale) :

```powershell
powershell -ExecutionPolicy Bypass -File tools/test-login-flow.ps1
```

CI : `.github/workflows/ci.yml` (build + tests sur windows-latest).
