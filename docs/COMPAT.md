# Banana Nana: paste compatibility (Stage 1 gate)

Tool: `tools/PasteLab` (`dotnet run --project tools/PasteLab`). Click a variant, switch to the app, Ctrl+V.

Sample in every variant: **bold** / *italic* / underlined line with Polish characters, a 3-item bullet list, a yellow "Banana Nana" image (240×96).

Legend per cell: `B` bold/italic/underline OK · `L` list OK · `I` image shows · `PL` Polish characters OK · `T` plain text only · `✗` nothing pasted. Add notes for anything odd (wrong size, broken image icon, extra spacing).

| Variant | Notepad | Word | Outlook (new email) | Teams (chat) |
|---|---|---|---|---|
| A. HTML, base64 `data:` image | | | | |
| B. HTML, `file:///` image | | | | |
| C. RTF, `\pngblip` image | | | | |
| D. Text + RTF + HTML together | | | | |
| E. WPF built-in RTF export | | | | |
| F. Image only (bitmap + PNG) | | | | |

Test environment: Word/Outlook version: ___ · Teams: new Teams (`ms-teams.exe`) · Date: ___

## Already known (automated, 2026-10-31)
- All six variants place the expected clipboard formats (checked via UI Automation).
- WPF `TextRange.Save(DataFormats.Rtf)` **does** emit the image as `\pict` (tested on the .NET 10 desktop runtime via roll-forward; re-check on a self-contained .NET 8 build).

## Decision
_To fill in after the matrix: one format for all apps, or per app by foreground process._
