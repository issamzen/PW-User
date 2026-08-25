# Hostinger Power Copy package — first speed test

This test moves the `DH001` CATPart and CATScript from the developer PC into **private Hostinger storage**. The WPF client obtains a license seat, downloads the package, verifies SHA-256, extracts it to a versioned local cache, and opens the CATPart in CATIA.

> Test-stage security: the `.pcpkg` is currently a ZIP container with a custom extension. It is authenticated and stored outside `public_html`, but it is **not encrypted yet**. This version is intended to validate functionality and speed before adding encryption/signing and temporary extraction.

## Resulting user flow

```text
Sign in
→ authorized catalog loads
→ user clicks Use in CATIA
→ lease.php grants a seat
→ package.php authorizes the request
→ first use downloads and verifies the .pcpkg
→ package is extracted to the versioned local cache
→ CATIA opens the downloaded CATPart
→ native Instantiate From Selection dialog appears
→ Run check executes the downloaded CATScript
→ lease is released
```

On later uses of the same template version, the package is loaded from cache instead of being downloaded again.

---

## 1. Build `DH001_1.5.0.pcpkg` on Windows

Use the supplied script:

```text
HostingerLicenseServerStarter\tools\CreatePowerCopyPackage.ps1
```

Open PowerShell in that folder and run:

```powershell
powershell -ExecutionPolicy Bypass -File ".\CreatePowerCopyPackage.ps1" `
  -TemplateId "DH001" `
  -Version "1.5.0" `
  -PowerCopyName "PC_DogHouse_Slider_Check_v1.5" `
  -CatPartPath "C:\Users\ennad\Downloads\DH_Slider_Check_Source_v001.CATPart" `
  -CatScriptPath "C:\Users\ennad\Downloads\scripts\DogHouse3SliderQuickCheckApi.CATScript" `
  -CheckFunction "RunCheckForTool" `
  -OutputDirectory ".\output"
```

The script creates:

```text
output\DH001_1.5.0.pcpkg
output\DH001_1.5.0_publish.sql
```

The package contains:

```text
manifest.json
template/DH_Slider_Check_Source_v001.CATPart
scripts/DogHouse3SliderQuickCheckApi.CATScript
```

Do not manually rename files inside the package.

---

## 2. Upload the package to private Hostinger storage

In Hostinger File Manager, upload:

```text
DH001_1.5.0.pcpkg
```

to:

```text
domains/catalog.estichara.ma/private_mold/packages/DH001_1.5.0.pcpkg
```

Do **not** put it in `public_html`.

Verify `private_mold/config.php` contains:

```php
'package_directory' => __DIR__ . '/packages',
```

Expected layout:

```text
domains/catalog.estichara.ma/
├── private_mold/
│   ├── config.php
│   └── packages/
│       └── DH001_1.5.0.pcpkg
└── public_html/
    └── mold-api/v1/package.php
```

---

## 3. Upload the updated `package.php`

Upload the updated file from:

```text
HostingerLicenseServerStarter/public_html/mold-api/v1/package.php
```

to:

```text
domains/catalog.estichara.ma/public_html/mold-api/v1/package.php
```

This version returns `X-Package-SHA256`. The desktop client refuses to extract the download if this hash is missing or different.

---

## 4. Register the package in MySQL

Open the generated file:

```text
DH001_1.5.0_publish.sql
```

Run its SQL in phpMyAdmin. It has this form:

```sql
UPDATE templates
SET package_file = 'DH001_1.5.0.pcpkg',
    package_sha256 = 'THE_GENERATED_LOWERCASE_SHA256',
    version = '1.5.0'
WHERE id = 'DH001';
```

Verify:

```sql
SELECT id, version, package_file, package_sha256, is_published
FROM templates
WHERE id = 'DH001';
```

Expected:

```text
id              DH001
version         1.5.0
package_file    DH001_1.5.0.pcpkg
package_sha256  64 hexadecimal characters
is_published    1
```

---

## 5. Build and run the updated WPF client

Use the updated project. Two new source files are included:

```text
PackageManager.cs
PackagePerformanceLog.cs
```

The project now also references:

```text
System.IO.Compression
System.IO.Compression.FileSystem
```

Build steps:

1. close Visual Studio;
2. delete `.vs`, `bin`, and `obj`;
3. open only `ProfessionalPowerCopyCatalogModern.csproj`;
4. verify CATIA COM references and `Embed Interop Types = False`;
5. rebuild;
6. start CATIA and activate a destination CATPart;
7. start the client and sign in.

---

## 6. Measure first-download speed

1. Select `DH001`.
2. Click **Clear cache**.
3. Click **Use in CATIA**.
4. Watch the package line under the CATIA buttons.

During transfer it displays:

```text
Downloading 42% • 8.4 / 20.0 MB
```

After transfer it displays:

```text
Downloaded 20.0 MB in 2.40 s • 8.3 MB/s
```

Complete the native CATIA dialog and click **Run check**.

The local cache is:

```text
%LOCALAPPDATA%\Estichara\MoldAutomationCatalog\Packages\DH001\1.5.0\
```

---

## 7. Measure cached speed

Without clearing the cache, click **Use in CATIA** again.

Expected message:

```text
Loaded package v1.5.0 from local cache in ... ms.
```

No CATPart package is downloaded during this second run. Authentication, catalog, lease and heartbeat requests still go to Hostinger.

---

## 8. Collect speed results from test users

Click **Speed log** in the WPF dashboard. It opens:

```text
%LOCALAPPDATA%\Estichara\MoldAutomationCatalog\Logs\package-performance.csv
```

Columns:

```text
timestamp_utc
computer
template_id
version
mode                 download or cache
bytes
download_seconds
total_prepare_seconds
megabytes_per_second
```

For a useful test, ask each user to send results for:

1. one fresh download after **Clear cache**;
2. one cached launch;
3. office network;
4. home network or VPN if relevant.

Do not ask users to send passwords, access tokens, lease tokens, CATIA documents or customer CAD data.

---

## 9. Updating a package

Every package content update must use a new version, for example:

```text
DH001_1.5.1.pcpkg
```

Then update both:

```text
manifest Version = 1.5.1
MySQL templates.version = 1.5.1
```

The client uses `Id + Version` as the cache key. Changing the version causes a fresh download without changing the EXE.

---

## 10. Expected errors

| Error | Meaning | Fix |
|---|---|---|
| `package_not_authorized_or_not_available` | MySQL package field is empty or user is not authorized | update package fields and entitlement |
| `package_file_missing` | MySQL points to a file that is absent | upload to `private_mold/packages` |
| `package_hash_missing` | updated `package.php` not uploaded or hash is empty | upload PHP and update SHA-256 |
| `package_integrity_error` | uploaded bytes do not match MySQL hash | rebuild/re-upload and run generated SQL |
| `no_seat_available` | all floating seats are occupied | release a seat or wait for lease expiry |
| manifest ID/version mismatch | package and MySQL metadata differ | rebuild using identical ID/version |

---

## 11. Security status of this test

Protected now:

- package is outside `public_html`;
- download needs a valid access token;
- download needs a valid active lease token;
- entitlement and license expiration are checked;
- HTTPS is used;
- SHA-256 is checked by the server and the client;
- extraction blocks path traversal.

Not protected yet:

- package content is not encrypted;
- extracted CATPart and CATScript remain in the user's local cache;
- a licensed user can inspect/copy cached files;
- no digital signature independent from the server database exists yet.

After speed and CATIA compatibility tests pass, the next phase is encrypted/signed `.pcpkg`, temporary extraction, cleanup and key delivery tied to an active lease.
