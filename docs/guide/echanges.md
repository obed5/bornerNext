# Guide — Échanges

**Accès** : menu **Modules → Échanges**.

## Recevoir une liste de passagers (PAXLST)

1. Onglet **Messages** : **Importer un fichier** (message EDIFACT de la compagnie).
2. L'application analyse l'enveloppe, conserve le fichier d'origine, crée le message et ses
   lignes (passagers et équipage).
3. **Rapprocher les passagers** : chaque passager est comparé aux dossiers locaux (numéro de
   document) et aux listes de surveillance ; les correspondances de liste passent en
   seconde inspection.
4. Sélectionner un message pour consulter ses passagers et leurs statuts.

## Transmettre les données du poste

- **Exporter PAXLST** : régénère les messages reçus ou déclare les mouvements du poste
  groupés par vol ou par passage.
- **Exporter XML** : export inter-postes des messages de la période.

## Contrôles externes

1. Onglet **Contrôles** : choisir le système partenaire (registre de l'immigration, cartes et
   permis, système aérien), saisir le document et l'état civil.
2. **Contrôler** : le verdict s'affiche (aucune trace, opposition, indéterminé, erreur) avec
   le résumé du partenaire ; l'historique des contrôles de la session reste affiché.
3. En mode simulé, les verdicts sont déterministes et annoncés comme tels.

## Journal et connecteurs

- Onglet **Journal** : exécutions par canal (statut, comptages, erreurs unitaires).
- Onglet **Connecteurs** : point d'accès, mode (simulé/direct), activation, **jeton
  d'authentification** du partenaire (chiffré au repos, jamais réaffiché) et **purge** des
  fichiers anciens.

## Remarques

- Les passagers douteux d'un message se traitent dans l'onglet **Passagers douteux** (même
  circuit que la file de seconde inspection).
- Le rapprochement utilise le numéro de document : une liste de passagers sans numéros
  exploitables ne produira pas de correspondance.
