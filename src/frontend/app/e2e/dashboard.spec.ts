import { test, expect, Page } from '@playwright/test';
import fs from 'fs';
import path from 'path';

const STATE_FILE = path.join(__dirname, '.auth-state.json');

function loadSharedAuth() {
  return JSON.parse(fs.readFileSync(STATE_FILE, 'utf-8'));
}

async function loginPage(page: Page) {
  const { auth } = loadSharedAuth();
  await page.addInitScript((authData) => {
    localStorage.setItem('rookhub_user', JSON.stringify(authData));
  }, auth);
  await page.goto('/dashboard');
  await page.waitForURL('**/dashboard', { timeout: 15_000 });
}

test.describe('Dashboard', () => {
  test('shows welcome message', async ({ page }) => {
    const { username } = loadSharedAuth();
    await loginPage(page);
    await expect(page.locator('h1')).toContainText(`Welcome, ${username}`, { timeout: 10_000 });
  });

  test('shows the default training tiles (Puzzles, Guess the moves, Training Goals)', async ({ page }) => {
    await loginPage(page);

    // Kuratierter Standard (DEFAULT_VISIBLE im DashboardComponent): sichtbar ist nur der
    // Trainings-Kern; Repertoires, Freunde und Bestenlisten schaltet man ueber „Anpassen" zu.
    const titles = page.locator('mat-card-title');
    await expect(titles.filter({ hasText: /^Puzzles$/ })).toBeVisible({ timeout: 10_000 });
    await expect(titles.filter({ hasText: /^Guess the moves$/ })).toBeVisible();
    await expect(titles.filter({ hasText: /^Training Goals$/ })).toBeVisible();
  });

  test('navigation to /puzzles works', async ({ page }) => {
    await loginPage(page);

    await page.getByRole('button', { name: /Solve Puzzles/i }).click();
    await page.waitForURL('**/puzzles', { timeout: 10_000 });
    await expect(page).toHaveURL(/\/puzzles/);
  });

  test('navigation to /training-goals works', async ({ page }) => {
    await loginPage(page);

    await page.getByRole('button', { name: /Open goals/i }).click();
    await page.waitForURL('**/training-goals', { timeout: 10_000 });
    await expect(page).toHaveURL(/\/training-goals/);
  });
});
