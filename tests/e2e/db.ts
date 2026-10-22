import { execFileSync } from 'node:child_process';

const SERVER = '(localdb)\\mssqllocaldb';
const SEPARATOR = '|';

/** Nazwa bazy E2E ustawiana przez scripts/run-e2e.ps1 (AnonymousSurvey_E2E_<guid>). */
export function e2eDatabase(): string {
  const name = process.env.E2E_DB;
  if (!name) {
    throw new Error('Brak E2E_DB — uruchom testy przez scripts/run-e2e.ps1 (ustawia też BASE_URL).');
  }
  return name;
}

/** Wykonuje zapytanie przez sqlcmd i zwraca wiersze jako tablice kolumn (rozdzielone znakiem „|”). */
export function rows(query: string, database: string = e2eDatabase()): string[][] {
  const output = execFileSync(
    'sqlcmd',
    ['-S', SERVER, '-d', database, '-b', '-h', '-1', '-W', '-f', '65001', '-s', SEPARATOR, '-Q', `SET NOCOUNT ON; ${query}`],
    { encoding: 'utf8', windowsHide: true },
  );
  return output
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line) => line.split(SEPARATOR).map((cell) => cell.trim()));
}

/** Pierwsza komórka pierwszego wiersza jako liczba (np. `SELECT COUNT(*) ...`). */
export function scalar(query: string, database: string = e2eDatabase()): number {
  const result = rows(query, database);
  if (result.length === 0) {
    throw new Error(`Zapytanie nie zwróciło wierszy: ${query}`);
  }
  return Number(result[0][0]);
}

/** Liczba wierszy tabeli (opcjonalnie z warunkiem WHERE napisanym w teście). */
export function countRows(table: string, where?: string, database: string = e2eDatabase()): number {
  if (!/^[A-Za-z_][A-Za-z0-9_.]*$/.test(table)) {
    throw new Error(`Niedozwolona nazwa tabeli: ${table}`);
  }
  return scalar(`SELECT COUNT(*) FROM ${table}${where ? ` WHERE ${where}` : ''}`, database);
}
