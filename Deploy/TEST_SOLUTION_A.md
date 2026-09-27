# Testing solution A — the PW floating toolbar

No CATIA setup, no macro library, no `Tools > Customize`. You only build the
app and sign in.

## 0. Prerequisites

* Windows 10/11 with CATIA V5 installed (the toolbar also runs without CATIA)
* Visual Studio 2019/2022 with **.NET desktop development**, target **.NET 4.8**
* Build **x86** (the project is already `PlatformTarget = x86`, to match the
  CATIA COM interop)

## 1. Get the code and build

```bat
git pull
```

Open `ProfessionalPowerCopyCatalogModern.sln` → `Build > Rebuild Solution`
(Debug, x86) → `F5`.

> If the build fails on the `INFITF` / `MECMOD` COM references, re-add them:
> `Project > Add COM Reference > CATIA V5 InfInterfaces / MecModInterfaces`.

## 2. First look (CATIA not started yet)

1. Sign in with a licensed account.
2. A dark rounded **PW bar** appears on the right of the screen with 4 icons.
   Nothing else to do — that is the whole "installation" for the customer.
3. Drag it by the small **PW** label → it stays where you put it.
4. Click the small **✕** at the bottom → it hides. Click the **PowerCopies**
   tab in the app → it comes back.

## 3. With CATIA running

1. Start CATIA V5, open a **CATPart** with at least one solid body.
2. The bar should **dock to the right edge of the CATIA window**.
3. Move / resize the CATIA window → the bar follows (0.4 s refresh).
4. Minimise CATIA → the bar disappears. Restore CATIA → it comes back.
5. Click any icon → the CATIA window must **not** lose focus / flicker.

## 4. The four commands

| Icon | Expected |
|---|---|
| **Licence** (key) | the app opens on the account panel and refreshes the seat info from the server |
| **Stroke** | runs the STROKE script inside CATIA → message box `STROKE_Distance = xx.xxx mm created` (or *already on the CATPart*), and the parameter is visible in the spec tree |
| **Library** | the app comes to the front on the catalog list |
| **Checker** | runs the integrated check of the selected template (select a card first, otherwise it tells you to) |

## 5. What to report back

| Check | Why |
|---|---|
| Does the bar dock correctly on your monitor setup (multi-screen, 125 %/150 % scaling)? | DPI / multi-monitor positioning |
| Does it follow CATIA when maximised on a second screen? | same |
| Do the icons render, or do you see empty squares? | glyphs come from *Segoe MDL2 Assets* (Win10+); on Win8 I switch to bitmap icons |
| Does clicking an icon steal focus from CATIA? | `WS_EX_NOACTIVATE` behaviour |
| Stroke result on a real lifter CATPart | the only command that writes geometry/parameters |

## 6. Known behaviour (by design, not bugs)

* The bar appears **after sign-in**, not on the login screen.
* If the user drags it, it stops following CATIA (manual position wins) until
  the app is restarted.
* It hides while CATIA is minimised, and parks on the right of the desktop when
  CATIA is not running at all (Licence and Library still work).
* Closing the main window closes the bar and exits the app.

## 7. Next step once A is validated

Switch on route **B** (real icons inside CATIA) for the releases your customers
use: `Deploy/Capture-PWToolbar.ps1` once on your machine, then
`Deploy/Install-PWToolbar.ps1` from the installer — see
`Deploy/README_ONE_CLICK.md`.
