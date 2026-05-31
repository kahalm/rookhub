import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  timeout: 60_000,
  expect: { timeout: 5_000 },
  fullyParallel: false,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: 'html',

  use: {
    baseURL: 'http://localhost:8085',
    headless: true,
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    trace: 'retain-on-failure',
  },

  projects: [
    {
      name: 'no-auth',
      testMatch: ['puzzles.spec.ts', 'puzzle-moves.spec.ts', 'dashboard.spec.ts', 'viz-mobile.spec.ts'],
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
