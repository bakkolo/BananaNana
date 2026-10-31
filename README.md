# Banana Nana

Portable Windows text expander (C# / WPF / .NET 8). Type a trigger such as `;offer` followed by a space in any app
and it is replaced by a saved snippet with formatting, lists, links and images. No installation, no admin rights.

Status: editor, snippet library, live expansion (Word, Teams, Notepad verified) and single-file EXE work.
Not done yet: tray icon and autostart, password-field / elevated-app / excluded-app checks (Stage 4), select-option fields.
Plan and findings: `docs/PLAN.md`, `docs/RESEARCH.md`, `docs/COMPAT.md`.

## Build and test
Needs the .NET 8 SDK or newer:

```
dotnet build
dotnet test
```

`tools/PasteLab` is a test bench for clipboard formats and live expansion.

## Single EXE
```
dotnet publish BananaNana/BananaNana.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

Produces `publish/BananaNana.exe` (about 70 MB, .NET included). Copy it anywhere and run it.
The library is `BananaNana.library.json` next to the EXE or in `Documents\BananaNana`.
