import { test as base, expect } from '@playwright/test';
import { PageGuards } from './guards';

type Options = {
  /** Wzorce problemów, które dany test świadomie dopuszcza (np. oczekiwany 404 dla zasobu). Domyślnie brak. */
  allowedProblems: RegExp[];
};

type Fixtures = {
  guards: PageGuards;
};

/**
 * `test` z automatyczną fixture `guards`: PO każdym teście failuje, jeśli wystąpił
 * console.error (poza DX1000/DevExpress trial), requestfailed, odpowiedź >= 500 z aplikacji
 * albo widoczny #blazor-error-ui. Wszystkie specyfikacje importują `test` stąd, nie z @playwright/test.
 */
export const test = base.extend<Options & Fixtures>({
  allowedProblems: [[], { option: true }],

  guards: [
    async ({ page, baseURL, allowedProblems }, use) => {
      const guards = new PageGuards(page, new URL(baseURL!).origin, allowedProblems);
      await use(guards);
      await guards.assertClean();
    },
    { auto: true },
  ],
});

export { expect };
