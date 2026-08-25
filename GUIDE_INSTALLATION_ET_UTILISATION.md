# Mold Automation Catalog — guide clair d’installation et d’utilisation

Ce document décrit **l’état actuel fonctionnel** du projet. Il sépare volontairement :

1. ce qui est stocké sur le PC Windows/CATIA ;
2. ce qui est stocké sur Hostinger ;
3. la création et la modification des utilisateurs ;
4. l’ajout d’une nouvelle Power Copy ;
5. le test complet.

---

## 1. Où va chaque fichier ?

### A. Sur le PC Windows qui exécute CATIA — état actuel

Pour le test déjà configuré, conserver exactement :

```text
C:\Users\ennad\Downloads\DH_Slider_Check_Source_v001.CATPart
C:\Users\ennad\Downloads\scripts\DogHouse3SliderQuickCheckApi.CATScript
```

Le fichier local lu par l’application est :

```text
catalog.json
```

Il doit être placé **dans le même dossier que l’EXE**. Pendant le développement, modifier le `catalog.json` du projet puis reconstruire ; Visual Studio le copie dans le dossier de sortie.

La correspondance actuelle de `DH001` doit être :

```json
{
  "Id": "DH001",
  "CatPartPath": "C:\\Users\\ennad\\Downloads\\DH_Slider_Check_Source_v001.CATPart",
  "PowerCopyName": "PC_DogHouse_Slider_Check_v1.5",
  "CheckScriptDirectory": "C:\\Users\\ennad\\Downloads\\scripts",
  "CheckScriptFile": "DogHouse3SliderQuickCheckApi.CATScript",
  "CheckFunction": "RunCheckForTool"
}
```

Règles importantes :

- le `Id` doit être identique dans le fichier local et dans la base Hostinger ;
- `PowerCopyName` doit être exactement le nom de la Power Copy dans le CATPart ;
- `CheckFunction` doit être exactement la fonction publique du CATScript ;
- le CATPart source reste ouvert pendant la boîte native CATIA **Instantiate From Selection** ;
- ne pas déplacer ou renommer ces fichiers pendant le premier test.

Après validation, on pourra standardiser les installations sous :

```text
C:\ProgramData\Estichara\MoldAutomationCatalog\
├── Templates\DH001\DH_Slider_Check_Source_v001.CATPart
└── Scripts\DH001\DogHouse3SliderQuickCheckApi.CATScript
```

mais ce changement n’est pas nécessaire pour le test actuel.

### B. Sur Hostinger — public

```text
domains/catalog.estichara.ma/public_html/
├── thumbnails/
│   └── DH001.png
└── mold-api/
    └── v1/
        ├── .htaccess
        ├── bootstrap.php
        ├── health.php
        ├── login.php
        ├── me.php
        ├── catalog.php
        ├── lease.php
        ├── heartbeat.php
        ├── release.php
        ├── package.php
        ├── setup_admin.php
        ├── admin_grant_license.php
        └── admin_licenses.php
```

Le fichier public `https://catalog.estichara.ma/catalog.json` n’est **plus la source principale** de l’application avec login. Le client connecté appelle `catalog.php`, qui lit la table MySQL `templates`.

Les miniatures peuvent rester publiques. Exemple :

```text
https://catalog.estichara.ma/thumbnails/DH001.png
```

### C. Sur Hostinger — privé

```text
domains/catalog.estichara.ma/private_mold/
├── config.php
└── packages/
```

- `config.php` contient les identifiants MySQL : jamais dans `public_html` ;
- `packages/` recevra plus tard les `.pcpkg` chiffrés ;
- ne pas mettre de CATPart brut dans `public_html` pour un produit commercial.

**Aujourd’hui, le client utilise encore les CATPart et CATScript locaux.** Le téléchargement/extraction du futur `.pcpkg` n’est pas encore branché. Une nouvelle carte peut venir du serveur sans nouvel EXE, mais elle ne sera utilisable dans CATIA que si son `Id` possède aussi une correspondance locale dans le `catalog.json` à côté de l’EXE.

