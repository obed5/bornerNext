# Reprise de l'historique

Passage du système précédent à l'application : reprise des dossiers (personnes, documents,
mouvements), des visas, des permis, des listes de surveillance et des dossiers douteux.

## 1. Principe

- **Aucune donnée n'est modifiée dans la base d'origine** : la reprise lit (connexion en
  lecture seule recommandée), la cible est la base de l'application.
- **Le dossier frontalier est le pivot** : le document de voyage est créé au moment du
  premier mouvement qui le référence, avec la personne de ce mouvement (l'origine ne lie
  pas un document à une personne ailleurs).
- **Rapprochement par identifiant d'origine** : chaque ligne reprise conserve son
  identifiant d'origine (colonne technique dédiée) ; relancer la commande ne réinsère donc
  rien (reprise reprenable, utile si le traitement est interrompu).
- **Aucune donnée n'est inventée** : une référence introuvable est comptée comme rejet avec
  son motif ; le mouvement concerné n'est pas repris tant que la référence manque.
- **Aucune donnée personnelle n'est copiée dans le code, les journaux ou la documentation** :
  le rapport ne contient que des comptages, des motifs et des identifiants techniques.

## 2. Ce qui est repris

| Section | Origine (tables) | Cible | Observations |
|---|---|---|---|
| Nomenclatures (44 types) | `lDocumentTypes`, `lSex`, `lTravelReasons`, `lVisaTypes`, `lOccupation`, `lMaritalStatus`… | `LookupTypes` / `Lookups` / `LookupCultures` | Rapprochement par code externe, puis par libellé ; les éléments absents de la cible sont créés avec leurs libellés fr/en |
| Pays | `lCountries` (+ cultures) | `Countries` (+ cultures) | Rapprochement par identifiant d'origine, puis par code alpha-3 |
| Postes frontières | `lBorderPoints` (+ cultures) | `BorderPoints` (+ cultures) | Identifiant d'origine conservé |
| Personnes | `Persons` | `Persons` | Fiche légère (l'état civil est porté par le document) |
| Documents | `Documents` | `TravelDocuments` | Créés au fil des mouvements, rattachés à la personne du mouvement |
| Photos et documents scannés | `Photos` | `DocumentImages` | Sur option (`--with-photos`) : volumétrie importante |
| Mouvements | `PassportRegistrationData` | `Movements` | Sens, motif, refus, pays, poste, inspecteur, horodatages |
| Enfants rattachés | `Children` | `MovementChildren` | Unicité par (mouvement, nom, prénom, naissance) |
| Visas | `Visa`, `VisaChildren` | `Visas`, `VisaChildren` | Numéro conservé tel quel ; rattachement du mouvement fait après la reprise des visas |
| Permis | `Permit` (+ antécédents médicaux) | `Permits`, `PermitMedicalHistories` | Titulaire ; les personnes à charge du permis relèvent d'une reprise ultérieure |
| Listes de surveillance | `PersonsStopList`, `DocumentsStopList` | `PersonStopLists`, `DocumentStopLists` | Variantes de translittération, sources, annulations, autorisations |
| Dossiers douteux | `DoubtablePersons` | `DoubtablePersons` | Statut, action, notes d'inspecteur et de superviseur, refus consigné |

Non repris : comptes utilisateurs (les mots de passe d'origine ne sont pas réutilisables :
créer les comptes dans l'application, ou réinitialiser avec `reset-password`), réglages
propres au poste d'origine (à revoir dans Administration → Paramètres), journaux et
télémétrie de l'origine.

## 3. Mise en œuvre

```powershell
# 1. Répétition générale : aucun écrit, rapport détaillé
BorderNext.Migration.exe migrate `
  --source "Server=SRV-ORIGINE;Database=AncienneBase;User Id=lecture;Password=***;TrustServerCertificate=True;ApplicationIntent=ReadOnly" `
  --target "Server=SRV-CIBLE;Database=BorderNext;User Id=bordernext;Password=***;TrustServerCertificate=True" `
  --dry-run --report rapport-simulation.json

# 2. Contrôler le rapport (comptages et motifs de rejet), puis reprise réelle
BorderNext.Migration.exe migrate --source "…" --target "…" --report rapport-reprise.json

# 3. Avec photos (volumétrie importante) et par lots de 500
BorderNext.Migration.exe migrate --source "…" --target "…" --with-photos --page 500

# 4. Contrôle final
BorderNext.Migration.exe inspect --target "…"
```

Options utiles : `--only lookups,countries,border-points,persons,movements,visas,permits,watchlists,doubtful`
(traitement par section), `--verbose` (journal détaillé), `--command-timeout 300` (base lente),
`--log-sql` (diagnostic des requêtes).

## 4. Ordre et contrôles d'intégrité

1. Nomenclatures → pays → postes : sans eux, aucune référence ne peut être rapprochée.
2. Personnes, puis mouvements (documents créés à la volée avec leur personne).
3. Visas et enfants de visa, puis rattachement des visas aux mouvements.
4. Permis et antécédents médicaux.
5. Listes de surveillance, dossiers douteux.

Contrôles à effectuer sur le rapport :

| Contrôle | Attendu |
|---|---|
| `Documents` écrites | ≈ mouvements repris (documents distincts) |
| `PassportRegistrationData` rejetées | faible ; en cas de rejet « document absent de la cible », vérifier les documents orphelins dans l'origine |
| `Visa` rejetées | faible ; « document porteur absent » signale des visas sans mouvement |
| Total reprises | cohérent avec la volumétrie de l'origine |

Un rejeu de la même commande doit afficher `écrites 0` et `ignorées = total` : c'est la
preuve que la reprise est idempotente.

## 5. Volumétrie et durée

| Volumétrie (origine) | Durée indicative | Remarque |
|---|---|---|
| 3 000 mouvements | quelques minutes | Jeu d'essai |
| 68 000 mouvements / 22 000 personnes (ordre de grandeur de la production observée) | 1 à 3 heures selon le serveur | Traiter par lots ; photos incluses, prévoir l'espace disque |

Conseil : effectuer la première reprise complète sur une copie de la base cible (ou sur une
base dédiée) pour mesurer la durée avant la bascule.

## 6. Précautions

1. **Base d'origine en lecture seule** : utiliser un compte de lecture
   (`ApplicationIntent=ReadOnly`) et, si possible, une copie de sauvegarde restaurée.
2. **Données personnelles** : la reprise réplique des données réelles — elle s'exécute dans
   l'environnement du site, sur des postes et serveurs autorisés. Aucune donnée n'est
   transmise à l'extérieur, aucun export de données n'est produit par l'outil.
3. **Fenêtre d'exploitation** : planifier la reprise hors service ; la base cible peut être
   utilisée pendant la reprise, mais les éditions sont plus lentes (statistiques en cours de
   constitution).
4. **Après la reprise** : recalculer les statistiques (`UPDATE STATISTICS`, voir
   `exploitation.md`), contrôler `inspect`, ouvrir quelques dossiers repris dans
   l'enregistrement passeport pour vérifier la restitution (identité, photo, mouvements).
5. **Origine conservée en lecture** jusqu'à la validation définitive de la reprise.

## 7. Après la bascule

- Les comptes utilisateurs sont créés dans l'application (rôles et poste) ; aucun mot de
  passe d'origine n'est repris.
- Les listes de surveillance reprises continuent de déclencher l'alarme à l'enregistrement
  (mêmes critères de correspondance).
- Les réglages du poste sont à revoir : pays, devise, terminal, sens par défaut, durée de
  conservation des fichiers, connecteurs.
- Conserver la base d'origine en archive selon la politique du site ; ne pas la laisser
  accessible en écriture.
