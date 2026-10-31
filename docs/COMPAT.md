# Banana Nana: paste compatibility (Stage 1 gate)

Tool: `tools/PasteLab` (`dotnet run --project tools/PasteLab`). Click a variant, switch to the app, Ctrl+V.

Sample in every variant: **bold** / *italic* / underlined line with Polish characters, a 3-item bullet list, a yellow "Banana Nana" image (240×96).

Legend per cell: `B` bold/italic/underline OK · `L` list OK · `I` image shows · `PL` Polish characters OK · `T` plain text only · `✗` nothing pasted. Add notes for anything odd (wrong size, broken image icon, extra spacing).

| Variant | Notepad | Word | Outlook (new email) | Teams (chat) |
|---|---|---|---|---|
| A. HTML, base64 `data:` image | T PL | B L I PL | | |
| B. HTML, `file:///` image | T PL | B L I PL (image embedded, not linked) | | |
| C. RTF, `\pngblip` image | T PL | B I PL, list as `•` chars only | | |
| D. Text + RTF + HTML together | T PL | B L I PL (Word used the HTML) | | |
| E. WPF built-in RTF export | T PL | B L I PL | | |
| F. Image only (bitmap + PNG) | ✗ | I | | |

Test environment: Word 16 (Microsoft 365) · Teams: new Teams (`ms-teams.exe`) · Date: 2026-10-31
Word column: confirmed twice, by COM automation and by a manual paste saved as `Banana Nana test.docx` (6 embedded images at 240×96 px, real lists in A/B/D/E).
Outlook: classic Outlook has no mail profile on this PC, so COM automation aborted; test the new Outlook by hand.

## Already known (automated, 2026-10-31)
- All six variants place the expected clipboard formats (checked via UI Automation).
- WPF `TextRange.Save(DataFormats.Rtf)` **does** emit the image as `\pict` (tested on the .NET 10 desktop runtime via roll-forward; re-check on a self-contained .NET 8 build).

## Decision
_To fill in after the matrix: one format for all apps, or per app by foreground process._
