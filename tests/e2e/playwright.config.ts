import { defineConfig, devices } from '@playwright/test';

// scripts/run-e2e.ps1 ustawia BASE_URL (port z zakresu 5300-5399) oraz E2E_DB (baza AnonymousSurvey_E2E_<guid>).
const baseURL = process.env.BASE_URL ?? 'http://localhost:5202';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    locale: 'pl-PL',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'edge',
      use: { ...devices['Desktop Edge'], channel: 'msedge' },
    },
  ],
});
