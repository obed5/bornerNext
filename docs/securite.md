# Sécurité — modèle de menace et revue

Document de revue de sécurité remis avec l'application : ce que protège le système, contre
quoi, et les mesures en place. À relire à chaque évolution majeure.

## 1. Périmètre et biens à protéger

| Bien | Sensibilité | Où il vit |
|---|---|---|
| Dossiers des voyageurs (identité, photo, empreintes, mouvements) | Données personnelles sensibles | Base SQL Server + répertoire d'images |
| Listes de surveillance et dossiers douteux | Sensible — diffusion restreinte | Base |
| Journaux d'audit | Sensible (traçabilité) | Base + export CSV |
| Comptes et secrets techniques | Critique | Base (hachés) + magasin DPAPI du poste |
| Fichiers d'échange (listes de passagers, exports) | Sensible | Répertoire d'échanges |

## 2. Acteurs et menaces

| Menace | Scénario | Mesure en place |
|---|---|---|
| Accès non autorisé au poste | Vol de session, tentative de devinette de mot de passe | Mots de passe PBKDF2-HMAC-SHA256 (210 000 itérations, sel aléatoire), changement imposé à la première connexion, **verrouillage temporaire** après 5 échecs (15 min, paramétrable), comptes nominatifs, révocation de compte |
| Escalade de privilèges | Un agent accède à un écran réservé | Rôles (ADMIN, SUPERUSER, officiers) + matrice de droits par contrôle (`ControlsToRoles`, par poste), contrôle à l'ouverture des écrans |
| Fuite par les journaux | Écrire des données personnelles dans un journal technique | Les journaux techniques ne portent que des événements et des identifiants ; les données d'audit sont dans la table d'audit, exportée et archivée selon la politique du site |
| Altération de preuve | Modification d'un dossier sans trace | Audit par événements (ajout, modification, révocation, recherche, export, impression, échanges), horodatage, machine et numéro de poste |
| Injection SQL | Requête construite par concaténation | EF Core uniquement ; requêtes paramétrées ; l'outil de reprise n'utilise que des commandes paramétrées |
| Vol de secrets | Mot de passe dans un fichier de configuration, jeton de partenaire en clair | Aucun mot de passe en configuration ; mot de passe d'amorçage par variable d'environnement ou fichier protégé DPAPI ; jetons des connecteurs **chiffrés au repos** et jamais réaffichés ; chaîne de connexion par variable d'environnement |
| Rejeu / usurpation vers un partenaire | Appel d'un service externe sans contrôle | Contrôle externe journalisé par canal et par référence ; jeton porteur du partenaire envoyé en en-tête, jamais journalisé |
| Perte de données | Panne matérielle, erreur d'exploitation | Sauvegardes (base + répertoire d'échanges), procédure de restauration testée (`exploitation.md`) |
| Fuite par export | Extraction massive de dossiers | Exports journalisés (`AUDIT_LOG_DataExported`), recherche limitée au poste, pas d'accès distant à l'interface |
| Poste compromis | Logiciel malveillant sur le poste | Recommandations système : compte de service dédié, droits restreints sur les répertoires, mise à jour Windows, antivirus, pare-feu (seuls les flux partenaires sortants nécessaires) |

Hors périmètre : sécurité physique du poste, sécurité du réseau, sécurité du serveur SQL et
de son instance, protection des sauvegardes, conformité des traitements (registre, durée de
conservation légale des données personnelles) — à traiter par l'administration du site.

## 3. Mesures techniques (état livré)

1. **Authentification** : mot de passe ou empreinte d'opérateur, verrouillage temporaire,
   changement de mot de passe à la première connexion, historique de dernière connexion.
2. **Autorisation** : rôles et droits par contrôle et par poste ; menu soumis aux droits.
3. **Secrets** : DPAPI (portée utilisateur) pour le mot de passe d'amorçage et les jetons des
   connecteurs ; variable d'environnement pour la chaîne de connexion ; aucun secret dans le
   dépôt de sources, les journaux ou les exports.
4. **Requêtes** : EF Core paramétré partout ; aucune concaténation de valeurs.
5. **Traçabilité** : journal d'audit par événements, journal d'échange par exécution
   (statut, comptages, erreurs unitaires), journaux techniques rotatifs.
6. **Retrait des accès** : révocation de compte, révocation d'entrée de liste motivée,
   rétention et purge des fichiers d'échange.
7. **Interface** : i18n fr/en, aucune télémétrie sortante, aucune dépendance à un service
   en ligne ; les seuls flux sortants sont les partenaires configurés (mode simulé par défaut).
8. **Données de démonstration** : strictement synthétiques, générées par l'outil (aucune
   donnée réelle recopiée).

## 4. Points de vigilance et suites

| Point | État |
|---|---|
| Chiffrement de la base au repos | À la charge du site (TDE SQL Server ou chiffrement disque) — recommandé |
| Chiffrement des flux vers les partenaires (TLS) | À valider avec les partenaires au moment du branchement réel |
| Politique de mots de passe (longueur, complexité, expiration) | Longueur et complexité contrôlées à la saisie ; expiration non imposée (choix à valider avec l'administration) |
| Séparation des tâches | Rôles disponibles (admin / superviseur / officier) ; l'affectation relève du site |
| Durée de conservation des données personnelles | Réglages de purge des fichiers livrés ; la purge des dossiers relève de la politique d'archivage légale |
| Tests d'intrusion | Non réalisés à ce stade ; recommandés avant mise en production |

## 5. Vérifications associées

- `tests/BorderNext.Tests/HardeningTests.cs` : verrouillage (seuil, expiration, biométrie),
  protection des secrets (round-trip, absence de clair), jetons de connecteur en en-tête,
  mot de passe d'amorçage (variable, fichier protégé, génération), rétention des fichiers.
- Contrôle automatisé : aucune occurrence de mot de passe maître ni de chaîne de connexion
  en clair dans le dépôt ; requêtes SQL sans concaténation (revue de code).
