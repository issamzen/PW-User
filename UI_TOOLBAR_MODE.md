# Toolbar mode — the product is the 4 icons

The big dashboard is gone from the user's world. What the customer sees after
installing and signing in is a **slim always-on-top PW bar** next to CATIA,
and one purpose-built panel per tool.

```
   ┌────┐
   │ PW │   ← drag handle
   ├────┤
   │ 🔑 │   Licence  → small activation panel (e-mail + key)
   │ 📏 │   Stroke   → runs immediately, no window (toast with the result)
   │ 📚 │   Library  → premium PowerCopy window
   │ ✔  │   Checker  → compact clash / feasibility panel
   ├────┤
   │ ⏻  │   Quit PW-User (the only way to close the tools)
   └────┘
```

## What changed

| Before | Now |
|---|---|
| Full dashboard window at startup | `MainWindow` is **hidden** and only acts as the engine (licence, catalog, packages, CATIA COM). It is never shown, and stays out of the taskbar. |
| Toolbar disappeared when CATIA was minimised | The bar is **always visible and always on top**, docks to the CATIA window when it is there, parks on the right of the desktop otherwise. |
| Panels closed by the actions | Panels **never close themselves**: clicking ✕ on a panel only hides it, and every tool keeps its state. The product exits only via the ⏻ button (with confirmation). |
| One window for everything | One window per job: `LicenseWindow`, `LibraryWindow`, `CheckerWindow`, plus a `ToastWindow` for silent commands. |

## The four tools

**1 · Licence** — `LicenseWindow.xaml`
400 px card: e-mail, licence key, "keep me activated", one **Activate** button.
Once activated it flips to a green "Licence active" card with the account, the
remaining time and the number of authorized templates, plus *Refresh* and
*Sign out*. This is the only place the customer types anything.

**2 · Stroke** — no window at all
Runs the STROKE script inside CATIA on the active CATPart and shows a
self-closing toast: `STROKE_Distance = 42.500 mm created`. Errors show a toast
too, in red.

**3 · Library** — `LibraryWindow.xaml`
The premium window: gradient rail with live search and auto-built category
chips, card list of the licensed templates, and a hero detail panel with
**Use in CATIA** / **Run feasibility check**, the lifter-workflow badge, the
package state and a live status strip fed by the engine.

**4 · Checker** — `CheckerWindow.xaml`
Compact panel: template + destination CATPart, a colour-coded verdict badge
(PASS / CLASH / RUNNING), the per-position result rows and a **Run check**
button.

## Behaviour rules implemented

* Toolbar: `Topmost`, `WS_EX_NOACTIVATE` (clicking it never steals CATIA's
  focus), `WS_EX_TOOLWINDOW` (not in the taskbar / Alt-Tab), draggable by its
  "PW" grip, and it remembers a position you set by hand.
* All panels are `Topmost` too, so they can never get lost behind CATIA.
* Clicking Library / Stroke / Checker without a licence pops the licence panel
  with a red toast instead of failing.
* Quitting closes every panel cleanly and releases the licence seat.

## Test it

1. Build (Debug, x86) and run. **No window opens** — only the PW bar appears.
2. Click 🔑 → activate with your account → the card turns green.
3. Click 📚 → the premium library; select a template → **Use in CATIA**.
4. Click 📏 with a CATPart open → toast with the measured `STROKE_Distance`.
5. Click ✔ → the checker panel; **Run check** after an instantiation.
6. Minimise CATIA, move CATIA between screens → the bar stays visible and
   follows. Close a panel with ✕ → the bar stays.
7. Click ⏻ → confirmation → everything closes and the seat is released.
