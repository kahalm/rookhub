/**
 * Playwright config for the isolated E2E test stack (compose.e2e.yml).
 * Default ports come from .env.e2e (frontend 8086, API 5002). scripts/e2e.sh passes
 * overridden ports on as E2E_BASE_URL / E2E_API_URL, e.g. on a host where the dev or prod
 * stack already holds them:  API_PORT=15099 FRONTEND_PORT=18099 bash scripts/e2e.sh
 */
import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.e2e.ts',
  timeout: 60_000,
  expect: { timeout: 5_000 },
  fullyParallel: false,
  retries: 1,
  workers: 1,
  reporter: 'html',

  use: {
    baseURL: process.env.E2E_BASE_URL || 'http://localhost:8086',
    headless: true,
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    trace: 'retain-on-failure',
  },

  projects: [
    {
      name: 'no-auth',
      testMatch: ['puzzles.spec.ts', 'puzzle-moves.spec.ts', 'dashboard.spec.ts', 'viz-mobile.spec.ts', 'offline.spec.ts'],
      use: { browserName: 'chromium' },
    },
    {
      name: 'auth',
      testMatch: ['auth.spec.ts'],
      dependencies: ['no-auth'],
      use: { browserName: 'chromium' },
    },
  ],
});
