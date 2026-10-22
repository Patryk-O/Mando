# AnonymousSurvey — ankieta anonimowa

Projekt rekrutacyjny: Blazor Server (.NET 10), Clean Architecture, SQL Server (LocalDB / Express), EF Core, DevExpress Blazor (Demo/trial). Interfejs w języku polskim.

## Wymagania

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server **LocalDB** (z Visual Studio / Build Tools) lub **SQL Server Express**
- Dostęp do nuget.org (pakiet `DevExpress.Blazor` 26.1.x jest publicznie dostępny)
- Opcjonalnie: licencja DevExpress (trial lub pełna) — bez niej aplikacja działa, ale pokazuje baner „evaluation” (patrz niżej)

## Szybki start

```powershell
cd E:\ReposTest\Mando
dotnet build
dotnet test
dotnet run --project src\AnonymousSurvey.Web --launch-profile http
```

Aplikacja nasłuchuje na `http://localhost:5202` (profil `http`) lub `https://localhost:7069` (profil `https`).
Przy pierwszym uruchomieniu baza `AnonymousSurvey` na LocalDB jest tworzona automatycznie i wypełniana danymi seed.

## Adresy URL i tokeny z seeda

| Ścieżka | Opis |
|---------|------|
| `/` | Formularz ankiety (pytania ładowane z TVF) |
| `/dziekujemy` | Potwierdzenie wysłania |
| `/wyniki/{token}` | Wyniki: agregaty (średnie, rozkłady, Tak/Nie) + `DxGrid` ze wszystkimi odpowiedziami |
| `/konfiguracja/{token}` | Konfiguracja: wybór pytań z puli i ich kolejność |

Tokeny z `004_Seed.sql` (tajne adresy — to jedyna „autoryzacja” widoków administracyjnych):

| Widok | Token | Pełny adres (profil `http`) |
|-------|-------|------------------------------|
| Wyniki | `wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13` | `http://localhost:5202/wyniki/wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13` |
| Konfiguracja | `konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538` | `http://localhost:5202/konfiguracja/konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538` |

Błędny token daje komunikat „Nie znaleziono strony lub adres jest nieprawidłowy”. W środowisku produkcyjnym tokeny należy zmienić w bazie (tabela `Survey`).

Seed: jedna ankieta „Ankieta satysfakcji”, 5 pytań przypisanych (2× skala 1–5, 2× Tak/Nie, 1× otwarte) i 3 dodatkowe pytania w puli.

## Baza danych (LocalDB / Express)

W `appsettings.Development.json` jest connection string `SurveyDb` dla LocalDB:

`Server=(localdb)\mssqllocaldb;Database=AnonymousSurvey;...`

Dla SQL Express zmień serwer, np. na `.\SQLEXPRESS`.

Przy starcie aplikacji (`Database:InitializeOnStartup`, domyślnie `true`) `DatabaseInitializer` przez EF Core:

1. tworzy bazę, jeśli jej nie ma,
2. wykonuje idempotentne skrypty z `src/AnonymousSurvey.Infrastructure/Sql/` (osadzone jako zasoby):
   `001_Schema.sql` (tabele), `002_fn_GetSurveyQuestions.sql` (TVF), `003_usp_SaveSurveyResponse.sql` (SP), `004_Seed.sql` (ankieta, pula pytań, tokeny).

Skrypty można też uruchomić ręcznie w SSMS/`sqlcmd` w kolejności numerów. Aby wyłączyć automatyczną inicjalizację: `"Database": { "InitializeOnStartup": false }`.

### Pytania z TVF przez EF Core

- T-SQL: `dbo.fn_GetSurveyQuestions(@SurveyId)` zwraca `SurveyId, QuestionId, Text, Type, SortOrder` (tylko aktywne pytania przypisane do ankiety).
- EF Core: keyless typ `SurveyQuestionRow` + `modelBuilder.HasDbFunction(...)` na metodzie `SurveyDbContext.GetSurveyQuestions(int)` (`IQueryable<SurveyQuestionRow>`).
- `SurveyQuestionQuery` (`ISurveyQuestionQuery`) odczytuje pytania formularza **wyłącznie** przez tę funkcję — formularz i walidacja nie czytają pytań zwykłym LINQ po tabelach.

