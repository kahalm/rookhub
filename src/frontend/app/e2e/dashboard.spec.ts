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

  test('shows dashboard cards (Repertoires, Tournaments, Friends, Puzzles)', async ({ page }) => {
    await loginPage(page);
    await expect(page.locator('mat-card').first()).toBeVisible({ timeout: 10_000 });

    const pageText = await page.locator('body').textContent();
    expect(pageText).toMatch(/Repertoires/i);
    expect(pageText).toMatch(/Friends/i);
    expect(pageText).toMatch(/Puzzles/i);
  });

  test('navigation to /puzzles works', async ({ page }) => {
    await loginPage(page);

    await page.getByRole('button', { name: /Solve Puzzles/i }).click();
    await page.waitForURL('**/puzzles', { timeout: 10_000 });
    await expect(page).toHaveURL(/\/puzzles/);
  });

  test('navigation to /friends works', async ({ page }) => {
    await loginPage(page);

    await page.getByRole('button', { name: /Manage Friends/i }).click();
    await page.waitForURL('**/friends', { timeout: 10_000 });
    await expect(page).toHaveURL(/\/friends/);
  });
});
