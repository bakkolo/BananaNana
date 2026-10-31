# Banana Nana: handoff dla Codexa

> **Historical (2026-10-31).** This is the original Polish handoff that started the project. The MVP has
> since been built; for the current state see `README.md`, `docs/ARCHITECTURE.md` and `docs/USER_GUIDE.md`.
> Where this file and the code disagree, the code and those documents win.

## Cel i decyzje
Windows portable, C# WPF, jeden self-contained EXE bez admina. Kod startowy istnieje, ale NIE został skompilowany ani przetestowany na Windows. Nie ogłaszaj ukończenia bez testów.

## UI
Główne okno: lista skrótów, wyszukiwarka, podgląd, Nowy/Importuj/Eksportuj/Ustawienia. Dwuklik otwiera OSOBNE okno edytora. Edytor: pogrubienie, kursywa, podkreślenie, obrazy, wklejanie z Worda. Zapisuj obrazy i formatowanie; obecny kod używa XamlPackage (base64). Sprawdź zgodność z realnym wklejaniem z Worda i innymi edytorami.

## Biblioteki
Nazwa pliku BananaNana.library.json. Standardowe miejsca: folder EXE i Dokumenty/BananaNana. Jeden plik: otwórz, dwa: zapytaj, zero: w imporcie pokaż dialog wyboru. Eksport zawsze z wyborem lokalizacji. Zapisz wybraną ścieżkę w ustawieniach. Ustawienia: ścieżka, nazwa biblioteki, autostart. Zablokuj duplikaty triggerów bez rozróżniania wielkości liter. Dodaj wersjonowanie formatu, backupy i obsługę plików tylko do odczytu.

## OBOWIĄZKOWE MVP: globalne rozwijanie skrótów
To nie jest funkcja na później. Np. wpisanie `;oferta` i separatora w Teams/Outlook/Word zastępuje kod zapisaną wiadomością z formatowaniem i obrazami. Program działa w trayu, ma pauzę i wyjście.

## Kolejność implementacji
1. Najpierw zrób **pionowy prototyp wklejania**: zapisz rich text + obraz w edytorze, przekształć do poprawnego pakietu schowka (Unicode, RTF, CF_HTML, obrazy), wklej w Word/Teams/Outlook i przetestuj. To NAJWYŻSZY priorytet techniczny. Nie zakładaj, że XamlPackage jest akceptowane przez inne aplikacje.
2. Następnie `ClipboardPasteService`, `RichContentRenderer`, `ClipboardRestoreService`. Przywracanie schowka best-effort, z uwzględnieniem opóźnień i aplikacji asynchronicznych.
3. `KeyboardHookService` na Win32 WH_KEYBOARD_LL + `TriggerMatcher` z ograniczonym buforem i obsługą Backspace, separatorów, Unicode, PL/EN. Hook nie zapisuje historii klawiszy, ignoruje własne SendInput.
4. `ExpansionCoordinator`, `ForegroundWindowService`, `AppExclusionService`. Nie rozwijaj w polach haseł ani w samej aplikacji. Nie obchodź UIPI i nie wymagaj administratora.
5. Zasobnik systemowy, autostart per-user, okno ustawień, zapamiętywanie ścieżki, bezpieczny import/eksport, testy regresyjne.
6. Publikacja self-contained single-file i testy na czystym Windows bez SDK.

## Inspiracja Espanso, odtworzyć, nie kopiować
Repo: https://github.com/espanso/espanso . Przejrzyj moduły `espanso-match` (rolling matcher), `espanso-detect` (detekcja), `espanso-inject` (wstawianie), `espanso-engine` (koordynacja), `espanso-config` (reguły aplikacji). Implementuj samodzielnie w C#. Nie kopiuj źródeł Rust ani assetów GPL. Warto rozważyć cofnięcie ekspansji przez Backspace i paletę wyszukiwania w późniejszej wersji.

## Testy akceptacyjne
- Edycja w osobnym oknie; podgląd, obrazy, Unicode, brak duplikatów.
- Import 0/1/2 bibliotek, eksport, ścieżka, nazwa, autostart.
- Globalne wpisanie triggera w Notatniku, Wordzie, Outlooku i Teamsie.
- Tekst pogrubiony, lista, obraz, wielokrotne wyzwolenia, szybkie pisanie, zajęty schowek.
- Polskie znaki, przełączenie fokusu, cofanie, aplikacje wykluczone, brak administratora.
- Nie zakładaj pełnej zgodności formatowania w każdej aplikacji; udokumentuj ograniczenia.

## Najpierw przeczytaj
`docs/RESEARCH.md` (wyniki badan, zmieniaja czesc tego planu: Raw Input zamiast WH_KEYBOARD_LL) oraz `docs/PLAN.md` (etapy). Jesli cos tu jest sprzeczne z RESEARCH.md, wygrywa RESEARCH.md.
