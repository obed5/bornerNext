# Matrice de couverture

Chaque fonctionnalité de l'application, l'écran qui la porte, les tests qui la couvrent et
son état. Les tests sont dans `tests/BorderNext.Tests` (xUnit, 384 tests au 16/10/2026) ;
les scénarios de bout en bout pilotent l'application réelle (`tools/test-*-flow.ps1`).

| # | Fonctionnalité | Écran / composant | Tests | Scénario | État |
|---|---|---|---|---|---|
| 1 | Connexion par mot de passe, rôles, changement obligatoire | Connexion | `AuthServiceTests`, `PasswordHasherTests` | `test-login-flow.ps1` | ✅ |
| 2 | Verrouillage temporaire après échecs répétés | Connexion | `HardeningTests` (5 tests) | `test-lot9-flow.ps1` | ✅ |
| 3 | Connexion par empreinte d'opérateur | Connexion | `BiometricsAndDevicesTests`, `HardeningTests` | `test-lot3-flow.ps1` | ✅ |
| 4 | Journal d'audit par événements + export | Administration → Audit | `UserAdminServiceTests` | `test-admin-flow.ps1` | ✅ |
| 5 | Nomenclatures (44 types, 175 éléments fr/en) | Administration → Référentiels | `ReferenceDataServiceTests` | `test-admin-flow.ps1` | ✅ |
| 6 | Pays, provinces, villes (220 pays) | Administration → Pays | `GeoServiceTests` | `test-admin-flow.ps1` | ✅ |
| 7 | Postes frontières | Administration → Postes | `UserAdminServiceTests` | `test-admin-flow.ps1` | ✅ |
| 8 | Utilisateurs, droits par contrôle et rôle | Administration → Utilisateurs | `UserAdminServiceTests` | `test-admin-flow.ps1` | ✅ |
| 9 | Paramètres du poste (portée poste/utilisateur) | Administration → Paramètres | `SettingsServiceTests` | `test-admin-flow.ps1` | ✅ |
| 10 | Enregistrement passeport (personne + document) | Enregistrement passeport | `DossierServicesTests` | `test-lot2-flow.ps1` | ✅ |
| 11 | Photo et document scanné, lecture MRTD simulée | Enregistrement passeport | `BiometricsAndDevicesTests` | `test-lot3-flow.ps1` | ✅ |
| 12 | Mouvement entrée/sortie avec contrôles d'admission | Enregistrement passeport | `DossierServicesTests` | `test-lot2-flow.ps1` | ✅ |
| 13 | Historique des passages d'un dossier | Enregistrement passeport | `DossierServicesTests` | `test-lot2-flow.ps1` | ✅ |
| 14 | Visas : délivrance, enfants, tarifs, recherche | Visas | `DossierServicesTests` | `test-lot2-flow.ps1` | ✅ |
| 15 | Empreintes voyageur : capture, doublon, réaffichage | Biométrie | `BiometricsAndDevicesTests` | `test-lot3-flow.ps1` | ✅ |
| 16 | Enrôlement biométrique des opérateurs | Biométrie → Opérateurs | `BiometricsAndDevicesTests` | `test-lot3-flow.ps1` | ✅ |
| 17 | Voyages et passages (vols, navires, trains) | Voyages et transports | `BorderOpsTests` | `test-lot6-flow.ps1` | ✅ |
| 18 | Ouverture/clôture de salle d'enregistrement | Voyages et transports | `BorderOpsTests` | `test-lot6-flow.ps1` | ✅ |
| 19 | Personnes rattachées, réaffectation de voyage | Voyages et transports | `BorderOpsTests` | `test-lot6-flow.ps1` | ✅ |
| 20 | Registre des navires, transporteurs, terminaux | Voyages et transports | `BorderOpsTests` | `test-lot6-flow.ps1` | ✅ |
| 21 | Permis de séjour : résidence, contacts, conduite, antécédents | Permis | `PermitsAndEnforcementTests` | `test-lot4-flow.ps1` | ✅ |
| 22 | Permis : voyage, indépendant, personnes à charge | Permis | `PermitsAndEnforcementTests` | `test-lot4-flow.ps1` | ✅ |
| 23 | Antécédents médicaux (titulaire et personnes à charge) | Permis → Antécédents médicaux | `ReportsAndHealthTests` | `test-lot8-flow.ps1` | ✅ |
| 24 | Refus d'entrée avec base légale imprimable | Refus et sanctions | `PermitsAndEnforcementTests` | `test-lot4-flow.ps1` | ✅ |
| 25 | Ordres de reconduite et avis d'inadmissibilité | Refus et sanctions | `PermitsAndEnforcementTests` | `test-lot4-flow.ps1` | ✅ |
| 26 | Amendes, encaissement, reçu | Refus et sanctions | `PermitsAndEnforcementTests` | `test-lot4-flow.ps1` | ✅ |
| 27 | Listes de surveillance personnes et documents | Surveillance | `WatchlistsTests` | `test-lot5-flow.ps1` | ✅ |
| 28 | Correspondance floue (7 critères personnes, 6 documents) | Surveillance | `WatchlistsTests` | `test-lot5-flow.ps1` | ✅ |
| 29 | Empreintes de liste d'alerte, contrôle inverse | Surveillance | `WatchlistsTests` | `test-lot5-flow.ps1` | ✅ |
| 30 | Personnes douteuses : file, décision superviseur 1→2→3 | Surveillance → Douteux | `WatchlistsTests` | `test-lot5-flow.ps1` | ✅ |
| 31 | Alarme immédiate à l'enregistrement | Enregistrement passeport | `WatchlistsTests` | `test-lot5-flow.ps1` | ✅ |
| 32 | Import EDIFACT PAXLST (fichier, enveloppes, doublons) | Échanges → Messages | `ExchangesTests` | `test-lot7-flow.ps1` | ✅ |
| 33 | Rapprochement des passagers avec les dossiers locaux | Échanges → Messages | `ExchangesTests` | `test-lot7-flow.ps1` | ✅ |
| 34 | Passagers douteux d'un message, décision | Échanges → Douteux | `ExchangesTests` | `test-lot7-flow.ps1` | ✅ |
| 35 | Export PAXLST et XML inter-postes | Échanges | `ExchangesTests` | `test-lot7-flow.ps1` | ✅ |
| 36 | Contrôles externes (SIMIEC, Belize, système aérien) | Échanges → Contrôles | `ExchangesConnectorsTests` | `test-lot7-flow.ps1` | ✅ |
| 37 | Journal des échanges (statuts, comptages, erreurs) | Échanges → Journal | `ExchangesTests` | `test-lot7-flow.ps1` | ✅ |
| 38 | Connecteurs : point d'accès, mode, jeton chiffré | Échanges → Connecteurs | `HardeningTests`, `ExchangesConnectorsTests` | `test-lot9-flow.ps1` | ✅ |
| 39 | Éditions : 47 éditions en 8 familles, aperçu | Éditions et rapports | `ReportsAndHealthTests` | `test-lot8-flow.ps1` | ✅ |
| 40 | Export PDF des éditions (QuestPDF) | Éditions et rapports | `ReportsAndHealthTests` | `test-lot8-flow.ps1` | ✅ |
| 41 | Impression (fiches, registres, fiche vierge santé) | Éditions, Permis, Fiche sanitaire | `ReportsAndHealthTests` | `test-lot8-flow.ps1` | ✅ |
| 42 | Fiche sanitaire : questionnaire, validité, conclusion | Fiche sanitaire | `ReportsAndHealthTests` | `test-lot8-flow.ps1` | ✅ |
| 43 | Tableau de bord (jour, 3 ans, vols, refus) | Tableau de bord | `DashboardTests` | `test-dashboard-flow.ps1` | ✅ |
| 44 | Carte des nationalités du jour | Tableau de bord | `DashboardTests` | `test-lot8-flow.ps1` | ✅ |
| 45 | Reprise de l'historique du système précédent | Outil `migrate` | `MigrationTests` (10 tests) | `test-lot9-flow.ps1` | ✅ |
| 46 | Jeu de démonstration synthétique | Outil `seed-demo` | `MigrationTests` | `test-lot9-flow.ps1` | ✅ |
| 47 | Mesure des temps de réponse (< 1 s) | Outil `benchmark` | `MigrationTests` | `test-lot9-flow.ps1` | ✅ |
| 48 | Purge des fichiers selon la durée de conservation | Outil `purge-files`, Échanges → Connecteurs | `HardeningTests` | `test-lot9-flow.ps1` | ✅ |
| 49 | Réinitialisation d'un mot de passe (exploitation) | Outil `reset-password` | — (commandé, journalisé) | `test-lot9-flow.ps1` | ✅ |
| 50 | i18n français / anglais (972 chaînes) | Tous les écrans | parité vérifiée (972 / 972) | tous les scénarios | ✅ |

## Points non couverts ou différés

| Sujet | Motif | Suite |
|---|---|---|
| Éditions personnalisées (moteur de restitution dynamique de l'origine) | Tables d'origine vides, aucun modèle défini par les utilisateurs | À rouvrir si le besoin est confirmé |
| Options d'impression par édition | Organisation et pied de page proviennent des paramètres du poste | Écran de choix par édition à faire |
| Vues « PassengerInfoForDVT » (manifestes) | Sémantique non reprise faute de données | Couvert par les statistiques par vol |
| Export PISCES | Canal supplémentaire de l'origine (statut 0, 40 lignes) | Couvert par l'export inter-postes XML/PAXLST |
| Personnes à charge des permis (historique) | Reprise limitée au titulaire dans un premier temps | À compléter selon besoin (même mécanique) |
| Export tableur des éditions | Les éditions sortent en PDF | À préciser selon besoin |
| Matériel réel (caméra, lecteur d'empreintes, lecteur MRTD) | Simulateurs fournis, interfaces prêtes | Branchement SDK à la demande |
