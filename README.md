# AnonymousSurvey — ankiety anonimowe

Projekt rekrutacyjny: Blazor Server (.NET 10), Clean Architecture, SQL Server (LocalDB / Express), EF Core, DevExpress Blazor (Demo/trial). Interfejs w języku polskim.

Jedna instalacja obsługuje wiele ankiet. Publiczny adres to `/ankieta/{slug}`. `/` otwiera ankietę domyślną.

## Wymagania

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server **LocalDB** (z Visual Studio / Build Tools) lub **SQL Server Express**
- Dostęp do nuget.org (pakiet `DevExpress.Blazor` 26.1.x jest publicznie dostępny)
- Do testów E2E: Node.js, `sqlcmd` i zainstalowany Microsoft Edge
- Opcjonalnie: licencja DevExpress (trial lub pełna) — bez niej aplikacja działa, ale pokazuje baner „evaluation” (patrz niżej)

## Szybki start

```powershell
dotnet build
dotnet test
dotnet run --project src\AnonymousSurvey.Web --launch-profile http
```

Aplikacja nasłuchuje na `http://localhost:5202` (profil `http`) lub `https://localhost:7069` (profil `https`).
Przy pierwszym uruchomieniu baza `AnonymousSurvey` na LocalDB jest tworzona automatycznie i wypełniana danymi seed.

## Trasy

| Ścieżka | Kto | Opis |
|---------|-----|------|
| `/` | każdy | Przekierowanie do aktywnej ankiety domyślnej. Gdy domyślna nie jest aktywna — najwcześniej utworzona aktywna. Gdy nie ma żadnej aktywnej — komunikat „Brak aktywnych ankiet.” i link „Utwórz ankietę”. |
| `/ankieta/{slug}` | każdy | Formularz ankiety aktywnej. Slug nie rozróżnia wielkości liter i nie jest przekierowywany na inną wielkość. |
| `/ankieta/{slug}/dziekujemy` | każdy | Potwierdzenie zapisu odpowiedzi tej ankiety. |
| `/nowa-ankieta` | każdy (znający adres) | Tworzenie ankiety bez tokenu. |
| `/wyniki/{token}` | posiadacz tokenu wyników | Agregaty i siatka odpowiedzi. |
| `/konfiguracja/{token}` | posiadacz tokenu konfiguracji | Tytuł, opis, status, flaga domyślna, pytania. |

Błędny token albo nieznany slug (także szkic) daje komunikat „Nie znaleziono strony lub adres jest nieprawidłowy”.

Tokeny seedowej ankiety „Ankieta satysfakcji” (`004_Seed.sql`):

| Widok | Token | Pełny adres (profil `http`) |
|-------|-------|------------------------------|
| Wyniki | `wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13` | `http://localhost:5202/wyniki/wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13` |
| Konfiguracja | `konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538` | `http://localhost:5202/konfiguracja/konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538` |
| Formularz | slug `ankieta-satysfakcji` | `http://localhost:5202/ankieta/ankieta-satysfakcji` |

W środowisku produkcyjnym tokeny należy zmienić w bazie (tabela `Survey`). Seed: 5 pytań przypisanych (2× skala 1–5, 2× Tak/Nie, 1× otwarte, wszystkie wymagane) i 3 dodatkowe pytania w puli. Ankieta seedowa jest aktywna i domyślna.

## Statusy

| Status | W bazie | Formularz publiczny |
|--------|---------|---------------------|
| Szkic | `1` | Niewidoczny („Nie znaleziono”). |
| Aktywna | `2` | Przyjmuje odpowiedzi. Musi mieć co najmniej jedno pytanie. |
| Zamknięta | `3` | Widoczna, ale bez formularza: „Ta ankieta jest zamknięta i nie przyjmuje już odpowiedzi.” |

Ankieta domyślna może być szkicem albo zamknięta. Pod checkboxem jest wtedy podpowiedź, że `/` przejdzie do innej aktywnej ankiety.

## Wymagane i opcjonalne

Flaga „Wymagane” jest przy przypisaniu pytania do ankiety, nie przy pytaniu w puli. Na formularzu wymagane ma gwiazdkę. Pytanie opcjonalne można pominąć; skala i Tak/Nie mają przycisk „Wyczyść”. Ankieta bez żadnej odpowiedzi jest odrzucana komunikatem „Odpowiedz na co najmniej jedno pytanie.” Puste opcjonalne nie trafia do bazy. Odpowiedź otwarta ma co najwyżej 2000 znaków.