---

## 2. Corriger l’erreur XAML `MC3024 Background`

L’erreur venait du `Grid` nommé `LoginOverlay` : `Background` était défini une fois comme attribut et une deuxième fois avec `<Grid.Background>`.

La version correcte est :

```xml
<Grid x:Name="LoginOverlay" Grid.Column="0" Grid.ColumnSpan="3" Panel.ZIndex="50">
    <Grid.Background>
        <LinearGradientBrush ... />
    </Grid.Background>
```

La propriété `Background="#F8FAFC"` sur la ligne d’ouverture a été supprimée dans le projet corrigé.

Après remplacement du projet :

1. fermer Visual Studio ;
2. supprimer `.vs`, `bin` et `obj` ;
3. rouvrir uniquement `ProfessionalPowerCopyCatalogModern.csproj` ;
4. vérifier les références COM `INFITF` et `MECMOD` ;
5. mettre `Embed Interop Types = False` pour les deux ;
6. reconstruire.

---

## 3. Créer le premier administrateur — une seule fois

Pour une base déjà créée, importer une seule fois :

```text
migration_002_admin.sql
```

Dans `private_mold/config.php`, activer temporairement :

```php
'setup_enabled' => true,
'setup_key' => 'UNE-CLE-ALEATOIRE-TRES-LONGUE-AU-MOINS-32-CARACTERES',
```

Puis :

1. téléverser les derniers fichiers PHP de `mold-api/v1/` ;
2. renommer `setup_admin_once.html` avec un nom aléatoire ;
3. le téléverser temporairement dans `public_html` ;
4. ouvrir la page ;
5. saisir la clé, l’e-mail administrateur et un mot de passe en clair d’au moins 12 caractères ;
6. cliquer **Create / Reset Admin** ;
7. vérifier la réponse `"ok": true` ;
8. remettre immédiatement `setup_enabled` à `false` ;
9. supprimer définitivement la page HTML temporaire.

L’administrateur se connecte ensuite dans la même application WPF. Le menu **Licenses** apparaît uniquement si le serveur renvoie `role = admin`.

---

## 4. Ajouter un nouvel utilisateur depuis l’application

1. ouvrir le client WPF ;
2. se connecter avec le compte administrateur ;
3. ouvrir **Licenses** dans la barre latérale ;
4. remplir :

```text
Customer email       e-mail du client
Display name         nom affiché
Temporary password   obligatoire pour un nouveau client, minimum 12 caractères
License name         nom commercial de la licence
Seats                nombre de postes simultanés
Days                 durée à partir de maintenant
Entitlement           doghouse-basic
```

5. cliquer **Grant license** ;
6. vérifier que le client apparaît dans **Customer licenses** ;
7. se déconnecter ;
8. tester la connexion avec l’e-mail et le mot de passe temporaire du client.

Pour une licence individuelle, utiliser `Seats = 1`.

---

## 5. Modifier un utilisateur existant

### Depuis l’écran Licenses actuel

Saisir exactement le **même e-mail**, puis cliquer de nouveau **Grant license** :

- `Display name` remplace le nom affiché ;
- laisser `Temporary password` vide conserve le mot de passe actuel ;
- saisir un nouveau mot de passe d’au moins 12 caractères le réinitialise ;
- `Seats` remplace le nombre de sièges ;
- `Days` remet l’expiration à **maintenant + nombre de jours** ;
- `License name` remplace le nom de licence ;
- `Entitlement` ajoute ce droit à la licence.

Important : dans cette version, entrer un autre entitlement **ajoute** le nouveau droit, mais ne supprime pas automatiquement l’ancien.

### Suspension/réactivation — actuellement dans phpMyAdmin

