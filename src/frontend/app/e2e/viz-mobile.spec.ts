import { test as base, expect, Page, Locator } from '@playwright/test';

const test = base;

// iPhone 12 viewport
const MOBILE_VIEWPORT = { width: 390, height: 844 };

// Simple 2-move puzzle (same as puzzle-moves.spec.ts)
const PUZZLE_FEN = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1';
const TWO_MOVE_PUZZLE = {
  id: 99010, lichessId: 'e2eVizMob',
  fen: PUZZLE_FEN, moves: 'e7e5 d1h5', rating: 1000, themes: 'test',
};

async function mockPuzzleApi(page: Page, puzzle: object) {
  await page.route('**/api/puzzles/random**', r =>
    r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(puzzle) }));
  await page.route('**/api/puzzles/*/attempt**', r => r.fulfill({ status: 401 }));
  await page.route('**/api/puzzles/stats**', r => r.fulfill({ status: 401 }));
}

function squareCenter(boardWidth: number, square: string, orientation: 'white' | 'black' = 'white') {
  const sq = boardWidth / 8;
  const file = square.charCodeAt(0) - 97;
  const rank = parseInt(square[1]) - 1;
  if (orientation === 'white') {
    return { x: (file + 0.5) * sq, y: (7 - rank + 0.5) * sq };
  }
  return { x: (7 - file + 0.5) * sq, y: (rank + 0.5) * sq };
}

async function clickSquare(page: Page, board: Locator, square: string, orientation: 'white' | 'black' = 'white') {
  const box = await board.boundingBox();
  if (!box) throw new Error('Board bounding box not available');
  const pos = squareCenter(box.width, square, orientation);
  await page.mouse.click(box.x + pos.x, box.y + pos.y);
}

async function makeMove(page: Page, board: Locator, from: string, to: string, orientation: 'white' | 'black' = 'white') {
  await clickSquare(page, board, from, orientation);
  await page.waitForTimeout(150);
  await clickSquare(page, board, to, orientation);
}