## Pula pytań i limit 30

Pytanie jest unikalne po parze (typ, tekst). Nowe pytanie (tekst + typ) dodane przy tworzeniu albo w konfiguracji trafia do wspólnej puli i **tylko do tej ankiety**. Pozostałe ankiety dostają je dopiero, gdy ktoś doda je ręcznie z puli.

Jedna ankieta ma co najwyżej 30 pozycji. Po osiągnięciu limitu przyciski „Dodaj” i „Dodaj pytanie” są wyłączone. W puli jest wyszukiwarka (wielkość liter i spacje na brzegach nie mają znaczenia).

Usunięcie pytania albo zmiana „Wymagane” nie kasuje złożonych odpowiedzi. Wyniki zostawiają historyczne dane; pytanie zdjęte z ankiety ma badge „usunięte z ankiety”.

## Tworzenie ankiety i tokeny

`/nowa-ankieta` nie wymaga tokenu. Po „Utwórz ankietę” ekran pokazuje trzy linki (publiczny, wyniki, konfiguracja) i przycisk „Kopiuj”. Tokeny są tylko w stanie strony — nie wracają w adresie i nie są pokazywane ponownie. Kto zna link konfiguracji, może zmieniać ankietę. Zapisz linki od razu.

Slug powstaje z tytułu (polskie znaki są ściągane, długość 3–60) albo z pola „Własny adres”. Zajęty slug jest odrzucany.

## Ankieta domyślna

Dokładnie jedna ankieta ma `IsDefault`. `/` prowadzi do niej, o ile jest aktywna. Ustawienie flagi na innej ankiecie zdejmuje ją z poprzedniej.

## Współbieżność konfiguracji

Zapis konfiguracji niesie `RowVersion`. Gdy ktoś inny zapisał ankietę w międzyczasie, druga karta dostaje „Konfiguracja została zmieniona przez kogoś innego. Odśwież stronę.” i przycisk „Wczytaj aktualną konfigurację”. Niezapisane zmiany przy opuszczeniu edytora potwierdza przeglądarka (`NavigationLock`).

## Baza danych (LocalDB / Express)

W `appsettings.Development.json` jest connection string `SurveyDb` dla LocalDB:

`Server=(localdb)\mssqllocaldb;Database=AnonymousSurvey;...`

Dla SQL Express zmień serwer, np. na `.\SQLEXPRESS`.

Przy starcie aplikacji (`Database:InitializeOnStartup`, domyślnie `true`) `DatabaseInitializer` przez EF Core:

1. tworzy bazę, jeśli jej nie ma,
2. wykonuje idempotentne skrypty z `src/AnonymousSurvey.Infrastructure/Sql/` (osadzone jako zasoby):
   `001_Schema.sql` (tabele), `002_fn_GetSurveyQuestions.sql` (TVF), `003_usp_SaveSurveyResponse.sql` (SP), `004_Seed.sql` (ankieta, pula pytań, tokeny).

Skrypty podnoszą też bazę ze starego schematu (sprzed slug/status/`IsRequired`). Istniejące odpowiedzi zostają. Skrypty można uruchomić ręcznie w SSMS/`sqlcmd` w kolejności numerów. Aby wyłączyć automatyczną inicjalizację: `"Database": { "InitializeOnStartup": false }`.

### Reset bazy deweloperskiej

Kolejny start utworzy bazę od zera i wgra seed. Zatrzymaj aplikację, potem:

```powershell
sqlcmd -S "(localdb)\mssqllocaldb" -Q "IF DB_ID(N'AnonymousSurvey') IS NOT NULL BEGIN ALTER DATABASE [AnonymousSurvey] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [AnonymousSurvey]; END"
```

Bazy `AnonymousSurvey_E2E_*` i `AnonymousSurvey_Test_*` należą do testów i są usuwane po przebiegu. Nie kasuj ich, gdy test jeszcze działa. Osierocone (starsze niż godzina) sprząta kolejny przebieg.

### Pytania z TVF przez EF Core