L’écran WPF actuel ne possède pas encore de bouton Suspend/Reactivate. Pour couper immédiatement tout accès d’un utilisateur :

```sql
UPDATE users
SET status = 'suspended'
WHERE email = 'client@example.com';
```

Pour le réactiver :

```sql
UPDATE users
SET status = 'active'
WHERE email = 'client@example.com';
```

Pour laisser le login fonctionner mais suspendre uniquement la licence :

```sql
UPDATE licenses l
JOIN users u ON u.id = l.user_id
SET l.status = 'suspended'
WHERE u.email = 'client@example.com';
```

Pour la réactiver :

```sql
UPDATE licenses l
JOIN users u ON u.id = l.user_id
SET l.status = 'active'
WHERE u.email = 'client@example.com';
```

Pour remplacer les droits d’un client par `doghouse-basic` uniquement :

```sql
DELETE le
FROM license_entitlements le
JOIN licenses l ON l.id = le.license_id
JOIN users u ON u.id = l.user_id
WHERE u.email = 'client@example.com';

INSERT INTO license_entitlements (license_id, entitlement_code)
SELECT l.id, 'doghouse-basic'
FROM licenses l
JOIN users u ON u.id = l.user_id
WHERE u.email = 'client@example.com'
ORDER BY l.id DESC
LIMIT 1;
```

---

## 6. Ajouter une nouvelle Power Copy au catalogue

Exemple : `CL001`.

### Étape 1 — préparer les fichiers locaux

```text
C:\MoldTools\Templates\CL001\Clip_Source_v001.CATPart
C:\MoldTools\Scripts\CL001\ClipQuickCheckApi.CATScript
```

Le CATPart doit contenir une Power Copy avec un nom stable, par exemple :

```text
PC_Clip_Check_v1.0
```

Le CATScript doit exposer une fonction pour l’application, par exemple :

```vb
Function RunCheckForTool()
```

### Étape 2 — téléverser la miniature

```text
public_html/thumbnails/CL001.png
```

URL :

```text
https://catalog.estichara.ma/thumbnails/CL001.png
```

### Étape 3 — ajouter les métadonnées dans MySQL

Pour réutiliser le droit `doghouse-basic` :

```sql
INSERT INTO templates
(id, name, category, version, description, inputs_text, output_text,
 thumbnail_url, powercopy_name, entitlement_code, is_published)
VALUES
('CL001', 'Clip Access Check', 'Clips', '1.0.0',
 'Checks clip access and extraction clearance.',
 'Clip axis • support surface • reference point',
 'Colored clip access envelope',
 'https://catalog.estichara.ma/thumbnails/CL001.png',
 'PC_Clip_Check_v1.0',
 'doghouse-basic', 1)
ON DUPLICATE KEY UPDATE
 name = VALUES(name),
 category = VALUES(category),
 version = VALUES(version),
 description = VALUES(description),
 inputs_text = VALUES(inputs_text),
 output_text = VALUES(output_text),
 thumbnail_url = VALUES(thumbnail_url),
 powercopy_name = VALUES(powercopy_name),
 entitlement_code = VALUES(entitlement_code),
 is_published = VALUES(is_published);
```

`is_published = 1` rend la carte visible aux utilisateurs qui possèdent l’entitlement. `is_published = 0` la masque.

### Étape 4 — ajouter la correspondance locale

Ajouter dans le `catalog.json` à côté de l’EXE :

```json
{
  "Id": "CL001",
  "Name": "Clip Access Check",
  "Category": "Clips",
  "Version": "1.0.0",
  "Thumbnail": "https://catalog.estichara.ma/thumbnails/CL001.png",
  "CatPartPath": "C:\\MoldTools\\Templates\\CL001\\Clip_Source_v001.CATPart",
  "PowerCopyName": "PC_Clip_Check_v1.0",
  "CheckScriptDirectory": "C:\\MoldTools\\Scripts\\CL001",
  "CheckScriptFile": "ClipQuickCheckApi.CATScript",
  "CheckFunction": "RunCheckForTool",
  "IsFavorite": false
}
```

