# BorderNext

Application de gestion d'un poste frontière : enregistrement des voyageurs et de leurs
documents (passeports, visas), biométrie, opérations de transport (vols, navires, trains),
permis de séjour, refus d'entrée et sanctions, listes de surveillance, supervision du poste
par un tableau de bord — interface WPF en français/anglais, données SQL Server.

> Nom provisoire « BorderNext » — renommable ultérieurement sans impact sur le code.

## Ce que fait l'application

| Module | Fonctions |
|---|---|
| **Tableau de bord** (écran d'accueil) | Transactions du jour avec comparaison sur trois ans, arrivées/départs, inspecteurs actifs, personnes douteuses en attente, voyages ouverts et vols du jour, refus et amendes, ventilation par poste frontière, nationalités les plus représentées avec carte schématique du jour. Rafraîchissement automatique. |
| **Enregistrement passeport (F5)** | Personnes et documents de voyage (photo, scan), mouvements entrée/sortie avec les contrôles d'admission (complétude du document, validité à la date de voyage, visa), rattachement à un voyage en salle d'enregistrement ouverte, historique des passages, alarme immédiate si la personne ou le document figure sur une liste de surveillance. |
| **Visas** | Délivrance et suivi des visas (numérotation propre, période, tarifs, droits), rattachement au document, enfants rattachés, recherche. |
| **Biométrie** | Empreintes des voyageurs (10 doigts, contrôle de doublon avant enrôlement), enrôlement biométrique des opérateurs et connexion par empreinte. Abstractions matériel (caméra, lecteur d'empreintes, lecteur de documents MRTD) fournies avec des simulateurs de démonstration (images synthétiques, MRZ TD3 conforme ICAO 9303) ; un pilote réel se branche derrière les mêmes interfaces. |
| **Voyages et transports** | Vols, navires et trains : création d'un voyage et de son passage, ouverture/fermeture de la salle d'enregistrement, suppression, personnes rattachées et réaffectation sur un autre voyage, registre des navires, référentiels des transporteurs et terminaux. |
| **Permis** | Permis de séjour : résidence, contacts, permis de conduire, antécédents, voyage et activité, indépendants, personnes à charge, statuts et révocations. Antécédents médicaux du titulaire et de ses personnes à charge (maladies contractées, vaccinations, état de santé), imprimables. |
| **Refus et sanctions** | Refus d'entrée avec base légale imprimable, ordres de reconduite et avis d'inadmissibilité, amendes (copies document/titulaire/visa au moment des faits) avec encaissement et reçu. |
| **Surveillance** | Listes de surveillance personnes et documents (sources, variantes orthographiques, action à mener, annulation motivée, autorisation de lever), moteur de correspondance flou (distance d'édition, découpage des noms, noms inversés, seuil renforcé pour les nationaux), empreintes de liste d'alerte, personnes douteuses en seconde inspection (décision du superviseur), simulateur de correspondance et contrôle inverse contre le registre. |
| **Échanges** | Listes de passagers EDIFACT PAXLST : import depuis les compagnies (enveloppes UNB/UNH/UNT/UNZ, libérations, messages multiples), conservation du fichier d'origine, rapprochement des passagers avec les dossiers locaux et inscription en seconde inspection des passagers douteux ; export PAXLST (régénération des messages reçus ou déclaration des mouvements du poste) et export XML inter-postes ; contrôles externes SIMIEC (immigration), Belize (cartes et permis) et système aérien (liste noire, dernier mouvement) en mode simulé ou direct ; journal des échanges (statuts, comptages, erreurs unitaires) et configuration des connecteurs. |
| **Éditions et rapports** | Catalogue de 47 éditions en 8 familles (impressions individuelles, mouvements, secondes inspections, nationalités, profils et permis, transport, échanges, santé) : fiches d'enregistrement, de visa, de permis, de refus, d'amende, d'ordre de reconduite et d'antécédents médicaux ; synthèses, mouvements journaliers/hebdomadaires/mensuels, registres de passages, statistiques par nationalité, sens, type de document, motif de voyage, tranches d'âge et poste frontière, statistiques par vol, doutes et décisions, nationalités cartographiées. Aperçu à l'écran (tableaux et synthèses), export PDF (QuestPDF) et impression ; critères par période, poste, sens, pays, type de document, visa, vol, inspecteur et état civil. |
| **Fiche sanitaire** | Questionnaires de santé configurés (questions à choix, dates, texte libre, questions de suivi conditionnelles), application à un dossier de voyageur avec validité paramétrable, saisie et enregistrement des réponses, validation produisant le message de fin (libre pratique, orientation médicale, surveillance) et impression de la fiche vierge. |
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
dotnet test                           # tests (384 actuellement)
dotnet run --project src/BorderNext.UI
```

Au premier lancement :
1. les migrations sont appliquées automatiquement (base `BorderNext` créée si absente) ;
2. un compte administrateur initial est créé (identifiant `admin`) avec le mot de passe
   d'amorçage — fourni par la variable d'environnement `BORDERNEXT_BOOTSTRAP_PASSWORD`,
   sinon généré aléatoirement et déposé une seule fois dans
   `%ProgramData%\BorderNext\mot-de-passe-initial.txt` (aucun mot de passe en configuration) ;
3. le changement de mot de passe est **obligatoire** à la première connexion ;
4. les référentiels sont semés (idempotent, section par section) : nomenclatures,
   pays/villes, postes frontières, transporteurs et terminaux, paramétrage de départ
   et droits par défaut.

Administration : menu **Modules → Administration** (rôles ADMIN/SUPERUSER, ou selon la
matrice des droits `ControlsToRoles`) — 6 onglets : référentiels, pays & géographie,
postes frontières, utilisateurs & droits, paramètres, journal d'audit.

Configuration : `src/BorderNext.UI/appsettings.json` (culture par défaut ; chaîne de
connexion de développement). En production, la chaîne de connexion et le mot de passe
d'amorçage se fournissent par variable d'environnement
(`BORDERNEXT_CONNECTION_STRING`, `BORDERNEXT_BOOTSTRAP_PASSWORD`) — voir
[docs/installation.md](docs/installation.md).
Le répertoire de conservation des fichiers échangés (paramètre
`Exchange_MessageDirectory`) et les points d'accès des systèmes partenaires se règlent
dans l'application (module **Échanges → Connecteurs**), connecteurs par défaut en mode
simulé.

## Sécurité

- Mots de passe hachés PBKDF2-HMAC-SHA256 (210 000 itérations, sel aléatoire) — jamais en clair.
- **Verrouillage temporaire** après échecs de connexion répétés (5 par défaut, 15 minutes),
  compteurs audités ; changement de mot de passe imposé à la première connexion.
- Requêtes exclusivement paramétrées (EF Core) — aucune concaténation SQL, y compris dans
  l'outil de reprise d'historique.
- **Secrets protégés** : aucun mot de passe en configuration. Le mot de passe d'amorçage
  vient de la variable d'environnement `BORDERNEXT_BOOTSTRAP_PASSWORD` ou d'un fichier
  protégé DPAPI (généré aléatoirement au premier démarrage à défaut) ; la chaîne de
  connexion peut venir de `BORDERNEXT_CONNECTION_STRING` ; les jetons des partenaires sont
  chiffrés au repos et jamais réaffichés.
- Journal d'audit par événements (`AUDIT_LOG_*`) ; journal des échanges par exécution.
- Aucun mot de passe maître ni backdoor : l'accès de secours passe par
  `BorderNext.Migration reset-password`, opération tracée.
- Rétention et purge des fichiers échangés ; modèle de menace et revue dans
  [docs/securite.md](docs/securite.md).

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
- [x] **Lot 7 — Échanges** : messages de listes de passagers EDIFACT (PAXLST) à l'import
      comme à l'export, conservation des fichiers d'origine, rapprochement des passagers
      avec les dossiers locaux, passagers douteux avec décision du superviseur, contrôles
      externes (SIMIEC, Belize, système aérien) en mode simulé ou direct, journal des
      échanges et configuration des connecteurs.
- [x] **Tableau de bord du poste** : écran d'accueil décrit ci-dessus.
- [x] **Lot 8 — Éditions, questionnaires santé et antécédents médicaux** : catalogue de
      47 éditions (fiches individuelles et analyses) avec aperçu, export PDF et impression,
      carte des nationalités du jour, questionnaires de santé (réponses, questions de suivi,
      validité, message de fin) et antécédents médicaux des permis, titulaires et personnes
      à charge.
- [x] **Lot 9 — Migration de l'historique, durcissement et documentation** : reprise des
      dossiers du système précédent (personnes, documents, mouvements, visas, permis, listes
      de surveillance, dossiers douteux) par lots et rejouable, jeu de démonstration
      synthétique, mesure des temps de réponse (toutes les mesures sous la seconde sur la
      volumétrie de référence), verrouillage des comptes, secrets protégés (DPAPI), purge des
      fichiers, documentation complète (installation, exploitation, sécurité, migration,
      matrice de couverture, guides par module).

## Documentation

| Document | Contenu |
|---|---|
| [docs/installation.md](docs/installation.md) | Prérequis, base, secrets, premier démarrage, publication |
| [docs/exploitation.md](docs/exploitation.md) | Sauvegardes, journaux, maintenance, purge, incidents |
| [docs/securite.md](docs/securite.md) | Modèle de menace et revue de sécurité |
| [docs/migration.md](docs/migration.md) | Reprise de l'historique du système précédent |
| [docs/couverture.md](docs/couverture.md) | Matrice fonctionnalité → écran → tests → état |
| [docs/guide/](docs/guide) | Guides utilisateur par module |

## Tests

```bash
dotnet test                           # tous les tests
dotnet test --filter PasswordHasher   # une classe
```

Scénarios de bout en bout (pilotage de l'application réelle par automatisation UI, avec
captures) : `tools/test-login-flow.ps1`, `test-admin-flow.ps1`, puis un scénario par lot
(`tools/test-lot2-flow.ps1` … `tools/test-lot9-flow.ps1`) et
`tools/test-dashboard-flow.ps1` (contrôle croisé écran/base). L'outil d'exploitation
(`src/BorderNext.Migration`) porte la reprise d'historique, le jeu de démonstration, la
mesure des temps de réponse, la purge et la réinitialisation de mot de passe.

```powershell
powershell -ExecutionPolicy Bypass -File tools/test-login-flow.ps1
```

CI : `.github/workflows/ci.yml` (build + tests sur windows-latest).