- T-SQL: `dbo.fn_GetSurveyQuestions(@SurveyId)` zwraca `SurveyId, QuestionId, Text, Type, SortOrder, IsRequired` (aktywne pytania przypisane do ankiety).
- EF Core: keyless typ `SurveyQuestionRow` + `modelBuilder.HasDbFunction(...)` na metodzie `SurveyDbContext.GetSurveyQuestions(int)` (`IQueryable<SurveyQuestionRow>`).
- `SurveyQuestionQuery` (`ISurveyQuestionQuery`) odczytuje pytania formularza **wyłącznie** przez tę funkcję — formularz i walidacja nie czytają pytań zwykłym LINQ po tabelach.

### Zapis odpowiedzi przez SP przez EF Core

- T-SQL: `dbo.usp_SaveSurveyResponse @SurveyId, @SessionId, @AnswersJson` — w jednej transakcji tworzy `ResponseSession` oraz wiersze `Answer` (JSON przez `OPENJSON`). Ankieta musi być aktywna. Wymagane pytania muszą mieć odpowiedź; opcjonalne można pominąć; potrzebna jest co najmniej jedna odpowiedź.
- EF Core: `SurveyResponseSaver` (`ISurveyResponseSaver`) wywołuje ją przez `Database.ExecuteSqlInterpolatedAsync` — bez `DbSet.Add` / `SaveChanges` na encjach `Answer`.
- Anonimowość: sesja = GUID + czas UTC; nie zapisujemy IP ani identyfikatorów użytkownika. Podwójne kliknięcie „Wyślij” jest blokowane w przeglądarce (przycisk), bez identyfikatora idempotencji po stronie serwera.

## Polimorfizm

- **Statyczny** (przeciążenia): `ISurveyResponseValidator.Validate(AnswerInputDto, SurveyQuestionDto)` — pojedyncza odpowiedź, oraz `Validate(IReadOnlyList<AnswerInputDto>, IReadOnlyList<SurveyQuestionDto>)` — cały formularz.
- **Dynamiczny** (dziedziczenie / metody wirtualne):
  - `BasePage` (abstrakcyjna) — wspólny cykl ładowania, stany: ładowanie / błąd / brak strony; strony `HomePage`, `SurveyFormPage`, `ThankYouPage`, `ResultsPage`, `ConfigPage`, `NewSurveyPage` implementują `PageTitle` i `LoadAsync`.
  - `QuestionInputBase` — bazowy komponent pytania; `ScaleQuestionInput`, `YesNoQuestionInput`, `OpenQuestionInput` wybierane w `QuestionInput` przez `DynamicComponent` na podstawie typu pytania.

## DevExpress (Demo / trial)

Pakiet `DevExpress.Blazor` 26.1.5 pochodzi z nuget.org. Rejestracja jest zgodna z oficjalnym szablonem z paczki `DevExpress.Blazor.ProjectTemplates` (`dotnet new dx.blazor`):

- `Program.cs` — `AddDevExpressBlazor(options => options.SizeMode = SizeMode.Medium)`,
- `App.razor` — `@DxResourceManager.RegisterTheme(Themes.OfficeWhite)` i `@DxResourceManager.RegisterScripts()` w `<head>`.

Z szablonu **nie** przejmujemy shella demo (Drawer, NavMenu, strony przykładowe, `site.css` z `overflow: hidden`) — ankieta anonimowa nie powinna pokazywać nawigacji do widoków administracyjnych. Referencyjny projekt można wygenerować lokalnie:

```powershell
dotnet new dx.blazor -n DxTemplate -o _local\dx-template -f net10.0 -int Server -th OfficeWhite -sz Medium
```

Używane komponenty: `DxButton`, `DxRadioGroup`, `DxMemo`, `DxTextBox`, `DxCheckBox`, `DxGrid`.

**Ostrzeżenie DX1000 / DX1001 przy kompilacji jest oczekiwane** („For evaluation purposes only”) — to wersja Demo/trial bez zainstalowanej licencji. W działającej aplikacji pojawia się pomarańczowy baner evaluation u góry strony. Nie blokuje to działania. Testy E2E ignorują ten komunikat w konsoli.

Ostrzeżenie `DX1000` pojawia się także w świeżo wygenerowanym projekcie z oficjalnego szablonu `dx.blazor`, więc nie wynika z kodu tej aplikacji, tylko z zakresu zainstalowanej licencji (musi obejmować `DevExpress.Blazor`, np. subskrypcja ASP.NET / Universal / DXperience lub aktywny trial).

Aby użyć własnej licencji (klucza **nie** commitujemy):

