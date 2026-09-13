import { test as base, expect } from '@playwright/test';
import { seedSolveMode, statusCard } from './fixtures/solver';

// Puzzles are public (no auth needed)
const test = base;

test.describe('Puzzles', () => {
  // Ohne gemerkte Spielweise legt sich beim ersten Einstieg ein modaler Dialog uebers Brett.
  test.beforeEach(async ({ page }) => {
    await seedSolveMode(page, 'easy');
  });

  test('puzzle page loads without auth', async ({ page }) => {
    await page.goto('/puzzles');
    // Should not redirect to /login
    await expect(page).toHaveURL(/\/puzzles/, { timeout: 10_000 });
  });

  test('puzzle board is displayed', async ({ page }) => {
    await page.goto('/puzzles');

    // Board rendered by app-puzzle-board / chessground
    const board = page.locator('app-puzzle-board, cg-board, .cg-wrap').first();
    await expect(board).toBeVisible({ timeout: 15_000 });
  });

  test('endless mode config screen is displayed', async ({ page }) => {
    await page.goto('/puzzles/endless');

    // Config screen with "Endless Puzzle Mode" title
    await expect(page.locator('.config-screen')).toBeVisible({ timeout: 10_000 });
    await expect(page.locator('body')).toContainText('Endless Puzzle Mode');
  });

  test('puzzle shows eval and give-up buttons after loading', async ({ page }) => {
    await page.goto('/puzzles');

    const board = page.locator('app-puzzle-board, cg-board, .cg-wrap').first();
    await expect(board).toBeVisible({ timeout: 15_000 });

    // Reset und Mouseslip kommen erst nach dem ersten Zug dazu.
    const card = statusCard(page);
    await expect(card.getByRole('button', { name: /Show Eval/ })).toBeVisible({ timeout: 15_000 });
    await expect(card.getByRole('button', { name: /Give Up/ })).toBeVisible();
  });

  test('endless mode starts after clicking start button', async ({ page }) => {
    await page.goto('/puzzles/endless');

    // Wait for config screen
    await expect(page.locator('.config-screen')).toBeVisible({ timeout: 10_000 });

    // Click start button
    const startBtn = page.getByRole('button', { name: /start/i });
    await expect(startBtn).toBeVisible({ timeout: 5_000 });
    await startBtn.click();

    // After start, game screen should show (board visible, config gone)
    const board = page.locator('app-puzzle-board, cg-board, .cg-wrap').first();
    await expect(board).toBeVisible({ timeout: 15_000 });
  });
});
