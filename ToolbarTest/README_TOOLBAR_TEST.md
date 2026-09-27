# PW-User — CATIA toolbar test kit

Goal of this folder: **prove that the 4-icon toolbar idea works on a real
CATIA seat** before any of it is built for production. Four tiny macros, four
icons, no server, no licence, no package — 15 minutes of work.

| Icon | Macro | What it proves |
|---|---|---|
| **L** | `PW_01_License.CATScript` | a toolbar button can run our code, read the CATIA release, and unlock the other tools (writes a session token valid 8 h) |
| **S** | `PW_02_Stroke.CATScript` | real geometry work from a button: detect the main body, measure the bounding-box width, create/update `STROKE_Distance` |
| **P** | `PW_03_Library.CATScript` | a branded HTA panel opens from CATIA, and the licensed `.exe` is launched when it is installed |
| **C** | `PW_04_Checker.CATScript` | interference check between two bodies (minimum distance, SPAWorkbench — **no DMU licence needed**) |

Buttons 2, 3 and 4 refuse to run until button 1 has been clicked: that is the
"licence unlocks the libraries" behaviour, tested locally with a token file in
`%LOCALAPPDATA%\Estichara\PWUserTest\session.tok`.

---

## 1. Install (1 minute)

Double-click **`Install_PW_Toolbar_Test.bat`**.
It copies the macros and icons to:

```
%LOCALAPPDATA%\Estichara\PWUserTest\Macros
%LOCALAPPDATA%\Estichara\PWUserTest\Icons
```

Nothing else is touched — no CATIA setting is modified by the script.

## 2. Declare the macro library in CATIA (1 minute, once)

1. `Tools` ▸ `Macro` ▸ `Macros…` ▸ **`Macro libraries…`**
2. *Library type* = **Directory** ▸ **Add existing library**
3. Pick `%LOCALAPPDATA%\Estichara\PWUserTest\Macros` ▸ **Close**

The four macros now appear in the `Macros…` list.

## 3. Put the 4 icons on a toolbar (2 minutes, once)

1. `Tools` ▸ **`Customize…`** ▸ **Commands** tab ▸ *Categories*: **Macros**
2. Drag `PW_01_License`, `PW_02_Stroke`, `PW_03_Library`, `PW_04_Checker`
   onto any toolbar (or onto the empty grey area to create a new one).
3. Select each command ▸ **`Show properties…`** ▸ *Icon:* **`…`** ▸ choose the
   matching `.bmp` in `%LOCALAPPDATA%\Estichara\PWUserTest\Icons`.
   You can also rename the command here (e.g. “PW Licence”).
4. **Close**. CATIA writes this into your `CATSettings`; the toolbar survives a
   restart.

## 4. Test sequence

1. Open any **CATPart** with at least one solid body.
2. Click **L** → message box with the CATIA release + token path.
   *(Click 2/3/4 before 1 to see the “No valid session” lock.)*
3. Click **S** → `STROKE_Distance` is created in the part parameters.
4. Click **P** → the HTA library panel opens (or your `.exe` starts, if it is
   installed in `%LOCALAPPDATA%\Estichara\PWUser\`).
5. Add a second body, click **C** → pick body 1, pick body 2 → clash or
   clearance verdict.
6. **Restart CATIA** and click the icons again — this is the real test: it
   tells us whether the toolbar persists on that seat/environment.

## 5. What to report back

| Question | Why it matters |
|---|---|
| CATIA release + SP (shown by button 1) | which releases we must support |
| Did the toolbar survive the CATIA restart? | decides if the `CATSettings` route is enough |
| Is `Tools > Customize` available, or locked by your IT/admin env? | locked environments force the CAA add-in route |
| Did the icons accept the `.bmp` files? | some releases prefer 24×24 BMP, others accept PNG |
| Any error box, and its exact text | fixes before the production version |

## 6. Known limits of this test kit (by design)

* No server call, no real licence check — the token is created locally.
* The macros are plain text: in production the licence/decryption logic moves
  into the signed `.exe`, the macros stay thin launchers.
* Toolbar creation is manual here. Automating it on first run (copying a
  prepared `CATSettings`) is the next experiment — worth doing **only if this
  test passes**.