1. Pobierz `DevExpress_License.txt` z <https://www.devexpress.com/DX1001> i umieść w `%AppData%\DevExpress\` (Windows), **albo**
2. ustaw zmienną środowiskową `DevExpress_License` na zawartość klucza, **albo**
3. zapisz klucz w User Secrets — `Program.cs` przekaże go do zmiennej `DevExpress_License`, jeśli ta nie jest ustawiona (nie sprawdzone bez realnego klucza):

```powershell
cd src\AnonymousSurvey.Web
dotnet user-secrets init
dotnet user-secrets set "DevExpress:LicenseKey" "TWÓJ_KLUCZ"
```

Ograniczenie: wbudowane teksty komponentów DevExpress (np. „Page Size”, komunikaty filtra) pozostają po angielsku — lokalizacja wymaga osobnych zasobów DevExpress.

## Polecenia

```powershell
dotnet test
powershell -File scripts\verify.ps1                         # build, testy jednostkowe, integracyjne
powershell -File scripts\verify.ps1 -E2E                    # jw. + Playwright na porcie 5210
powershell -File scripts\verify.ps1 -E2E -E2EPort 0         # port losowy z zakresu 5300–5399
powershell -File scripts\verify.ps1 -E2E -E2EFilter smoke   # podzbiór E2E (--grep)
powershell -File scripts\run-e2e.ps1 -Port 5210             # samo E2E, świeża baza, proces zabijany na końcu
powershell -File scripts\mutation.ps1                       # Stryker.NET (próg doradczy)
powershell -File scripts\verify.ps1 -Mutation               # build, testy i mutacje
```

- `AnonymousSurvey.UnitTests` — walidator odpowiedzi, `SlugGenerator`, model edytora pozycji, zapis formularza.
- `AnonymousSurvey.IntegrationTests` — tymczasowa baza na LocalDB (`(localdb)\mssqllocaldb`), usuwana po testach: TVF, SP, katalog, konfiguracja, upgrade starego schematu i start aplikacji na zaktualizowanej bazie.
- `tests/e2e` — Playwright (`@playwright/test`, kanał Edge `msedge`). `run-e2e.ps1` tworzy `AnonymousSurvey_E2E_<guid>`, ustawia `BASE_URL` i `E2E_DB`, a w `finally` zabija proces i usuwa bazę. Opis struktury: `tests/e2e/README.md`.

Stryker (`.config/dotnet-tools.json`, `stryker-config.json`) mutuje `SurveyResponseValidator`, `SlugGenerator` i `SurveyItemsEditorModel`. Cel doradczy to co najmniej 80%. Niższy wynik nie przerywa `mutation.ps1` (`thresholds.break` = 0); ocalałe mutanty są w raporcie cleartext i w `reports/stryker/` (katalog jest poza gitem).

## Zakres zmian w zarządzaniu ankietami

- Wiele ankiet pod `/ankieta/{slug}`, statusy Szkic / Aktywna / Zamknięta, ankieta domyślna na `/`.
- Tworzenie z `/nowa-ankieta` z jednorazowymi linkami do wyników i konfiguracji.
- Nowe pytanie trafia do puli i tylko do ankiety, w której powstało. Limit 30 pozycji.
- Wymagane / opcjonalne per przypisanie; wyniki „Odpowiedziało X z N” liczą tylko sesje złożone po dodaniu pytania.
- Blokada optymistyczna konfiguracji (`RowVersion`) i ostrzeżenie o niezapisanych zmianach.
- Upgrade starej bazy przy starcie oraz testy E2E (Playwright) i mutacje (Stryker, doradczo).

## Struktura

```
AnonymousSurvey.sln
  src/AnonymousSurvey.Domain          # encje, QuestionType, SurveyStatus
  src/AnonymousSurvey.Application     # DTO, walidator, SlugGenerator, serwis zapisu
  src/AnonymousSurvey.Infrastructure  # EF Core, TVF/SP, skrypty SQL, repozytoria
  src/AnonymousSurvey.Web             # Blazor Server + DevExpress, model edytora
  tests/AnonymousSurvey.UnitTests
  tests/AnonymousSurvey.IntegrationTests
  tests/e2e                           # Playwright (poza solution)
  scripts/verify.ps1                  # bramka: build + testy + opcjonalnie E2E i Stryker
  scripts/run-e2e.ps1
  scripts/mutation.ps1
  _local/                             # poza gitem — specs i notatki lokalne
```

## Specyfikacja lokalna

Kopia designu i planu implementacji: `_local\specs\` (nie trafia do repozytorium).
