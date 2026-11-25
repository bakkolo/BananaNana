# Banana Nana on Linux (step 3: engine and CLI)

Status: experimental. The engine is a test harness (`bn-linux-cli`), not the full app yet.

## How it works

1. **Keys in.** `BananaNana.Linux/Evdev/InputMonitor` opens every keyboard and mouse under
   `/dev/input/event*` with one reader thread each (`KeyboardReader`). Devices are read, not grabbed,
   so other apps still get every key. New devices are picked up by a `FileSystemWatcher`; a device
   that errors (unplugged) is dropped.
2. **Key map.** `XkbKeymap` (libxkbcommon, the engine the compositor uses) turns evdev codes into text
   for any layout. Every key from every keyboard goes into one xkb state, so Shift, CapsLock, AltGr and
   dead keys come from xkb. Backspace, Space, Tab, Enter, Esc, arrows, Home/End, PgUp/PgDn, Insert,
   Delete, mouse buttons, and Ctrl, Alt or Meta shortcuts follow the same rules as Windows. Modifier-only
   keys never reset the word, and dead keys type nothing. If libxkbcommon cannot be loaded, the built-in
   US table (`KeyMap`) is used and a warning is printed.
3. **Matching.** The shared `TriggerMatcher` (BananaNana.Core, unchanged) keeps the current word. A
   space after a whole word equal to a trigger fires the expansion.
4. **Expansion** (`LinuxExpansionEngine`), about 840 ms in total:
   - send (trigger length + 1) Backspaces through a virtual keyboard (`/dev/uinput`),
   - save the clipboard (`wl-paste --list-types`, then text/html if offered, else text/plain),
   - copy the snippet (`wl-copy --type ...`), HTML only when it has formatting or images,
   - wait 150 ms, send Ctrl+V, wait 500 ms,
   - restore the clipboard (`wl-copy --type <saved type>`).
   Key events typed during an expansion are not matched.

## Keyboard layout

The layout is the first match of:

1. `~/.config/kxkbrc` `[Layout]`, when `Use=true` (LayoutList, VariantList, Model, Options). This is
   what KDE writes.
2. `/etc/X11/xorg.conf.d/00-keyboard.conf` (`XkbLayout`, `XkbVariant`, `XkbModel`, `XkbOptions`).
3. `XKB_DEFAULT_LAYOUT`, `XKB_DEFAULT_VARIANT`, `XKB_DEFAULT_MODEL`, `XKB_DEFAULT_OPTIONS`.
4. `us`.

With several layouts, the active one is read from KDE with `qdbus6 org.kde.keyboard /Layouts
org.kde.KeyboardLayouts.getLayout`, and then followed with `dbus-monitor` on the `layoutChanged` signal.
A change resets the word being typed. Without qdbus6 or dbus-monitor the group stays at 0. Editing
`kxkbrc` reloads the layout. The log prints the layout in use at start and on every change.

`--layout <xkb layout>[:variant]` overrides detection for testing, for example `--layout pl` or
`--layout de`. It also turns off KDE following.

## Triggers

A trigger is any text without spaces, for example `;offer`. Start it with `;` or `/` so it never
matches a normal word. Matching is case-insensitive. The expansion fires when you type a space after
the trigger. The space and the trigger are deleted, and the snippet is pasted.

## Setup

Needs `wl-clipboard` (`wl-copy`, `wl-paste`) and a Wayland session.

Give your user access to the input devices and to `/dev/uinput`:

```sh
sudo usermod -aG input "$USER"        # then log out and back in
```

Create `/etc/udev/rules.d/99-banana-nana-uinput.rules` with:

```
KERNEL=="uinput", GROUP="input", MODE="0660", OPTIONS+="static_node=uinput"
```

Then reload: `sudo udevadm control --reload && sudo udevadm trigger`.

## Running the CLI

```sh
bn-linux-cli snippets.json
```

`snippets.json` is a list of snippets:

```json
[
  { "trigger": ";demo", "html": "<b>Bold</b> text", "text": "Bold text" },
  { "trigger": ";sig", "html": "Kind regards,\nMatt", "text": "Kind regards,\nMatt" }
]
```

`html` and `text` are both optional; a missing one takes the other's value. A sample is in
`BananaNana.Linux.Cli/samples/snippets.json`.

The CLI prints the keyboards and mice it finds, hotplug changes, each trigger match, and the time
of each step. Press Ctrl+C to quit; the virtual keyboard is destroyed on exit.

## Limitations

- Focus changes are not detected (Windows resets on a foreground change), so a click or arrow key is
  the only reset.
- Dead keys are not composed: a dead key types nothing, and the next key is typed without the accent.
  Characters outside the Basic Multilingual Plane (surrogate pairs) are ignored.
- The built-in US fallback still resets on Right Alt, as before. The xkb path does not.
- Held modifiers are not released before Ctrl+V (Windows does this). Holding Shift during the paste
  can turn it into Ctrl+Shift+V.
- Ctrl+V uses the key that types 'v' in the active layout, found at the time of the expansion.
- wl-copy offers one mime type per copy, so HTML and plain text cannot be offered together yet.
- Only text is saved and restored. If the clipboard holds an image, the snippet replaces it.
