# BorderNext

Application de gestion d'un poste frontière : enregistrement des voyageurs et de leurs
documents (passeports, visas), biométrie, opérations de transport (vols, navires, trains),
permis de séjour, refus d'entrée et sanctions, listes de surveillance, supervision du poste
par un tableau de bord — interface WPF en français/anglais, données SQL Server.

> Nom provisoire « BorderNext » — renommable ultérieurement sans impact sur le code.

## Ce que fait l'application

| Module | Fonctions |
|---|---|
| **Tableau de bord** (écran d'accueil) | Transactions du jour avec comparaison sur trois ans, arrivées/départs, inspecteurs actifs, personnes douteuses en attente, voyages ouverts et vols du jour, refus et amendes, ventilation par poste frontière, nationalités les plus représentées. Rafraîchissement automatique. |
| **Enregistrement passeport (F5)** | Personnes et documents de voyage (photo, scan), mouvements entrée/sortie avec les contrôles d'admission (complétude du document, validité à la date de voyage, visa), rattachement à un voyage en salle d'enregistrement ouverte, historique des passages, alarme immédiate si la personne ou le document figure sur une liste de surveillance. |
| **Visas** | Délivrance et suivi des visas (numérotation propre, période, tarifs, droits), rattachement au document, enfants rattachés, recherche. |
| **Biométrie** | Empreintes des voyageurs (10 doigts, contrôle de doublon avant enrôlement), enrôlement biométrique des opérateurs et connexion par empreinte. Abstractions matériel (caméra, lecteur d'empreintes, lecteur de documents MRTD) fournies avec des simulateurs de démonstration (images synthétiques, MRZ TD3 conforme ICAO 9303) ; un pilote réel se branche derrière les mêmes interfaces. |
| **Voyages et transports** | Vols, navires et trains : création d'un voyage et de son passage, ouverture/fermeture de la salle d'enregistrement, suppression, personnes rattachées et réaffectation sur un autre voyage, registre des navires, référentiels des transporteurs et terminaux. |
| **Permis** | Permis de séjour : résidence, contacts, permis de conduire, antécédents, voyage et activité, indépendants, personnes à charge, statuts et révocations. |
| **Refus et sanctions** | Refus d'entrée avec base légale imprimable, ordres de reconduite et avis d'inadmissibilité, amendes (copies document/titulaire/visa au moment des faits) avec encaissement et reçu. |
| **Surveillance** | Listes de surveillance personnes et documents (sources, variantes orthographiques, action à mener, annulation motivée, autorisation de lever), moteur de correspondance flou (distance d'édition, découpage des noms, noms inversés, seuil renforcé pour les nationaux), empreintes de liste d'alerte, personnes douteuses en seconde inspection (décision du superviseur), simulateur de correspondance et contrôle inverse contre le registre. |
| **Administration** | 6 onglets : nomenclatures (44 types, 175 éléments fr/en), pays et géographie (220 pays), postes frontières, utilisateurs et droits (`ControlsToRoles`), paramètres, journal d'audit avec export CSV. |

Transversal : authentification et rôles (ADMIN, SUPERUSER, officiers), journal d'audit
par événements, interface bilingue français/anglais, contrôle d'accès aux écrans par
la matrice des droits.

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
dotnet test                           # tests (240 actuellement)
dotnet run --project src/BorderNext.UI
```

Au premier lancement :
1. les migrations sont appliquées automatiquement (base `BorderNext` créée si absente) ;
2. un compte administrateur initial est créé : **admin / ChangeMe!2026** ;
3. le changement de mot de passe est **obligatoire** à la première connexion ;
4. les référentiels sont semés (idempotent, section par section) : nomenclatures,
   pays/villes, postes frontières, transporteurs et terminaux, paramétrage de départ
   et droits par défaut.

Administration : menu **Modules → Administration** (rôles ADMIN/SUPERUSER, ou selon la
matrice des droits `ControlsToRoles`) — 6 onglets : référentiels, pays & géographie,
postes frontières, utilisateurs & droits, paramètres, journal d'audit.

Configuration : `src/BorderNext.UI/appsettings.json` (chaîne de connexion, culture par
défaut, mot de passe bootstrap — à remplacer en production par un secret protégé).

## Sécurité

- Mots de passe hachés PBKDF2-HMAC-SHA256 (210 000 itérations, sel aléatoire) — jamais en clair.
- Requêtes exclusivement paramétrées (EF Core) — aucune concaténation SQL.
- Journal d'audit par événements (`AUDIT_LOG_*`).
- Aucun mot de passe maître ni backdoor.
- Le mot de passe d'amorçage est un défaut de développement ; son déplacement vers un
  secret protégé fait partie des travaux de durcissement à venir.

## Feuille de route

- [x] **Lot 0 — Socle** : solution, base + migrations, auth/rôles, i18n fr/en, audit, tests, CI.
- [x] **Lot 1 — Référentiels et administration** : nomenclatures, pays/villes, postes
      frontières, paramètres, comptes et droits, journal d'audit avec export CSV.
- [x] **Lot 2 — Passeports et visas** : personnes, documents de voyage, mouvements
      entrée/sortie avec contrôles d'admission, visas avec numérotation et enfants,
      recherche documentaire.
- [x] **Lot 3 — Biométrie et documents** : abstraction matériel + simulateurs, empreintes
      des voyageurs, enrôlement des opérateurs, connexion par empreinte, lecture automatique
      du document au formulaire passeport.
- [x] **Lot 4 — Permis et enforcement** : permis (résidence, contacts, permis de conduire,
      antécédents, voyage, indépendant, personnes à charge), refus d'entrée avec base légale,
      ordres de reconduite, amendes avec encaissement.
- [x] **Lot 5 — Listes de surveillance et personnes douteuses** : listes personnes/documents,
      moteur de correspondance à 7 critères personnes et 6 critères documents, workflow
      douteux 1→2→3, empreintes de liste, simulateur, contrôle inverse.
- [x] **Lot 6 — Opérations frontière** : voyages et passages (vols, navires, trains),
      personnes rattachées et réaffectation, registre des navires, transporteurs et terminaux.
- [x] **Tableau de bord du poste** : écran d'accueil décrit ci-dessus.
- [ ] **Lot 7 — Échanges** : messages EDIFACT (PAXLST), API passagers, connecteurs externes.
- [ ] **Lot 8 — Rapports** : éditions (QuestPDF), manifestes, résultats et questionnaires.
- [ ] **Lot 9 — Migration des données et durcissement** : reprise des données historiques,
      secrets protégés, revue de sécurité.

## Tests

```bash
dotnet test                           # tous les tests
dotnet test --filter PasswordHasher   # une classe
```

Scénarios de bout en bout (pilotage de l'application réelle par automatisation UI, avec
captures) : `tools/test-login-flow.ps1`, `test-admin-flow.ps1`, puis un scénario par lot
(`tools/test-lot2-flow.ps1` … `tools/test-lot6-flow.ps1`) et
`tools/test-dashboard-flow.ps1` (contrôle croisé écran/base).

```powershell
powershell -ExecutionPolicy Bypass -File tools/test-login-flow.ps1
```

CI : `.github/workflows/ci.yml` (build + tests sur windows-latest).
