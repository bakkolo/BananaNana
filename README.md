# Banana Nana

Portable Windows text expander (C# / WPF / .NET 10, Windows 11 Fluent theme). Type a trigger such as `;offer` followed by a space in any app
and it is replaced by a saved snippet with formatting, lists, links and images. No installation, no admin rights.

Status: editor, snippet library, live expansion (Word, Teams, Notepad verified), tray icon, start with Windows and single-file EXE work.
Not done yet: password-field / elevated-app / excluded-app checks (Stage 4), select-option fields.
Plan and findings: `docs/PLAN.md`, `docs/RESEARCH.md`, `docs/COMPAT.md`.

## Build and test
Needs the .NET 10 SDK or newer:

```
dotnet build
dotnet test
```

`tools/PasteLab` is a test bench for clipboard formats and live expansion.

## Single EXE
Two variants; both are one file and need no installation of the app itself.

**Standalone** (about 66 MB, .NET included, runs on any 64-bit Windows 10/11):
```
dotnet publish BananaNana/BananaNana.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish/standalone
```

**Small** (about 0.3 MB, needs the free .NET Desktop Runtime 10 or newer installed once; without it Windows shows a download link):
```
dotnet publish BananaNana/BananaNana.csproj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -p:DebugType=none -o publish/small
```

Delete `BananaNana/obj/Release` between the two publishes, or the second may reuse the first one's settings.
Trimming is not used: Microsoft does not support it for WPF apps.

The library is `BananaNana.library.json` next to the EXE or in `Documents\BananaNana`.
