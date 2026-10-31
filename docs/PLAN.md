# Banana Nana: technical plan

Read together with `AGENTS.md` (requirements) and `docs/RESEARCH.md` (findings that change this plan).

**Stack:** C# / .NET 8 / WPF, one self-contained EXE, no admin rights. Only extra dependency: xUnit for tests.

## Architecture

```
Tray (WinForms NotifyIcon) ── Settings ── Library (JSON + backups)
        │
KeyboardInputService (Raw Input, RIDEV_INPUTSINK)
        │ key events
TriggerMatcher (rolling buffer)
        │ match
ExpansionCoordinator ── ForegroundWindowService / AppExclusionService
        │
ClipboardPasteService ← RichContentRenderer (Unicode + RTF + CF_HTML + image)
        │ SendInput Ctrl+V
ClipboardRestoreService (best effort)
```

## Stages

### 0. Setup (½ day)
Windows machine with the .NET 8 SDK, git repo, xUnit test project, CI build on `windows-latest`.

### 1. Paste prototype (3–5 days, highest risk)
- Small test app: a button per variant puts bold text, a list and an image on the clipboard.
- Variants: (a) HTML with base64 `data:` image, (b) HTML with `file:///` image, (c) RTF with `\pict` image, (d) all formats together (plain text + RTF + HTML).
- Paste each into Notepad, Word, Outlook and new Teams. Record results in `docs/COMPAT.md`.
- **Gate:** choose the format strategy (one format for all apps, or per app by foreground process).

### 2. Clipboard services (2–3 days)
- Snapshot the full clipboard, set ours, send Ctrl+V, restore after a delay (start at 300–500 ms).
- Retry `OpenClipboard` when another app has it locked.
- Per-app delay setting.

### 3. Keyboard input and matcher (3–4 days)
- Raw Input (`RegisterRawInputDevices` with `RIDEV_INPUTSINK`), not `WH_KEYBOARD_LL` (see research).
- Characters via `ToUnicodeEx` with flag `0x4`, using the foreground window's keyboard layout.
- Skip Alt combos but keep AltGr, which Polish needs.
- Bounded buffer (about 64 chars) that resets on focus change, mouse click and Enter. Handles Backspace and separators.
- Keep no key history. Ignore our own injected input.
- Unit tests for the matcher.

### 4. Coordinator and safety (2–3 days)
- On match: send Backspaces to delete the trigger, then paste.
- Skip password fields: UI Automation `IsPassword`, checked only on match, in the background, with a timeout.
- Skip our own app and the excluded-apps list.
- Elevated apps: don't try to cross UIPI; detect it and show a tray warning.

### 5. App shell (3–4 days)
- Main window: list, search, preview, New/Import/Export/Settings. Separate editor window: bold, italic, underline, images, paste from Word.
- Library file `BananaNana.library.json` with a format version, backups, read-only handling, case-insensitive duplicate triggers.
- Import dialog for 0/1/2 libraries, export with a location picker, tray with pause and exit.
- Per-user autostart via the `HKCU\...\Run` registry key.

### 6. Release (2–3 days)
- `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
- Test on a clean Windows VM with no SDK. Write `docs/LIMITATIONS.md`.

## Acceptance test matrix
Notepad, Word, Outlook, Teams × plain text, bold, list, image, fast typing, busy clipboard, Polish characters, focus switch, excluded app, no admin.

## Main risks
| Risk | Mitigation |
|---|---|
| Apps read clipboard formats differently | Stage 1 gate, per-app format choice, plain-text fallback |
| Antivirus false positives | Raw Input instead of a hook, no key logging, code signing later |
| Clipboard restore races | Configurable delay, best effort only |
