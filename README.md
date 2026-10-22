# AnonymousSurvey — ankieta anonimowa

Projekt rekrutacyjny: Blazor Server (.NET 10), Clean Architecture, SQL Server (LocalDB / Express), DevExpress Blazor (Demo/trial).

## Wymagania

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server **LocalDB** (z Visual Studio / Build Tools) lub **SQL Server Express**
- (Faza 4+) klucz licencyjny DevExpress Blazor Demo/trial

## Uruchomienie (scaffold — faza 1)

```powershell
cd E:\ReposTest\Mando
dotnet build
dotnet run --project src\AnonymousSurvey.Web
```

Aplikacja nasłuchuje domyślnie na `https://localhost:7xxx` (port w konsoli przy starcie).

## Baza danych (LocalDB / Express)

W `appsettings.Development.json` jest placeholder connection stringa `SurveyDb` dla LocalDB:

`Server=(localdb)\mssqllocaldb;Database=AnonymousSurvey;...`

Dla SQL Express zmień serwer np. na `.\SQLEXPRESS`.

Przy starcie aplikacji (`Database:InitializeOnStartup`, domyślnie `true`) `DatabaseInitializer` przez EF Core:

1. tworzy bazę, jeśli jej nie ma,
2. wykonuje idempotentne skrypty z `src/AnonymousSurvey.Infrastructure/Sql/` (osadzone jako zasoby):
   `001_Schema.sql` (tabele), `002_fn_GetSurveyQuestions.sql` (TVF), `003_usp_SaveSurveyResponse.sql` (SP), `004_Seed.sql` (jedna ankieta, 8 pytań w puli, tokeny).

Skrypty można też uruchomić ręcznie w SSMS/`sqlcmd` w kolejności numerów.

### Pytania z TVF przez EF Core

- T-SQL: `dbo.fn_GetSurveyQuestions(@SurveyId)` zwraca `SurveyId, QuestionId, Text, Type, SortOrder` (tylko aktywne pytania przypisane do ankiety).
- EF Core: keyless typ `SurveyQuestionRow` + `modelBuilder.HasDbFunction(...)` na metodzie `SurveyDbContext.GetSurveyQuestions(int)` (`IQueryable<SurveyQuestionRow>`).
- `SurveyQuestionQuery` (`ISurveyQuestionQuery`) odczytuje pytania formularza **wyłącznie** przez tę funkcję — formularz i walidacja nie czytają pytań zwykłym LINQ po tabelach.

### Zapis odpowiedzi przez SP przez EF Core

- T-SQL: `dbo.usp_SaveSurveyResponse @SurveyId, @SessionId, @AnswersJson` — w jednej transakcji tworzy `ResponseSession` oraz wiersze `Answer` (JSON przez `OPENJSON`). Procedura sprawdza, że odpowiedzi pokrywają dokładnie aktualne pytania ankiety (z TVF); w przeciwnym razie wycofuje transakcję.
- EF Core: `SurveyResponseSaver` (`ISurveyResponseSaver`) wywołuje ją przez `Database.ExecuteSqlInterpolatedAsync` — bez `DbSet.Add` / `SaveChanges` na encjach `Answer`.
- Anonimowość: sesja = GUID + czas UTC; nie zapisujemy IP ani identyfikatorów użytkownika.

### Testy

```powershell
dotnet test
```

`AnonymousSurvey.IntegrationTests` tworzą tymczasową bazę na LocalDB (`(localdb)\mssqllocaldb`) i usuwają ją po testach.

## DevExpress (User Secrets)

Klucz trial **nie** commitujemy. Po dodaniu pakietów DevExpress (faza 4) ustaw np.:

```powershell
cd src\AnonymousSurvey.Web
dotnet user-secrets init
dotnet user-secrets set "DevExpress:LicenseKey" "TWÓJ_KLUCZ"
```

Alternatywnie: `appsettings.Development.json` (lokalnie, poza gitem jeśli zawiera klucz).

## Adresy URL (po implementacji UI)

| Ścieżka | Opis |
|--------|------|
| `/` | Formularz ankiety |
| `/dziekujemy` | Potwierdzenie wysłania |
| `/wyniki/{token}` | Wyniki (agregaty + lista) — token z seeda |
| `/konfiguracja/{token}` | Konfiguracja pytań — token z seeda |

Tokeny docelowe będą w README po **fazie 5** (seed). Na razie: `wyniki-przyklad`, `konfiguracja-przyklad` (placeholdery).

## Struktura

```
AnonymousSurvey.sln
  src/AnonymousSurvey.Domain
  src/AnonymousSurvey.Application
  src/AnonymousSurvey.Infrastructure
  src/AnonymousSurvey.Web
  _local/          # poza gitem — specs i notatki lokalne
```

## Specyfikacja lokalna

Kopia designu i planu implementacji: `_local\specs\` (nie trafia do repozytorium).
