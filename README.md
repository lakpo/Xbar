# Xbar

**A shortcut bar that lives inside your Windows 11 File Explorer windows.**

*[Lire ce document en français](README.fr.md)*

![The Xbar bar inside an Explorer window](docs/xbar-bar.png)

Xbar adds a slim vertical bar just inside the right edge of every Explorer window. Pin the
folders, files and applications you reach for all day, and get to them without leaving the
window you are already in.

It does not inject anything into `explorer.exe`, does not patch it, and never asks for
administrator rights.

---

## Download

1. Grab **`Xbar.exe`** from the [latest release](https://github.com/lakpo/Xbar/releases/latest).
2. Put it wherever you like — `C:\Users\<you>\Apps\Xbar\` is a good spot.
3. Double-click it.

That is the whole installation. No setup wizard, no runtime to install, nothing written outside
your own user profile. Xbar appears in the notification area and a bar shows up in every open
Explorer window.

> Windows may show a **SmartScreen** warning the first time, because the file is not
> code-signed. Click *More info* → *Run anyway*.

**Prefer a self-contained folder?** Download `Xbar-<version>-portable.zip` from the same release
and unzip it anywhere, a USB stick included. In that mode Xbar keeps everything in a `Data\`
folder next to the executable and writes nothing else to the machine.

| | Where settings live | Starts with Windows |
|---|---|---|
| `Xbar.exe` on its own | `%AppData%\Xbar\` | yes, from the first run |
| Portable zip | `Data\` next to the exe | no — opt in from the tray icon |

To uninstall: quit Xbar from the tray icon, delete the executable, and optionally remove
`%AppData%\Xbar\`.

**Requirements:** Windows 11 (build 22000 or later), 64-bit. No .NET installation needed.

---

## Getting started

**Add a shortcut** — drag a folder, a file, an application or a `.lnk` onto the bar. Or
right-click the bar → *Add*, or use the Settings window.

**Use a folder shortcut**

| Gesture | Result |
|---|---|
| Left click | Go to that folder in the current tab |
| Middle click | Open it in a new tab |
| Ctrl + click | Open it in a new window |

**Drop files onto a button.** Dropped on an *application*, they are passed to it as arguments —
exactly as if you had dropped them on its executable. Dropped on a *folder*, they are moved
there (copied across drives; Ctrl forces a copy, Shift a move). The target icon grows and a
label spells out what will happen before you let go.

**Double-click empty space** in the file list to go up to the parent folder. Clicking a file, a
folder, the navigation pane or the address bar is left alone, and nothing happens at the root of
a drive.

**Rearrange** by dragging buttons within the bar. Right-click any button to rename it, change its
icon, add separators, or remove it.

**Move the bar** by dragging the small grip at its top. The position is remembered as a fraction
of the window height, so it stays where you put it whatever the size of the window.

---

## Features

**The bar**

- one bar per Explorer window, all sharing a single configuration;
- follows its window when you move, resize, maximise or minimise it;
- stays above its own Explorer window without ever covering your other applications;
- steps aside only when a scroll bar is actually shown, so a margin of 0 really does hug the edge;
- hides itself when the window becomes too small to hold it;
- light/dark theme following Windows, adjustable opacity and button size, optional auto-hide.

**Four display modes**

| Mode | Behaviour |
|---|---|
| Icons only | Compact, tooltip on hover |
| Icon + text | Labels always visible |
| Icons, text on bar hover | The whole bar widens when the mouse enters it |
| **Icons, name on button hover** *(default)* | Only the hovered button slides its name out |

**Shortcuts**

- folders, files, applications and separators;
- a file opens with its associated application, like a double-click in Explorer;
- `.lnk` shortcuts are resolved to their target;
- per-shortcut editor: name, path, arguments, working directory, drop handling;
- the `{currentFolder}` token in arguments — for example `cursor.exe "{currentFolder}"` — is
  replaced by the folder currently on screen.

**Two bars.** Each holds its own list of shortcuts. With the mouse over the bar, press **Tab** to
switch to the other one; a short message shows which bar is on screen. The bar's right-click menu
and the *Shortcuts* tab of the settings do the same. Tab is only claimed while the pointer is over
the bar.

**Icon library.** Right-click a button → *Icon library…* to pick one of the 7,400+
[Material Design Icons](https://pictogrammers.com/library/mdi/) (Apache 2.0) built into Xbar,
searchable by name, in any colour — or *Automatic*, which follows the bar's light or dark theme.
The colour also applies to the glyph of built-in actions.

**Built-in actions**

- **Paste clipboard image** into the folder on screen, named `Clipboard_2026-09-01_13-42-18.png`,
  as PNG, JPG or WebP, with an optional name prompt and a discreet confirmation.
- **Recent folders** — a menu of the last 20 folders you visited, across every Explorer window.
  Click to go there, Ctrl+click to open a new window.

**Elsewhere**

- notification area icon, settings window, right-click menu on the bar itself;
- English and French interface, following the Windows language by default;
- single instance — launching Xbar again opens the settings of the running one;
- updates through GitHub Releases, checked 20 s after launch then every 6 hours. By default Xbar
  **asks first**: *Install now* downloads the new `Xbar.exe`, checks it against the SHA-256
  published by GitHub, swaps it in and restarts; *Later* asks again at the next launch. Other
  choices: install silently, without restarting (the new version is used the next time Xbar
  starts), or only notify. The check itself can be switched off;
- no telemetry, no analytics, nothing sent anywhere; logs stay on your machine.

![The Xbar settings window](docs/xbar-settings.png)

---

## Configuration

| Mode | Location |
|---|---|
| Installed | `%AppData%\Xbar\` |
| Portable | `Data\` next to `Xbar.exe` (marker: a file named `portable`) |

Holding `settings.json`, `shortcuts.json` (plus `shortcuts-2.json` for the
second bar), `history.json`, `icons\` and `logs\`.

On the very first run the bar contains your Pictures folder — under the name Windows gives it —
a separator, then the two built-in actions.

Startup with Windows uses the per-user `Run` key, so it never triggers a UAC prompt.

---

## Building from source

```bash
dotnet build src/Xbar/Xbar.csproj
```

Requires the .NET 10 SDK. To produce the deliverables:

```powershell
pwsh build\publish.ps1
```

This writes the portable version — folder and zip — to `dist\`, and, if
[Inno Setup 6](https://jrsoftware.org/isdl.php) is installed, an `Xbar-<version>-setup.exe` that
installs into the user profile without UAC. Both builds are self-contained single files: no .NET
runtime is required on the target machine.

The script refuses to run while an Xbar started from `dist\` is still open, and it preserves an
existing portable `Data\` folder across rebuilds.

---

## How it works

```
src/Xbar/
  Core/                 paths, logging, settings, theme, single instance, Windows startup
  Interop/              Win32 P/Invoke, WinEvent hooks, Shell COM interfaces
  ExplorerIntegration/  window tracking, navigation, folder history, empty-area double-click
  Overlay/              the bar window, placement, drag & drop, menus, floating labels
  Shortcuts/            model, storage, launching, .lnk resolution
  Actions/              built-in actions
  Icons/                shell icons and custom icons
  Tray/                 notification area icon
  UI/                   settings, shortcut editor, input dialog
  Localization/         interface strings (Resources/Strings.resx + .fr.resx)
  Update/               GitHub Releases check
```

A few decisions worth knowing about, mostly because the obvious approach turned out to be wrong.

**Window tracking** uses out-of-process `SetWinEventHook` — creation, destruction, movement,
minimisation, foreground changes — plus a light reconciliation every 5 seconds as a safety net,
which also covers `explorer.exe` restarting. No permanent polling.

**The overlay** is a WPF window with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, declared *owned* by
the Explorer window through `GWLP_HWNDPARENT`. Windows then keeps it above its owner for free,
without it ever becoming topmost. Nothing may touch its Z-order afterwards: any `SetWindowPos`
with `HWND_TOP` would raise it above every other application.

**The active tab.** On Windows 11 all tabs of a window share one HWND and each owns an
`IShellBrowser`, but nothing marks the visible one — every view reports itself visible, with
identical geometry. The reliable discriminator turns out to be Z-order: the active tab's
`ShellTabWindowClass` container comes first among its siblings. Reading the folder and navigating
then go through `IShellBrowser::BrowseObject`.

**New tabs** are the one place a keystroke is simulated: Windows exposes no public API for
Explorer tabs, so Xbar sends `Ctrl+T` and then navigates the new tab through COM, falling back to
a new window if the tab never appears.

**The scroll bar** is drawn by DirectUI in the modern view: neither `WS_VSCROLL` nor
`GetScrollBarInfo` can see it — a folder holding thousands of files still reports "invisible".
Xbar queries the `ScrollPattern` through UI Automation instead, off the UI thread, cached and
re-checked on navigation or resize.

**Icon sizes.** Windows does not scale an icon up when the requested size is missing — it
*centres* it in a larger canvas, so a custom folder icon fills only 48 px of the 256 px JUMBO
list and looks tiny. Xbar picks the image list matching the render size and measures how much of
the canvas is actually painted, dropping to a smaller list when the artwork is lost in it.

**Empty-area double-click** uses a low-level mouse hook (out of process; the click is never
swallowed), then the window class chain — the file view always sits inside `SHELLDLL_DefView`,
which rules out the navigation pane, address bar, search box and the desktop — and finally UI
Automation to tell empty space from a list item. That probe runs on the *first* click of the
pair: by the second one Explorer has already opened the folder, and the answer would be wrong.

---

## Not in this version

Plugins, custom scripting, advanced multi-monitor handling, per-monitor DPI, replacing
the native tabs — and a bar inside Open/Save dialogs: those use the common dialog, which
`ShellWindows` cannot see, so none of the COM integration above applies to them.

---

## Licence

None yet — see the repository owner.

The embedded Material Design Icons are © Pictogrammers, under the Apache 2.0 licence
(`src/Xbar/Icons/MDI-LICENSE.txt`). Regenerate them with `build/icons/generate-icons.mjs`.
