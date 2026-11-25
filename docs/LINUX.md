# Banana Nana on Linux (step 2: library import, engine and CLI)

Status: experimental. The desktop app (`banananana`, see below) runs the engine; `bn-linux-cli` stays for debugging.

## Library

The Linux library is `$XDG_DATA_HOME/BananaNana/library.json` (default `~/.local/share/BananaNana/library.json`):

```json
{ "Name": "My library", "Snippets": [ { "Trigger": ";sig", "Html": "<p>Kind regards</p>", "Text": "Kind regards" } ] }
```

Triggers must be non-empty and unique (case-insensitive). Saves go to a `.tmp` file that is then moved into place.

Import a Windows library (read only, the Windows file is never changed):

```sh
bn-linux-cli import /path/to/BananaNana.library.json
bn-linux-cli list
```

- Empty Linux library: every snippet is imported.
- Otherwise: snippets whose trigger already exists are skipped and listed.
- A snippet that cannot be converted is listed as failed; the others are still imported. The exit code is 1 if anything failed.

The Windows snippet `Content` is a base64 WPF XamlPackage. It is converted without WPF: bold, italic, underline,
strikethrough, colours (`#AARRGGBB`, `#RRGGBB`, named), font size and weight, alignment, line breaks, ordered and
unordered lists (nested), tables, links (http, https, mailto only), and images (PNG, JPEG, GIF embedded as data URIs).
Unknown elements are walked through. Images that are missing from the package, or in another format, are left out.

Layout guesses that have not been checked against a real Windows library yet: the XAML part is found through
`_rels/.rels`, then `[Content_Types].xml`, then the first `.xaml` part; images are looked up relative to the XAML part,
then at the package root, then by file name.

`bn-linux-cli` with no arguments runs the snippets in the Linux library and reloads them about 0.3 s after the file
changes. `bn-linux-cli snippets.json` still runs a test file in the `{ trigger, html, text }` format.

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

## Desktop app (step 4a)

`banananana` is the desktop window: snippet list, editor, settings, help and a tray icon. The expansion engine runs
inside it, so `bn-linux-cli` is only needed for debugging.

Build and publish (self-contained, one file):

```sh
dotnet publish BananaNana.Desktop -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ~/bn/out/linux-step4a
```

The solution (`BananaNana.sln`) also contains the Windows WPF projects; build it with `-p:EnableWindowsTargeting=true`.

Install for one user:

```sh
install -Dm755 banananana ~/.local/bin/banananana
install -Dm644 BananaNana.Desktop/packaging/banananana.png ~/.local/share/icons/hicolor/256x256/apps/banananana.png
install -Dm644 BananaNana.Desktop/packaging/banananana.desktop ~/.local/share/applications/banananana.desktop
```

- `banananana` opens the window. `banananana --tray` starts hidden in the tray.
- A second start brings the running window forward (socket `$XDG_RUNTIME_DIR/banananana.sock`).
- Closing the window hides it to the tray. Exit in the tray menu quits.
- Settings: language, "Start Banana Nana when I log in" (writes `~/.config/autostart/banananana.desktop`, which
  runs this binary with `--tray`), and "Pause expansion when Banana Nana starts".
- Settings file: `$XDG_CONFIG_HOME/BananaNana/settings.json` (default `~/.config/BananaNana/settings.json`), the same
  `AppSettings` class as Windows.
- Library: the same Linux library file as above. The app reloads it when the file changes.

Keyboard access: if the app cannot read the keyboards or `/dev/uinput`, the window shows a banner with the setup steps.
A new group membership only applies to a new login session, so log out and back in after `usermod -aG input`.

Editing: the editor works on plain text. A snippet with formatting or pictures shows a notice. Saving a changed
text saves it as plain text (the button says so). Renaming the trigger keeps the formatting.

Texts come from the Windows app's `BananaNana/Strings/Strings.*.xaml`, read by a small XML loader (`Loc`). New
Linux keys are in all seven files, marked `<!-- TODO translate -->` in the non-English ones.

Tests: `dotnet test tests/BananaNana.Desktop.Tests` (string files, trigger rules, autostart file, snippet helpers).

Limitations of the first desktop build:

- The window runs through XWayland on a KDE Wayland session (the Avalonia Wayland backend is not in this build).
- Avalonia's tray icon has no notification, so there is no "still running in the tray" balloon on the first close.
- The help text is the Windows one. It mentions the toolbar, pasting from Word and Ctrl+K, which this build does not
  have yet.
- The status line says "Listening on 1 keyboards" for a single keyboard (the string is shared with the Windows wording).

## Guide maker (stage 1)

The Guide maker is a header button and a tray menu item. It opens one native window (no browser) with the list of
guides and, for one guide, its title, intro and steps. Each step has a title, a text and a picture.

- Folder: `xdg-user-dir DOCUMENTS` + `BananaNana Guides` (`~/Documents/BananaNana Guides` when that tool is missing).
  One file per guide: `<guid>.bnguide` (JSON, `version` 1). A save writes `<guid>.bnguide.tmp` and moves it over the old file.
- Changes autosave 800 ms after the last one. Ctrl+S saves now, and closing the window saves first. When a save fails,
  the status says "Couldn't save" and the window stays open.
- Pictures: with a step focused, Ctrl+V puts the clipboard picture into that step. With no step focused, Ctrl+V adds a
  new step (take a screenshot, Ctrl+V, repeat). A picture can also be dropped on a step or on the window, or chosen
  with the file picker.
- Clipboard: Avalonia's clipboard is tried first (`image/png`, then `image/jpeg`). When it has no picture on Linux,
  `wl-paste --type image/png` (or `image/jpeg`) is used, so `wl-clipboard` must be installed.
- Pictures wider than 1600 px are scaled down. They are stored as PNG, or as JPEG (quality 90) when that PNG is over 1.5 MB.
- Steps are reordered with Move up and Move down (no drag handle yet). Delete step asks first.
- Annotations are stored as an empty list; the export comes in a later stage.
