# Banana Nana: architecture

C# / WPF / .NET 10, one Windows desktop app with no services and no admin rights.

## How an expansion works

```
keyboard / mouse (Raw Input, observe only)
        │
KeyboardInputService ── chars, Backspace, "reset" (click, focus change, Enter, arrows, shortcuts)
        │
TriggerMatcher ── current word only; on space: does the word equal a trigger?
        │ match
ExpansionCoordinator ── skips Banana Nana's own windows, pause
        │
RichContentRenderer ── stored snippet → plain text + RTF + HTML (images as base64 PNG)
        │
ClipboardPasteService ── snapshot clipboard → set ours → InputInjector: Backspaces + Ctrl+V
        │                  → wait → restore the old clipboard (only if nobody changed it)
target app
```

1. **Detection** (`Input/KeyboardInputService.cs`): Raw Input with `RIDEV_INPUTSINK` on a message-only
   window. Unlike a `WH_KEYBOARD_LL` hook it cannot be silently removed by Windows and never delays
   other apps' input. Keys become characters with `ToUnicodeEx` using the foreground window's keyboard
   layout (flag `0x4` so dead keys in the target app keep working). Ctrl+Alt counts as AltGr (needed for
   Polish letters); Ctrl or Alt alone, and Win, count as shortcuts and reset the word.
   Our own injected keys carry a marker in `dwExtraInfo` and are ignored.
2. **Matching** (`Input/TriggerMatcher.cs`): holds only the word being typed (max 64 characters, in
   memory, never logged). A match needs a separator (space) right after a whole word equal to a trigger,
   case-insensitive. Backspace edits the word; deleting past its start makes it "unknown" until the next
   separator. Enter is deliberately not a separator (Teams sends on Enter).
3. **Rendering** (`Paste/RichContentRenderer.cs`): snippets are stored as WPF XamlPackage (base64).
   The renderer loads one into a `FlowDocument` and produces:
   - plain text (Notepad),
   - RTF from WPF's own exporter (keeps images),
   - an HTML fragment written by hand: bold/italic/underline/strike, colour and size only where they
     differ from the body text, alignment, lists, tables, `http`/`https`/`mailto` links, images as
     base64 PNG. Images larger than their displayed size are downscaled first.
4. **Pasting** (`Paste/ClipboardPasteService.cs`, `Input/InputInjector.cs`): the three formats go on the
   clipboard together (`CF_HTML` is built byte-exact in `Paste/CfHtml.cs`), then one `SendInput` call
   sends Backspaces for the trigger + space and Ctrl+V. Held Shift/Ctrl/Alt/Win are released first so the
   paste is not turned into "paste as plain text". After 400 ms (+1 ms per KB of payload) the previous
   clipboard is restored, but only if the clipboard sequence number shows nobody else changed it.

Why these formats: see [COMPAT.md](COMPAT.md). Word and Teams both use the HTML; Teams ignores RTF and
drops `file:///` images, so images must be embedded.

## Project layout

```
BananaNana/                     the app
  App.xaml(.cs)                 startup: single instance, settings, language, tray mode, welcome guide
  MainWindow                    library list, preview, status bar, tips, tray integration
  EditorWindow                  snippet editor (toolbar, links, picture paste, resize, image editing)
  ImageEditorWindow             arrows, frames, highlights on a picture
  HelpWindow                    help guide / welcome screen
  SettingsWindow, LinkDialog
  ExpansionCoordinator.cs       library ⇄ input ⇄ paste glue
  LibraryStore.cs               library JSON (source-generated serializer), duplicate checks
  RichContent.cs                editor ⇄ XamlPackage storage
  ImageResizeAdorner.cs         corner handles; EditorImages.FixSizes (natural size, capped 500×350)
  ImageAnnotation.cs            arrow geometry, flattening drawings into the picture
  LinkUrl.cs                    link validation/normalisation (http, https, mailto only)
  TrayIcon.cs, Autostart.cs     notification-area icon; HKCU Run entry with --tray
  AppSettings.cs                %APPDATA%\BananaNana\settings.json (language, welcome shown)
  Loc.cs, Strings/*.xaml        translations
  Input/                        Raw Input listener, matcher, SendInput injector, P/Invoke
  Paste/                        renderer, CF_HTML builder, RTF writer, clipboard service
tests/BananaNana.Tests/         xUnit (links the app's source files; WPF tests run on an STA thread)
tools/PasteLab/                 test bench: clipboard format variants A–F, live expansion test
docs/                           documentation
```

