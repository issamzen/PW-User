# One-click deployment — what is possible, what is not

Your requirement: **the customer buys, installs, and the tools are there. No
`Tools > Customize`, no macro-library declaration, nothing.**

Here is the honest map of the options, and what this folder implements.

---

## The hard technical fact

**The CATIA V5 automation API cannot create a command, an icon or a toolbar.**
There is no `CATIA.Toolbars.Add(...)`. Toolbars only come from:

1. a **CAA V5 (RADE) C++ add-in** — the only *supported* way to add real
   commands/workbenches, or
2. the **user's `CATSettings`** files, which is what `Tools > Customize`
   writes when a user drags a macro onto a toolbar.

So "one click" can only mean: **we write those settings files for the user**,
or **we don't use a CATIA toolbar at all**.

---

## The three routes

| | Route | Customer steps | Cost / risk | Status here |
|---|---|---|---|---|
| **A** | **App-side floating toolbar** — a slim always-on-top PW bar docked to the CATIA window | **zero** | none: works on every V5 release, cannot be blocked by IT policy | ✅ **implemented** (`PwToolbarWindow.cs`, appears automatically after sign-in) |
| **B** | **Pre-captured `CATSettings`** deployed by the installer | **zero** (must close CATIA once) | must be captured once *per CATIA release*; replaces the user's own toolbar customization (backed up); undocumented binary format | ✅ **implemented** (`Capture-PWToolbar.ps1` + `Install-PWToolbar.ps1`) |
| **C** | **CAA V5 RADE add-in** | zero, icons are native | CAA licence (very expensive) + a build per CATIA release + 3–6 months | ⛔ not done — only worth it at volume |

**Recommended product setup: A + B together.**
B gives the native in-CATIA icons where the release is supported; A is the
fallback that *always* works, so the customer never sees a failure — and never
sees a manual step either.

---

## B — how it works in practice

**You, once per CATIA release (30 minutes):**

1. On a reference machine with that release, do the manual setup once
   (declare the macro library, drag the 4 commands, set the icons).
2. Close CATIA.
3. `powershell -ExecutionPolicy Bypass -File Capture-PWToolbar.ps1`
   → writes `Deploy\Settings\B27\*.CATSettings` (detected release).
4. Commit that folder; ship it inside your installer.

**The customer, zero times:**

Your installer (or the app at first run) calls:

```powershell
powershell -ExecutionPolicy Bypass -File Install-PWToolbar.ps1
```

which detects the release, copies the macro library to
`%LOCALAPPDATA%\Estichara\PWUser\Macros`, backs up the user's current CATIA
settings and drops in the prepared ones. Exit codes are made for an installer:

| Code | Meaning | What your installer should do |
|---|---|---|
| `0` | toolbar installed | tell the user "icons appear at the next CATIA start" |
| `2` | CATIA is running | ask to close CATIA, or retry after |
| `3` | no CATIA / no settings folder | silent, nothing to do |
| `4` | release not captured yet | silent fallback to the app toolbar (A) |

Rollback for support cases: `Install-PWToolbar.ps1 -Restore`.

### Limits you must accept with B
* CATIA must be **closed** while the settings are written (it rewrites them on
  exit). An installer step is the natural place.
* One capture **per release** (B21, B25, B27…). Untested releases fall back to A.
* Settings are **per Windows user**; a shared PC needs the install per profile.
* Corporate seats locked with `CATReferenceSettingPath` / admin-locked settings
  will refuse the change → A covers them.

---

## A — the floating toolbar (already in the app)

`PwToolbarWindow.cs`: a 58 px always-on-top bar with the four commands
(Licence, Stroke, Library, Checker). It finds the `CNEXT.exe` window, docks
itself to its right edge, follows it when CATIA moves/resizes, hides when CATIA
is minimised, never steals focus (`WS_EX_NOACTIVATE`), and can be dragged
anywhere by its "PW" grip. It shows up on its own right after sign-in — that
*is* the single click: install, sign in, the tools are there.

---

## Recommendation

Ship **A** now (it is done, and it makes the product demo-able with zero setup
on any seat), and add **B** for the two or three CATIA releases your first
customers actually run. Keep **C** (CAA) for later, only if a big customer
demands native workbench integration.