test.describe('Visualization Mobile', () => {
  test.use({ viewport: MOBILE_VIEWPORT });

  test('viz slider changes level and persists in localStorage', async ({ page }) => {
    await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
    await page.goto('/puzzles');

    const board = page.locator('cg-board');
    await expect(board).toBeVisible({ timeout: 15_000 });

    // Open settings gear
    await page.locator('.settings-gear').click();

    // Slider should be visible
    const slider = page.locator('.viz-slider input[type="range"]');
    await expect(slider).toBeVisible({ timeout: 5_000 });

    // Default level should be 1 (from preferences default)
    await expect(slider).toHaveValue('1');

    // Change to level 0 (Normal)
    await slider.fill('0');
    await expect(page.locator('.viz-level-desc')).toContainText('Normal');

    // Change to level 2 (Checker)
    await slider.fill('2');
    await expect(page.locator('.viz-level-desc')).toContainText('Checker');

    // Verify localStorage persisted
    const vizValue = await page.evaluate(() => localStorage.getItem('rookhub_visualization'));
    expect(vizValue).toBe('2');
  });

  test('viz-card appears directly below board on mobile (not in info-section)', async ({ page }) => {
    await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
    // Set visualization level to 1 (Blindfold)
    await page.addInitScript(() => {
      localStorage.setItem('rookhub_visualization', '1');
    });
    await page.goto('/puzzles');

    const board = page.locator('cg-board');
    await expect(board).toBeVisible({ timeout: 15_000 });
    // Wait for puzzle to start (AWAITING_USER_MOVE)
    await expect(page.locator('.status-text')).toContainText('Your turn', { timeout: 10_000 });

    // viz-card is in info-section (first child), which on mobile stacks below the board
    const vizCard = page.locator('.info-section .viz-card');
    await expect(vizCard).toBeVisible({ timeout: 5_000 });

    // On mobile (single column), viz-card should be below the board
    const boardBox = await board.boundingBox();
    const vizCardBox = await vizCard.boundingBox();
    expect(boardBox).toBeTruthy();
    expect(vizCardBox).toBeTruthy();
    expect(vizCardBox!.y).toBeGreaterThan(boardBox!.y + boardBox!.height - 5);
  });

  test('level 2 countdown + viz-hidden class + show button', async ({ page }) => {
    await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
    await page.addInitScript(() => {
      localStorage.setItem('rookhub_visualization', '2');
    });
    await page.goto('/puzzles');

    const board = page.locator('cg-board');
    await expect(board).toBeVisible({ timeout: 15_000 });
    // Wait for AWAITING_USER_MOVE (beginSolving triggers countdown)
    await expect(page.locator('.status-text')).toContainText('Your turn', { timeout: 10_000 });

    // Countdown should be visible
    const countdown = page.locator('.viz-countdown');
    await expect(countdown).toBeVisible({ timeout: 3_000 });
    await expect(countdown).toContainText('Figuren verschwinden in');

    // Wait for countdown to finish (3s + buffer)
    await expect(countdown).not.toBeVisible({ timeout: 6_000 });

    // board-section should have viz-hidden class
    const boardSection = page.locator('.board-section');
    await expect(boardSection).toHaveClass(/viz-hidden/, { timeout: 2_000 });

    // Show button should appear
    const showBtn = page.locator('.viz-show-btn');
    await expect(showBtn).toBeVisible({ timeout: 2_000 });
  });

  test('level 4 invisible: pieces hidden after countdown', async ({ page }) => {
    await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
    await page.addInitScript(() => {
      localStorage.setItem('rookhub_visualization', '4');
    });
    await page.goto('/puzzles');

    const board = page.locator('cg-board');
    await expect(board).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('.status-text')).toContainText('Your turn', { timeout: 10_000 });

    // Wait for countdown to finish (3s + buffer)
    await expect(page.locator('.viz-countdown')).not.toBeVisible({ timeout: 6_000 });

    // viz-hide-css style element should exist with opacity: 0
    const vizCss = await page.evaluate(() => {
      const el = document.getElementById('viz-hide-css');
      return el ? el.textContent : null;
    });
    expect(vizCss).toContain('opacity: 0');

    // viz-card should show level description
    await expect(page.locator('.viz-hint')).toContainText('Invisible');
  });

  test('pieces restored after puzzle solved', async ({ page }) => {
    await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
    await page.addInitScript(() => {
      localStorage.setItem('rookhub_visualization', '2');
      localStorage.setItem('rookhub_puzzle_config', JSON.stringify({ stockfishDepth: 1 }));
    });
    await page.goto('/puzzles');

    const board = page.locator('cg-board');
    await expect(board).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('.status-text')).toContainText('Your turn', { timeout: 10_000 });

    // Wait for countdown to finish so pieces are hidden
    await expect(page.locator('.viz-countdown')).not.toBeVisible({ timeout: 6_000 });
    await expect(page.locator('.board-section')).toHaveClass(/viz-hidden/, { timeout: 2_000 });

    // Solve the puzzle: correct move Qd1→h5 (visualization mode = click squares)
    await makeMove(page, board, 'd1', 'h5');

    // Puzzle solved
    await expect(page.locator('.status-text')).toContainText('Correct', { timeout: 10_000 });

    // viz-hidden should be gone (pieces restored)
    await expect(page.locator('.board-section')).not.toHaveClass(/viz-hidden/, { timeout: 3_000 });

    // viz-hide-css should be removed
    const vizCssGone = await page.evaluate(() => !document.getElementById('viz-hide-css'));
    expect(vizCssGone).toBe(true);
  });

  test('level 0 disables visualization entirely', async ({ page }) => {
    await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
    await page.addInitScript(() => {
      localStorage.setItem('rookhub_visualization', '0');
    });
    await page.goto('/puzzles');

    const board = page.locator('cg-board');
    await expect(board).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('.status-text')).toContainText('Your turn', { timeout: 10_000 });

    // No viz-card should be shown (level 0 = normal mode)
    await expect(page.locator('.viz-card')).not.toBeVisible();

    // No viz-hidden class on board-section
    await expect(page.locator('.board-section')).not.toHaveClass(/viz-hidden/);
  });
});
