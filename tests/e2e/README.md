# Testy E2E (Playwright, Edge)

Przeglądarkowe testy aplikacji ankiet na `@playwright/test`. Przeglądarka: zainstalowany **Microsoft Edge** (`channel: 'msedge'`) — nie pobieramy żadnych przeglądarek Playwrighta.

## Uruchamianie

Zawsze przez skrypt, który przygotowuje całe środowisko:

```powershell
powershell -File scripts\run-e2e.ps1                      # wszystkie testy
powershell -File scripts\run-e2e.ps1 -Filter "smoke"      # tylko testy z "smoke" w tytule (--grep)
powershell -File scripts\run-e2e.ps1 -Port 5211           # własny port (domyślnie wolny z 5300-5399)
powershell -File scripts\verify.ps1 -E2E                  # build + testy .NET + E2E
```

`run-e2e.ps1`: wybiera wolny port, tworzy bazę `AnonymousSurvey_E2E_<guid>` na LocalDB, startuje aplikację
(`dotnet run --no-launch-profile --urls http://localhost:<port>`, `ConnectionStrings__SurveyDb` wskazuje tę bazę),
czeka na `GET /`, ustawia `BASE_URL` i `E2E_DB`, uruchamia `npx playwright test`, a w `finally` zabija proces aplikacji
i usuwa bazę. Kod wyjścia skryptu = kod testów. Przy błędzie logi aplikacji zostają w `%TEMP%\anonymous-survey-e2e-<port>`.

Node.js: jeśli `node` nie ma w `PATH`, skrypty dodają katalog z WinGet (`OpenJS.NodeJS.LTS ... node-v24.19.0-win-x64`).
Zależności npm instalują się same (`npm ci`) przy pierwszym uruchomieniu.

Ręcznie (aplikacja już działa na własnej bazie): `$env:BASE_URL='http://localhost:5211'; $env:E2E_DB='<nazwa bazy>'; npx playwright test`.

## Struktura

| Plik | Rola |
|------|------|
| `playwright.config.ts` | projekt `edge`, `workers: 1`, `fullyParallel: false`, `baseURL` z `BASE_URL` |
| `fixtures.ts` | `test` z automatyczną fixture `guards` (globalne asercje) — **importuj `test`/`expect` stąd** |
| `guards.ts` | klasa `PageGuards` (zbieranie problemów strony) i lista znanego szumu (DX1000/DevExpress trial, 404 `favicon.ico`) |
| `db.ts` | `rows`, `scalar`, `countRows` przez `sqlcmd -S (localdb)\mssqllocaldb -d $E2E_DB` do asercji na bazie |
| `tests/smoke.spec.ts` | smoke seedowej ankiety (jedyne miejsce z seedem; teraz `test.fixme` — patrz niżej) oraz `test.fixme` tras ze strumieni D/E |
| `tests/guards.spec.ts` | dowód, że globalne asercje naprawdę failują (patrz niżej) |

## Globalne asercje (po każdym teście)

Auto-fixture `guards` failuje test, jeśli w jego trakcie:

- pojawił się `console.error` (poza bannerem DX1000 / komunikatami DevExpress trial i 404 `favicon.ico`) lub nieobsłużony wyjątek strony,
- jakiś request się nie udał (`requestfailed`; `net::ERR_ABORTED` przy nawigacji jest pomijany),
- aplikacja odpowiedziała statusem >= 500,
- na końcu widoczny jest `#blazor-error-ui`.

Świadomie oczekiwany problem (np. test ścieżki 404) dopuszczasz opcją: `test.use({ allowedProblems: [/Failed to load resource.*404/] })`.

**Dowód działania:** `tests/guards.spec.ts` testuje klasę `PageGuards` (każdy rodzaj problemu → `assertClean()` rzuca z właściwym komunikatem; szum i obcy origin nie) oraz auto-fixture z `fixtures.ts` przez `test.fail()` — test, który celowo robi `console.error` / pokazuje `#blazor-error-ui` / dostaje 500, musi się wywrócić, inaczej `test.fail()` zgłosi błąd. Strony próbne serwuje `page.route`, więc te testy nie wymagają działającej aplikacji.

**Po merge A / po merge C:** scenariusze seedowe są `test.fixme`, dopóki `SEED_SMOKE_PENDING_MERGE_C` w `smoke.spec.ts` jest `true`. Formularz na tej gałęzi nie ma danych: schemat SQL nie ma jeszcze kolumn modelu EF (po merge A, m.in. `Survey.Slug`, `IsRequired`), a `ISurveyCatalogQuery` rzuca `NotImplementedException` (po merge C). Wymuszenie przebiegu: `$env:E2E_SEED_SMOKE='1'`. Helper `CreateSurveyAsync` w testach integracyjnych jest pod `NewSchemaFact` i sam się uruchamia po merge A, gdy skrypty SQL zawierają `Slug`. Trasy `/ankieta/{slug}`, `/nowa-ankieta` i podziękowanie ze slugiem zostają `test.fixme` do strumieni D/E.

## Jak dodać test

1. Plik `tests/<obszar>.spec.ts`, `import { test, expect } from '../fixtures';`.
2. **Niezależność:** test sam tworzy potrzebne dane (np. przez `/nowa-ankieta` albo SQL w `db.ts`) z unikalnym slugiem (`Date.now()`/guid) i nie zakłada kolejności ani stanu po innych testach. Seed wolno wykorzystywać wyłącznie w `smoke.spec.ts`.
3. Asercje stanu bazy: `countRows('ResponseSession', 'SurveyId = 3')`, `scalar(...)`, `rows(...)` (baza E2E jest jednorazowa, więc można zmieniać dane).
4. Selektory: preferuj role/etykiety (`getByRole`, `getByLabel`) i klasy już używane w aplikacji (`.question-card`, `.state-box`); czekaj przez `expect(...).toBeVisible()`, nie `waitForTimeout`.
5. Test z oczekiwanym `console.error`/404 → `allowedProblems`; test czekający na trasę, której jeszcze nie ma → `test.fixme(...)` z komentarzem, który strumień ją dodaje.

## Debug

```powershell
cd tests\e2e
npx playwright show-report                                   # raport HTML po przebiegu
npx playwright show-trace test-results\<test>\trace.zip      # trace (zapisywany przy porażce)
```

Wolniejszy podgląd: `$env:PWDEBUG=1` albo `npx playwright test --headed --debug` (aplikacja i baza muszą działać — użyj ręcznego trybu z sekcji „Uruchamianie”).
Katalogi `node_modules`, `test-results`, `playwright-report` są w `.gitignore`.