## Data and files

| File | Content |
|---|---|
| `BananaNana.library.json` (next to the EXE or in `Documents\BananaNana`) | `{ "Name", "Snippets": [ { "Trigger", "Content" } ] }`, Content = base64 WPF XamlPackage (a zip with the document XAML and its images). Saved atomically via a `.tmp` file. |
| `%APPDATA%\BananaNana\settings.json` | `{ "Language", "WelcomeShown" }` |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\BananaNana` | `"<exe>" --tray` when Start with Windows is on |

## Single instance and tray

`App.OnStartup` takes a named mutex `Local\BananaNana.Instance`. A second launch signals the event
`Local\BananaNana.Show` (the first instance shows its window) and exits. The environment variable
`BANANANANA_INSTANCE` changes these names and the settings file name, so a test copy can run next to a
real one. The main window's close button hides it; Exit in the tray menu shuts down
(`ShutdownMode=OnExplicitShutdown`). `--tray` starts without showing the window.

## Translations

`Strings/Strings.en.xaml` is the reference. `Loc.Apply` always merges English first and the chosen language
on top, so missing keys fall back to English. XAML uses `{DynamicResource Key}` (updates live); code uses
`Loc.T("Key")` / `Loc.F("Key", args)` and re-renders its own texts on `Loc.Changed`. Every window gets its
`Language` set to the culture (`zh-CN`, `th-TH`, ...) so WPF picks the right font; without it, Chinese was
drawn with Japanese glyph shapes.

**Adding a language:** copy `Strings.en.xaml` to `Strings.xx.xaml`, translate the values (keep `{0}`
placeholders, never start a value with `{`), add `("xx", "Native name")` to `Loc.Languages` and the culture
to `Loc.Cultures`. `TranslationTests` fails until the file is complete.

## UI

WPF Fluent theme (`Application.ThemeMode="System"`, marked experimental in .NET 10, warning WPF0001
suppressed): follows Windows light/dark mode and accent colour, Mica backdrop. Icons are Segoe Fluent
Icons glyphs (Segoe MDL2 Assets on Windows 10). Snippet content is always shown on white "paper".
`AutomationProperties.Name` stays English on purpose: UI automation tests find controls by it in any language.

## Testing

- **Unit tests** (`dotnet test`, 47): CF_HTML offsets, RTF escaping, renderer output (formatting, lists,
  alignment, links, image sizes and downscaling, XamlPackage round trips), trigger matcher, link
  validation, image resize and annotation, library file format (byte-identical to earlier versions),
  autostart on a scratch registry key, translation completeness.
- **End-to-end** (manual scripts, not in the repo): a separate build driven by UI Automation and
  `SendInput` into a real Word document via COM: create snippets in the editor, add links, draw on
  pictures, expand, check the clipboard is restored, tray behaviour. They run under
  `BANANANANA_INSTANCE=BananaNanaTest` with a throwaway library next to the test EXE, never the user's
  library.

## Build and release

See the README for the publish commands (standalone ~66 MB, small ~0.4 MB needing .NET Desktop Runtime 10).

Trimming was tried and rejected: full trimming crashes on startup (XAML types removed); partial trimming
needed `BuiltInComInteropSupport` and a source-generated JSON serializer (now in place), then ran the UI but
silently stopped expanding snippets. Microsoft does not support trimming WPF apps.

Windows Forms is referenced only for the tray icon (`NotifyIcon`), which adds about 7 MB to the standalone
EXE. A hand-written `Shell_NotifyIcon` wrapper could remove it.

## Known limitations

- No password-field, elevated-app or excluded-app checks yet; elevated apps silently ignore our input (UIPI).
- Characters typed between a match and the injected Backspaces could be deleted instead of the trigger
  (rare; injection happens within milliseconds).
- After a click or focus change the matcher cannot see the text before the caret, so a trigger typed
  directly after existing text there still expands.
- Dead-key compositions are not seen by the matcher (triggers should use plain characters).
- Images are stored and pasted as PNG; large photos can be big.
