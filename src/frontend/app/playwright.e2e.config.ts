/**
 * Playwright config for the isolated E2E test stack (compose.e2e.yml).
 * Ports: Frontend 8086, API 5002 — no collision with dev stack.
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
    baseURL: 'http://localhost:8086',
    headless: true,
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    trace: 'retain-on-failure',
  },

  projects: [
    {
      name: 'no-auth',
      testMatch: ['puzzles.spec.ts', 'puzzle-moves.spec.ts', 'dashboard.spec.ts'],
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
