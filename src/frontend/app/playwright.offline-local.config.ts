/**
 * Standalone-Config NUR für e2e/offline.spec.ts — ohne globalSetup/API-Stack.
 * Läuft gegen einen beliebigen servierten Prod-Build (Service Worker nötig!), z. B.:
 *   docker run --rm -v $PWD/../nginx.conf:/etc/nginx/conf.d/default.conf:ro \
 *     -v $PWD/dist/app/browser:/usr/share/nginx/html:ro -p 127.0.0.1:18099:8080 nginx:alpine
 *   E2E_OFFLINE_BASE=http://127.0.0.1:18099 npx playwright test --config=playwright.offline-local.config.ts
 * Im normalen E2E-Stack-Lauf (scripts/e2e.sh) läuft offline.spec.ts stattdessen über die
 * regulären Configs (no-auth-Projekt) gegen das Prod-Frontend-Image.
 */
import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  timeout: 180_000,
  workers: 1,
  reporter: 'list',
  use: { headless: true, screenshot: 'only-on-failure' },
  projects: [{ name: 'offline', testMatch: ['offline.spec.ts'], use: { browserName: 'chromium' } }],
});
