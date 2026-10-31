# Banana Nana: research findings (2026-10-31)

**In short:** the plan holds, with four changes. The biggest unknown, whether Word, Outlook and Teams show pasted images, can't be answered from the web. Test it on Windows first (stage 1 in `docs/PLAN.md`).

## Changes to the original handoff
1. **Use Raw Input for keyboard detection instead of `WH_KEYBOARD_LL`.** Espanso does this (`RegisterRawInputDevices`, `RIDEV_INPUTSINK`). Windows can silently remove a low-level hook if its callback is slow, and antivirus treats hooks with more suspicion.
2. **Convert keys with `ToUnicodeEx` flag `0x4`, using the foreground window's layout** (`GetKeyboardLayout` of the foreground thread). Without it, dead keys and Polish layouts break. Skip Alt+key combos but keep AltGr, which Polish uses for ą, ę and other letters.
3. **Tray: use the built-in `System.Windows.Forms.NotifyIcon`** (`<UseWindowsForms>true</UseWindowsForms>`). H.NotifyIcon (MIT) works too, but it's an extra dependency we don't need.
4. **Don't use a third-party HTML↔RTF converter.** Store content as XamlPackage for editing. Write a small FlowDocument→HTML converter (about 200 lines) and use WPF's built-in `TextRange.Save(DataFormats.Rtf)` for RTF. Html2OpenRtf (MIT) is too early and doesn't list image support.

## How each piece works
| Piece | How |
|---|---|
| Typing plain text | `SendInput` with `KEYEVENTF_UNICODE`, all events in one call; optional per-key delay for apps that drop fast input. Check the return value. Espanso doesn't. |
| Rich paste | Put CF_UNICODETEXT, RTF and "HTML Format" (correct StartHTML/EndHTML/StartFragment/EndFragment byte offsets, UTF-8) on the clipboard, then send Ctrl+V. |
| Clipboard locks | Retry `OpenClipboard` several times with a short sleep. Free the memory if `SetClipboardData` fails. |
| Clipboard restore | Save the old contents, paste, wait about 300–500 ms, then restore. Best effort only. |
| Password fields | UI Automation `AutomationElement.FocusedElement` → `IsPassword`, only on match, off the input thread, with a timeout. It can be slow on some apps. |
| Elevated apps | UIPI blocks our input to them. Detect it (foreground process token elevation; access denied means treat as elevated) and show a tray warning. |
| Autostart | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, no admin needed. |

## Still unknown: test first on Windows
- Do **Word and Outlook** show images from HTML with an embedded base64 `data:` URI, or do they need RTF `\pict` images? Sources contradict each other.
- Does **new Teams** (built on Chromium) accept base64 images from pasted HTML? `file:///` images are likely blocked there.
- Does WPF's RTF export keep images on .NET 8? Not documented.
- If one format can't serve all three apps, **pick the format per app** based on the foreground process (`WINWORD.EXE`/`OUTLOOK.EXE` vs `ms-teams.exe`).

## Projects to learn from
- **Espanso** (Rust, GPL-3.0): read its design only, don't copy code. It pastes images and formatted text separately (HTML + text, or `CF_BITMAP`), so "text + image in one paste" goes beyond it. Its Windows clipboard code has bugs (no retries, deletes the bitmap after `SetClipboardData`, memory leaks on failure), so don't model ours on it.
- **Beeftext** (C++/Qt, MIT, maintenance mode): code can be studied and reused legally.
- No existing C# project fits, so we build our own.

## Sources
- Espanso inject: https://github.com/espanso/espanso/tree/dev/espanso-inject/src/win32
- Espanso detect: https://raw.githubusercontent.com/espanso/espanso/dev/espanso-detect/src/win32/native.cpp
- Espanso clipboard: https://raw.githubusercontent.com/espanso/espanso/dev/espanso-clipboard/src/win32/native.cpp
- Microsoft, HTML Clipboard Format: https://learn.microsoft.com/en-us/windows/win32/dataxchg/html-clipboard-format
- Markdown Monster, missing images: https://markdownmonster.west-wind.com/docs/_64m0mxxl7.htm
- Joplin, base64 images on the clipboard: https://discourse.joplinapp.org/t/embed-images-as-base64-when-copying-text-images-to-clipboard/45511
- Claude Code issue #57795: https://github.com/anthropics/claude-code/issues/57795
- TextRange.Save: https://learn.microsoft.com/en-us/dotnet/api/system.windows.documents.textrange.save?view=netstandard-1.3
- Html2OpenRtf: https://www.nuget.org/packages/Html2OpenRtf
- H.NotifyIcon: https://github.com/HavenDV/H.NotifyIcon
- IsPassword: https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.automationelementinformation.ispassword
- Beeftext: https://github.com/xmichelo/Beeftext