Attention à la virgule JSON entre deux objets. Redémarrer l’application et se reconnecter pour recharger le catalogue serveur.

---

## 7. Test complet de `DH001`

### Préparation

1. vérifier que le CATPart source existe au chemin configuré ;
2. vérifier que le CATScript existe au chemin configuré ;
3. démarrer CATIA V5 ;
4. ouvrir et activer le CATPart de destination ;
5. démarrer l’application WPF ;
6. se connecter avec un utilisateur qui possède `doghouse-basic`.

### Insertion

1. sélectionner **Dog House Slider Check** ;
2. cliquer **Use in CATIA** ;
3. l’API accorde un siège ;
4. CATIA ouvre la source et affiche sa boîte native **Instantiate From Selection** ;
5. sélectionner dans CATIA :

```text
IN_POINT
IN_AXIS
IN_B_SURFACE
```

6. valider avec **OK** dans CATIA ;
7. revenir dans l’application ;
8. cliquer **Run check**.

### Résultat attendu

- le CATScript colore la géométrie ;
- le tableau WPF affiche Slider 00, 20 et 40 ;
- rouge = clash physique ;
- vert = aucun clash physique ;
- orange = erreur ;
- les collisions entre `SLIDER_00`, `SLIDER_20`, `SLIDER_40` et leur conteneur `DH_SLIDER` sont ignorées ;
- la source se ferme ;
- le siège est libéré.

Si l’utilisateur annule la boîte CATIA, cliquer **Close source** pour fermer la source et libérer le siège.

---

## 8. Diagnostic rapide

| Message | Cause probable | Action |
|---|---|---|
| `MC3024 Background already defined` | deux définitions XAML du Background | utiliser le `MainWindow.xaml` corrigé, supprimer `bin/obj/.vs` |
| `server_configuration_error` | chemin de `private_mold/config.php` incorrect | vérifier l’arborescence et `bootstrap.php` |
| `invalid_credentials` / 401 | utilisateur ou hash incorrect | recréer/réinitialiser via la procédure admin, saisir le mot de passe en clair dans le client |
| menu Licenses absent | rôle utilisateur non admin | vérifier `users.role = 'admin'` |
| `no_seat_available` | tous les sièges sont utilisés | fermer/libérer l’ancienne session ou attendre 15 minutes |
| `no local test CATPart mapping` | aucun objet du même `Id` dans le `catalog.json` local | ajouter la correspondance locale |
| `Power Copy not found` | nom différent dans le CATPart | vérifier `PowerCopyName` caractère par caractère |
| `Check script folder/file not found` | chemin local incorrect | vérifier `CheckScriptDirectory` et `CheckScriptFile` |
| CATIA offline | CATIA non démarré avant le client | démarrer CATIA, activer le CATPart cible, relancer le client |

---

## 9. Ce qui est prêt et ce qui reste à faire

### Prêt maintenant

- login serveur ;
- rôle administrateur ;
- création/réinitialisation d’un client ;
- licence individuelle et sièges ;
- catalogue autorisé depuis MySQL ;
- Power Copy/CATScript locaux ;
- insertion avec la boîte native CATIA ;
- lease, heartbeat et release ;
- affichage des résultats du CATScript.

### Pas encore finalisé

- téléchargement et déchiffrement des `.pcpkg` ;
- livraison automatique du CATPart/CATScript à chaque nouveau PC ;
- boutons WPF Suspend/Reactivate ;
- organisations et gestion complète de plusieurs utilisateurs d’entreprise dans l’interface ;
- réinitialisation de mot de passe par e-mail ;
- rate limiting, audit et paiement.

Ne pas présenter la phase actuelle comme une protection commerciale finale tant que les `.pcpkg` chiffrés et signés ne sont pas implémentés.
