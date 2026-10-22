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

Dla SQL Express zmień serwer np. na `.\SQLEXPRESS`. Migracje i seed pojawią się w **fazie 3**.

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
