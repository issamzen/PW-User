# Modern Mold Automation Catalog — WPF Prototype

Start with **`GUIDE_INSTALLATION_ET_UTILISATION.md`**. For the new authenticated Hostinger package download and speed test, follow **`HOSTINGER_PACKAGE_SPEED_TEST.md`**.

This replaces the legacy-looking WinForms dashboard with a modern WPF catalog experience.

## UI improvements

- branded dark navigation rail with clear Power Copies and About destinations
- in-dashboard About & Account workspace (no separate window)
- account/session card, privilege states, contact links and future licensing rights
- modern searchable template list
- engineering filters for Dog Houses, Clips, Ribs, Bosses and Favorites
- per-item favorite toggle
- large thumbnails and selected-state cards
- side detail workspace with hero image
- category/version/verification badges
- input/output engineering cards
- primary CATIA actions
- modern result cards for Slider 00/20/40
- CATIA connection indicator

## Required CATIA references

Add through **Project > Add COM Reference**:

```text
CATIA V5 InfInterfaces    -> INFITF
CATIA V5 MecModInterfaces -> MECMOD
```

Set both:

```text
Embed Interop Types = False
```

## Configure local CATIA files

The signed-in catalog metadata comes from the Hostinger/MySQL API. The local `catalog.json` is still required in this test version to map each server template `Id` to a CATPart and CATScript installed on the Windows PC.

Edit `catalog.json`:

```text
CatPartPath
PowerCopyName
CheckScriptDirectory
CheckScriptFile
CheckFunction
```

The project copies `catalog.json` and the thumbnail to the output folder automatically.

## Build

The duplicate `LoginOverlay.Background` definition that caused XAML error `MC3024` has been removed.

1. Close Visual Studio and delete `.vs`, `bin`, and `obj` if an older XAML build is cached.
2. Open only `ProfessionalPowerCopyCatalogModern.csproj`.
3. Add INFITF and MECMOD COM references.
4. Match CATIA bitness (`x86` or `x64`).
5. Clean and rebuild.
6. Put `DogHouse3SliderQuickCheckApi.CATScript` in the configured script directory.
7. Start CATIA and activate the destination CATPart.
8. Run the WPF application.

## Workflow

```text
Select card
→ review thumbnail/details
→ Use in CATIA
→ complete native Insert Object dialog
→ Run check
→ CATIA colors update
→ result cards appear in the application
```

`Run check` stays manual for the first version because CATIA's native interactive command is asynchronous. Automatic post-insertion detection can be added after the expected generated Body names are finalized.

## Lifter templates

Templates flagged with `Workflow = "lifter"` (server catalog metadata, package
manifest, or local `catalog.json`) run the integrated **Lifter Studio** — the
full catvba macro ported to the dashboard — from the same **Use in CATIA**
button: one-time STROKE_Distance measurement, automatic STROKE link and Draft
formula (max 15°), per-instance parameter control and Boolean Remove, plus
server-only package handling (no local fallback, package wiped when the session
ends). See **`LIFTER_INTEGRATION.md`**.

## Adding catalog entries

1. Add/update the published metadata in the Hostinger MySQL `templates` table.
2. Upload the public thumbnail.
3. Add an object with the same `Id` to the local `catalog.json` for CATPart/CATScript paths.
4. Restart and sign in again.

The complete SQL example and path layout are in `GUIDE_INSTALLATION_ET_UTILISATION.md`.
