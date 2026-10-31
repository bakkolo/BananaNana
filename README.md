# Banana Nana

Starter i instrukcje dla Codexa. Windows WPF/.NET 8, portable. **To nie jest gotowa aplikacja**: globalny silnik, settings, tray, clipboard rich text oraz testy wymagają dokończenia. Kod nie był kompilowany na Windows.

## Budowanie
Na Windows z .NET 8 SDK:
`dotnet build BananaNana/BananaNana.csproj`

Publikacja po testach:
`dotnet publish BananaNana/BananaNana.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`

## Praca z Codex
Otwórz cały folder w Codex i poproś: `Read AGENTS.md and implement the complete MVP in its priority order. Build and test after each stage.`
