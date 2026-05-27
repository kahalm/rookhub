import { test, expect } from './fixtures/auth.fixture';

test.describe('Dashboard', () => {
  test('shows welcome message', async ({ authedPage, testUser }) => {
    await authedPage.goto('/dashboard');
    await authedPage.waitForURL('**/dashboard', { timeout: 10_000 });

    await expect(authedPage.locator('h1')).toContainText(`Welcome, ${testUser.username}`, { timeout: 10_000 });
  });

  test('shows dashboard cards (Repertoires, Tournaments, Friends, Puzzles)', async ({ authedPage }) => {
    await authedPage.goto('/dashboard');
    await authedPage.waitForURL('**/dashboard', { timeout: 10_000 });

    // Dashboard has mat-cards with icons
    const cards = authedPage.locator('.dashboard-card, mat-card');
    await expect(cards.first()).toBeVisible({ timeout: 10_000 });

    // Check for the key icons or card text
    const pageText = await authedPage.locator('body').textContent();
    expect(pageText).toMatch(/repertoire/i);
    expect(pageText).toMatch(/friend/i);
    expect(pageText).toMatch(/puzzle/i);
  });

  test('navigation to /puzzles works', async ({ authedPage }) => {
    await authedPage.goto('/dashboard');
    await authedPage.waitForURL('**/dashboard', { timeout: 10_000 });

    // Click puzzles link in navbar or dashboard card
    const puzzleLink = authedPage.locator('a[href="/puzzles"], a[routerLink="/puzzles"]').first();
    if (await puzzleLink.isVisible()) {
      await puzzleLink.click();
    } else {
      // Try navbar text link
      await authedPage.getByRole('link', { name: /puzzle/i }).first().click();
    }

    await authedPage.waitForURL('**/puzzles', { timeout: 10_000 });
    await expect(authedPage).toHaveURL(/\/puzzles/);
  });

  test('navigation to /friends works', async ({ authedPage }) => {
    await authedPage.goto('/dashboard');
    await authedPage.waitForURL('**/dashboard', { timeout: 10_000 });

    const friendsLink = authedPage.locator('a[href="/friends"], a[routerLink="/friends"]').first();
    if (await friendsLink.isVisible()) {
      await friendsLink.click();
    } else {
      await authedPage.getByRole('link', { name: /friend/i }).first().click();
    }

    await authedPage.waitForURL('**/friends', { timeout: 10_000 });
    await expect(authedPage).toHaveURL(/\/friends/);
  });
});