### Zapis odpowiedzi przez SP przez EF Core

- T-SQL: `dbo.usp_SaveSurveyResponse @SurveyId, @SessionId, @AnswersJson` — w jednej transakcji tworzy `ResponseSession` oraz wiersze `Answer` (JSON przez `OPENJSON`). Procedura sprawdza, że odpowiedzi pokrywają dokładnie aktualne pytania ankiety (z TVF); w przeciwnym razie wycofuje transakcję.
- EF Core: `SurveyResponseSaver` (`ISurveyResponseSaver`) wywołuje ją przez `Database.ExecuteSqlInterpolatedAsync` — bez `DbSet.Add` / `SaveChanges` na encjach `Answer`.
- Anonimowość: sesja = GUID + czas UTC; nie zapisujemy IP ani identyfikatorów użytkownika.

## Polimorfizm

- **Statyczny** (przeciążenia): `ISurveyResponseValidator.Validate(AnswerInputDto, SurveyQuestionDto)` — pojedyncza odpowiedź, oraz `Validate(IReadOnlyList<AnswerInputDto>, IReadOnlyList<SurveyQuestionDto>)` — cały formularz. Limit odpowiedzi otwartej: 2000 znaków.
- **Dynamiczny** (dziedziczenie / metody wirtualne):
  - `BasePage` (abstrakcyjna) — wspólny cykl ładowania, stany: ładowanie / błąd / brak strony; strony `SurveyFormPage`, `ThankYouPage`, `ResultsPage`, `ConfigPage` implementują `PageTitle` i `LoadAsync`.
  - `QuestionInputBase` — bazowy komponent pytania; `ScaleQuestionInput`, `YesNoQuestionInput`, `OpenQuestionInput` wybierane w `QuestionInput` przez `DynamicComponent` na podstawie typu pytania.

## DevExpress (Demo / trial)

Pakiet `DevExpress.Blazor` 26.1.5 pochodzi z nuget.org i jest zarejestrowany w `Program.cs` (`AddDevExpressBlazor`). Używane komponenty: `DxButton`, `DxRadioGroup`, `DxMemo`, `DxGrid`, motyw `office-white.bs5`.

**Ostrzeżenie DX1000 / DX1001 przy kompilacji jest oczekiwane** („For evaluation purposes only”) — to wersja Demo/trial bez zainstalowanej licencji. W działającej aplikacji pojawia się pomarańczowy baner evaluation u góry strony. Nie blokuje to działania.

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

## Testy

```powershell
dotnet test
```

- `AnonymousSurvey.UnitTests` — walidacja odpowiedzi (przeciążenia, zakres skali, limit tekstu, komplet odpowiedzi).
- `AnonymousSurvey.IntegrationTests` — tymczasowa baza na LocalDB (`(localdb)\mssqllocaldb`), usuwana po testach: TVF, SP (w tym rollback), wyniki, konfiguracja.

## Smoke test (ręczny)

1. Uruchom aplikację, otwórz `/wyniki/{token}` — zanotuj liczbę wypełnionych ankiet.
2. Na `/` kliknij „Wyślij odpowiedzi” bez wypełnienia — każde pytanie dostaje komunikat o błędzie.
3. Wypełnij wszystkie pytania i wyślij — przekierowanie na `/dziekujemy`.
4. Odśwież `/wyniki/{token}` — licznik +1, agregaty i wiersze w siatce zawierają nową odpowiedź.
5. Na `/konfiguracja/{token}` usuń pytanie (lub dodaj z puli) i zapisz — formularz `/` pokazuje zmieniony zestaw pytań.

## Struktura

```
AnonymousSurvey.sln
  src/AnonymousSurvey.Domain          # encje, enum QuestionType
  src/AnonymousSurvey.Application     # DTO, interfejsy, walidator, serwis zapisu
  src/AnonymousSurvey.Infrastructure  # EF Core, TVF/SP, skrypty SQL, repozytoria
  src/AnonymousSurvey.Web             # Blazor Server + DevExpress
  tests/AnonymousSurvey.UnitTests
  tests/AnonymousSurvey.IntegrationTests
  _local/                             # poza gitem — specs i notatki lokalne
```

## Specyfikacja lokalna

Kopia designu i planu implementacji: `_local\specs\` (nie trafia do repozytorium).
