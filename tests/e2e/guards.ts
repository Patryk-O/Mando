import type { Page } from '@playwright/test';

/**
 * Znane, ignorowane komunikaty: baner/ostrzeżenie DevExpress (wersja trial, DX1000) oraz 404 dla /favicon.ico
 * (przeglądarka żąda go sama, a aplikacja nie ma favicony). Wszystko inne w console.error jest błędem testu.
 */
export const KNOWN_NOISE: RegExp[] = [
  /DX1000/i,
  /devexpress\.com\/(DX1000|BUY)/i,
  /DevExpress.*(trial|evaluation)/i,
  /Failed to load resource.*\[[^\]]*\/favicon\.ico\]/,
];

/**
 * Zbiera problemy strony, które mają wywalić KAŻDY test: console.error, nieobsłużone wyjątki,
 * nieudane requesty, odpowiedzi >= 500 z aplikacji i widoczny #blazor-error-ui.
 */
export class PageGuards {
  readonly problems: string[] = [];

  constructor(
    private readonly page: Page,
    private readonly appOrigin: string,
    private readonly allowed: RegExp[] = [],
  ) {
    page.on('console', (message) => {
      if (message.type() === 'error') {
        const url = message.location().url;
        this.record(`console.error: ${message.text()}${url ? ` [${url}]` : ''}`);
      }
    });
    page.on('pageerror', (error) => this.record(`nieobsłużony wyjątek strony: ${error.message}`));
    page.on('requestfailed', (request) => {
      const reason = request.failure()?.errorText ?? 'nieznany powód';
      // Przerwane żądania (nawigacja, zamknięcie strony w trakcie ładowania) nie są błędami aplikacji.
      if (reason !== 'net::ERR_ABORTED') {
        this.record(`requestfailed: ${request.method()} ${request.url()} (${reason})`);
      }
    });
    page.on('response', (response) => {
      if (response.status() >= 500 && response.url().startsWith(this.appOrigin)) {
        this.record(`odpowiedź ${response.status()}: ${response.request().method()} ${response.url()}`);
      }
    });
  }

  private record(message: string): void {
    if (KNOWN_NOISE.some((noise) => noise.test(message))) return;
    if (this.allowed.some((allowedPattern) => allowedPattern.test(message))) return;
    this.problems.push(message);
  }

  /** Rzuca, jeśli w trakcie testu wystąpił problem lub widoczny jest #blazor-error-ui. */
  async assertClean(): Promise<void> {
    const collected = [...this.problems];
    if (!this.page.isClosed()) {
      const errorUiVisible = await this.page.locator('#blazor-error-ui').isVisible().catch(() => false);
      if (errorUiVisible) {
        collected.push('widoczny #blazor-error-ui (nieobsłużony błąd Blazora)');
      }
    }
    if (collected.length > 0) {
      throw new Error(`Globalne asercje E2E wykryły problemy:\n - ${collected.join('\n - ')}`);
    }
  }
}
