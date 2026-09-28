# CursorForge

A free, lightweight YoloMouse alternative for Windows: custom cursors with **zero added latency**.
It has a full matching set of state cursors and an anti-cheat-friendly overlay for games that draw their own cursor.

## Build / install

Needs the .NET 10 SDK, plus Visual Studio with the **Desktop development with C++** workload (for the NativeAOT agent).

```powershell
.\build.ps1             # builds into .\dist
.\build.ps1 -Install    # also installs to %LOCALAPPDATA%\Programs\CursorForge + Start-menu shortcut
.\build.ps1 -Uninstall  # stops the agent, removes autostart/shortcut/files (keeps settings)
```

Run `CursorForge.exe`. It starts the agent, registers autostart and opens the settings window.

## How it works

| Piece | What it is | Cost |
|---|---|---|
| `CursorForge.Agent.exe` | Native (NativeAOT) tray agent, starts with Windows | ~4 MB RAM, 0% CPU when idle |
| `CursorForge.exe` | WPF settings UI | Runs only while its window is open |

**System cursors (every app).** The agent renders your cursor and swaps it into Windows with `SetSystemCursor`.
The GPU hardware cursor keeps drawing it, exactly like the stock arrow, so **latency is identical to Windows' own cursor**.
Each state gets a matching glyph in your style: link hand, I-beam, animated busy spinner, working, unavailable,
precision, move, four resize arrows, alternate select, and help/pin/person.
The agent re-applies after anything that makes Windows reload its cursors (Settings changes, unlock, resume, display changes).

**Game overlay (opt-in, per app).** Some games set their own cursor image, which the system swap can't touch.
For apps on the *Games & Apps* list, the agent shows your cursor in a click-through layered window.
It is visible only while the game shows a cursor, and hidden when the cursor is hidden or locked (e.g. aiming in a shooter).

- *Auto*: follow the OS cursor's visibility (recommended; safe for shooters).
- *Always*: also show over games that hide the OS cursor and draw a software cursor. It still hides while the cursor is clipped or pinned to the centre.

The overlay trails the hardware cursor by about one frame, and a visible topmost window can take a borderless game
off its direct-flip path while shown. That's why it is per app and disappears whenever the game hides the cursor.

**Anti-cheat.** No DLL injection, no input hooks (`SetWindowsHookEx`), no reading or writing game memory, and no handles
opened to the game process. Process names come from a Toolhelp snapshot. Everything uses public, documented
user-mode APIs that screen recorders and overlays also use. No tool can *guarantee* how every anti-cheat behaves,
but there is nothing here for one to object to. For competitive shooters you normally don't need the overlay at all:
their menus use the standard cursor, which the system swap already covers.

## Hotkeys (configurable)

- `Ctrl+Alt+Shift+C`: turn the custom cursor on/off
- `Ctrl+Alt+Shift+O`: add/remove the focused game from the overlay list (like YoloMouse's in-game hotkey)

## Files

- Settings: `%APPDATA%\CursorForge\config.json`
- Imported image: `%APPDATA%\CursorForge\custom.cfimg`
- Generated animated cursors: `%APPDATA%\CursorForge\cache\*.ani`
- Autostart: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CursorForge`

Exiting the agent (tray → Exit, or *Stop agent* in Settings) restores your normal Windows cursors immediately.
