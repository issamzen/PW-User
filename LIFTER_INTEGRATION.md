# Lifter Studio integration (catvba → WPF dashboard)

The complete **Lifter catvba macro** ("CATIA Lifter Parameters" / Lifter Studio) is now
integrated into the WPF catalog. It runs from the **Use in CATIA** button — no CATVBA
project, no HTA window, no temp-file command protocol anymore. The WPF app talks to
CATIA directly through COM, using the same INFITF + MECMOD references the project
already has (the whole engine is late-bound, so **no new COM references are needed**).

## Files

| File | Role |
|---|---|
| `LifterEngine.cs` | 1:1 C# port of every CATIA operation of the macro (measurement, formulas, parameters, Boolean Remove). No UI. |
| `LifterSetupWindow.cs` | First-run panel (pure C#, **no XAML**): auto-detect main body → measure STROKE_Distance (Retry / Choose another body / Continue / Cancel). |
| `LifterStudioWindow.cs` | The dashboard (pure C#, **no XAML**): instance rail, tiles (instances / stroke / draft), Undercut depth card, Lifter head card, Boolean Remove card. Also contains the shared `LifterUi` helper used by both windows. |
| `MainWindow.xaml.cs` | The `Use in CATIA` lifter branch, pre-flight, window lifetime, package wipe. |
| `Models.cs`, `PackageManager.cs`, `catalog.json`, `MainWindow.xaml` | New `Workflow` marker + "Lifters" category chip. |

Both lifter windows are built **entirely in C#** (controls, templates and styles are
constructed in code — a standard, fully supported WPF technique). There is **no
`.xaml` file and no `InitializeComponent`** for them, so they cannot suffer from
XAML build-action / generated-file problems: a plain `.cs` file added to the
project always compiles the same way.

## New "Use in CATIA" flow for a lifter template

```
Use in CATIA  (template flagged Workflow = "lifter")
 ├─ 1. License lease from the server (unchanged)
 ├─ 2. Encrypted package download from Hostinger (unchanged: SHA-256 check,
 │     PCPK decryption, extraction to the package cache)
 ├─ 3. NO local development fallback — the PowerCopy always comes from the
 │     licensed server package
 ├─ 4. Lifter pre-flight on the destination CATPart:
 │      • STROKE_Distance missing → LifterSetupWindow (measure bbox W of the
 │        main body, Retry / pick another body / Continue / Cancel)
 │      • LinkStrokeToMainBody  — every instance's STROKE_Distance is driven
 │        by the root value (single source of truth)
 │      • CreateDraftParameters — Draft = min(floor(atan((UNDERCUT_LENGTH+5mm)
 │        / STROKE_Distance)*180/PI + 0.5), 15) * 1deg  (replaces old formulas)
 ├─ 5. Open the downloaded template CATPart → select the PowerCopy →
 │     StartCommand("Instantiate From Selection") (same as before)
 └─ 6. Lifter Studio window opens next to CATIA:
        • Complete CATIA's Insert Object dialog → click "Refresh instances"
          (refresh also re-links STROKE and refreshes Draft formulas, so a
          newly inserted instance is fixed up automatically)
        • Edit the 5 parameters → "Apply and Update"
        • Boolean Remove: pick Copy body / Target body in CATIA, then
          "Remove this instance" (REMOVE_TOOL_Ixxx + Remove_Ixxx)
        • "New instance" re-runs the insertion (re-downloads the package if
          the previous session wiped it, with a fresh lease)
 └─ 7. "Run check" (unchanged) → results → source closed → lease released →
       the encrypted lifter package is DELETED from this PC.
```

## How a template is flagged as a lifter

`CatalogItem.Workflow` is filled from (first match wins):

1. **The server catalog API** — add a `workflow` column to the `templates` table
   (e.g. `ALTER TABLE templates ADD COLUMN workflow VARCHAR(32) NULL;` set to
   `'lifter'`) and include it in the catalog JSON items.
2. **The package manifest** (`manifest.json` inside the `.pcpkg`):
   ```json
   {
     "Id": "LF001",
     "Version": "1.0.0",
     "PowerCopyName": "PC_Lifter_v1.0",
     "CatPart": "LIFTER_SOURCE_v001.CATPart",
     "CheckScript": "LifterQuickCheck.CATScript",
     "CheckFunction": "RunCheckForTool",
     "Workflow": "lifter"
   }
   ```
   The manifest is inside the encrypted, SHA-256-verified package, so the flag
   travels with the paid content.
3. **Local `catalog.json`** (development / testing) — a `"Workflow": "lifter"`
   field on the template entry, like the included `LF001` example. Note: for
   lifter templates the local `CatPartPath` / `CheckScriptDirectory` are only a
   development convenience — the Use in CATIA flow **never falls back** to them
   when the server package is unavailable.

The `Lifters` category chip was added to the catalog pane, and the example
`LF001` entry uses `Category: "Lifters"`.

## VBA → C# mapping

| catvba | LifterEngine / window |
|---|---|
| `CATMain` | `UseInCatiaButton_OnClick` lifter branch + `RunLifterPreFlight` |
| `FirstRunSetup` / setup HTA | `LifterSetupWindow` |
| `MeasureStrokeOnBody` (bbox W, Y-direction extremums, planes, SPAWorkbench) | `LifterEngine.MeasureStrokeOnBody` |
| `Build_BoxExtremum` | `LifterEngine.BuildBoxExtremum` |
| `DetectMainBody` | `LifterEngine.DetectMainBody` |
| `SelectBodyFromUser` (`SelectElement2`) | `LifterEngine.SelectBodyFromUser` |
| `LinkStrokeToMainBody` | `LifterEngine.LinkStrokeToMainBody` |
| `CreateDraftParameters` + `GetFormulaFor` | `LifterEngine.CreateDraftParameters` + `GetFormulaFor` |
| `GetDraftDisplay` / `ComputeDraftValue` | `LifterEngine.GetDraftDisplay` / `ComputeDraftValue` |
| `SendValues` / `UpdateValues` / `SetDimension` / `SetBooleanValue` | `LifterEngine.ReadInstance` / `UpdateInstance` |
| UNDERCUT/LIFTER_HEAD set discovery (`GetUndercutSets`…) | same names in `LifterEngine` |
| `RunBooleanRemoveCore` (Copy → Paste Special As Result → `AddNewRemove`) | `LifterEngine.RunBooleanRemove` |
| `PasteBodyAsResult`, `FindNewBodyAfterPaste`, `GetUniqueBodyName`, `GetUniqueFeatureName` | same names in `LifterEngine` |
| Per-instance body storage (module collections keyed "001") | `LifterStudioWindow` dictionaries |
| HTA dashboard (REFRESH/UPDATE/SELECTBODY/REMOVEONE/POWERCOPY) | Buttons on `LifterStudioWindow` calling the engine directly |
| `MakeTopmost` / `ReleaseTopmost` / `mshta.exe` / temp-file polling | Not needed — the WPF window is a normal, non-topmost window |

Two intentional improvements over the VBA:

- **Body identity** is resolved by name instead of COM proxy `Is` comparisons
  (out-of-process COM hands out a fresh proxy per call — the VBA workaround was
  already heading that way). CATIA keeps body names unique inside a part.
- **`GetUniqueFeatureName`** uses the real `Body.Shapes` collection, so the
  `Remove_I00x` name de-duplication actually runs (the VBA probed a
  non-existent `Body.Shape` property and silently skipped it).

## Selling-safety of the PowerCopy

- The PowerCopy CATPart is **never shipped with the app** and never read from a
  fixed local folder: it only arrives through the leased, encrypted, hash-checked
  package download.
- Lifter templates **do not use the local catalog.json fallback** — if the server
  package is missing/unpublished, Use in CATIA fails with a clear error.
- When the template session ends (Run check finished, Close source, sign out,
  app exit), the lifter package cache is **deleted from the PC**
  (`WipeLifterPackageOnSessionEnd` in `MainWindow.xaml.cs`, set it to `false` to
  keep the cache between sessions). The next use re-acquires a lease and
  re-downloads from the server.
- A "New instance" click from Lifter Studio after a wipe transparently
  re-acquires a lease and re-downloads before launching the insertion.

## Build

Nothing else to reference — the csproj now includes `Microsoft.CSharp` (required
by the late-bound `dynamic` COM calls in `LifterEngine.cs`; the SDK does **not**
add it automatically for net48 targets) and `System.Web.Extensions`. The usual
rules still apply:

1. Open only `ProfessionalPowerCopyCatalogModern.csproj`.
2. INFITF + MECMOD COM references, `Embed Interop Types = False`, x86/x64 to
   match your CATIA.
3. Clean + rebuild.

### Migrating from the XAML version (important)

The lifter windows were converted to pure C#. If your project still contains the
old XAML versions, remove **all four** files:

- `LifterSetupWindow.xaml`
- `LifterSetupWindow.xaml.cs`
- `LifterStudioWindow.xaml`
- `LifterStudioWindow.xaml.cs`

In Visual Studio: right-click each file → **Supprimer** → choose
**Supprimer** (not just *Exclure du projet*) in the dialog. **They must also be
gone from the project folder on disk** — with an SDK-style csproj, any `.xaml`
file left in the folder is picked up automatically as a `Page` and generates a
partial class that collides with the new pure-C# classes. If in doubt, enable
**Afficher tous les fichiers** in the Explorateur de solutions and check that no
`Lifter*.xaml` file remains; delete stale ones in Explorer, then remove them
from the project too.

Then add the two new files: **Projet → Ajouter → Élément existant…** →

- `LifterStudioWindow.cs`
- `LifterSetupWindow.cs`

Leave their **Action de génération = Compiler** (the default for `.cs` files) —
no Page action, no special configuration of any kind. Rebuild the solution.

### Troubleshooting build errors

**`Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo.Create` missing (many
times)** — the project does not reference `Microsoft.CSharp`. In Visual Studio:
Références → Ajouter une référence → Assemblys → Framework → cocher
**Microsoft.CSharp** (or use the updated csproj, which already declares it).

**Errors about `InitializeComponent`, named controls (`DetectDot`,
`UpperBox`, …) or missing 5-argument constructors on the Lifter windows** —
your project is still compiling an **old `.xaml` / `.xaml.cs` version** of the
lifter windows. Follow the migration steps above: delete the four old files
(from the project **and** the folder), add the two new `.cs` files, rebuild.
The new pure-C# files have no XAML and no `InitializeComponent`, so these
errors can only come from leftover old files.

**Duplicate class or ambiguous name after the migration** — a leftover
`Lifter*.xaml` file is auto-included from disk by the SDK-style project.
Delete it from the folder (see migration steps).

After fixing: close Visual Studio, delete the `.vs`, `bin` and `obj` folders
(stale markup-compile caches), reopen and **Rebuild**.


## Testing checklist

1. Add the `LF001` entry (or your real lifter template) to the server catalog
   with `workflow = 'lifter'`, or keep the local `catalog.json` entry for
   development and point its paths at your dev files.
2. Open CATIA with a destination CATPart (with at least one Body).
3. Select the lifter card → **Use in CATIA**:
   - first run shows the setup panel and measures STROKE_Distance;
   - the template opens, the Insert Object dialog starts,
   - Lifter Studio opens ("Complete CATIA's Insert Object dialog…").
4. Complete the insertion in CATIA → **Refresh instances** in Lifter Studio →
   the new instance appears, its STROKE_Distance is linked to the root value
   and the Draft formula is created (Draft tile ≤ 15°).
5. Edit parameters → **Apply and Update** → values change in CATIA's f(x)
   parameter tree; the Draft readout follows the rule.
6. Boolean Remove: **Select in CATIA** (copy body), **Select in CATIA** (target
   body), **Remove this instance** → `REMOVE_TOOL_Ixxx` + `Remove_Ixxx` appear,
   the tool body copy is consumed, the original stays untouched.
7. **Run check** → results appear → after release, check that
   `%LOCALAPPDATA%\Estichara\MoldAutomationCatalog\Packages\LF001` is gone.

## Notes / limitations

- All CATIA calls run on the WPF UI thread (like the existing Run check), so
  the window shows a wait cursor during measurement / remove operations.
- Native CATIA prompts (body picks) hide the window while CATIA waits for the
  selection, exactly like the macro released the top-most HTA.
- If a native CATIA dialog is open, COM calls are rejected; Lifter Studio
  reports "CATIA is busy…" instead of crashing, and you can retry.
- The check script of the lifter package follows the same
  `Position | Status | Distance | Body` line protocol as the existing templates.
