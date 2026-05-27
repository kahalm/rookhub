import { test as base, expect } from '@playwright/test';

// Puzzles are public (no auth needed)
const test = base;

test.describe('Puzzles', () => {
  test('puzzle page loads without auth', async ({ page }) => {
    await page.goto('/puzzles');
    // Should not redirect to /login
    await expect(page).toHaveURL(/\/puzzles/, { timeout: 10_000 });
  });

  test('puzzle board is displayed', async ({ page }) => {
    await page.goto('/puzzles');

    // Board rendered by app-puzzle-board / chessground
    const board = page.locator('app-puzzle-board, cg-board, .cg-wrap');
    await expect(board.first()).toBeVisible({ timeout: 15_000 });
  });

  test('endless mode config screen is displayed', async ({ page }) => {
    await page.goto('/puzzles/endless');

    // Config screen with "Endless Puzzle Mode" title
    await expect(page.locator('.config-screen, .config-card')).toBeVisible({ timeout: 10_000 });
    await expect(page.locator('body')).toContainText('Endless Puzzle Mode');
  });

  test('endless mode starts after clicking start button', async ({ page }) => {
    await page.goto('/puzzles/endless');

    // Wait for config screen
    await expect(page.locator('.config-screen, .config-card')).toBeVisible({ timeout: 10_000 });

    // Click start button
    const startBtn = page.getByRole('button', { name: /start/i });
    await expect(startBtn).toBeVisible({ timeout: 5_000 });
    await startBtn.click();

    // After start, game screen should show (board visible, config gone)
    const board = page.locator('app-puzzle-board, cg-board, .cg-wrap');
    await expect(board.first()).toBeVisible({ timeout: 15_000 });
  });
});
