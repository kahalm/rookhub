import { test as base, expect, Page, Locator } from '@playwright/test';
import { expectState, seedSolveMode, statusCard } from './fixtures/solver';

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

/**
 * Visualisierungsstufe vorbelegen. Die gemerkte Spielweise muss dazu passen, sonst ueberstimmt
 * sie die Stufe (SolveModeService.levelFor): „easy" ist immer Stufe 0, „training" mindestens 1.
 */
async function useVisualization(page: Page, level: number) {
  await seedSolveMode(page, level > 0 ? 'training' : 'easy');
  await page.addInitScript((lvl: number) => {
    localStorage.setItem('rookhub_visualization', String(lvl));
  }, level);
}

async function useStockfishDepth(page: Page, depth: number) {
  await page.addInitScript((d: number) => {
    localStorage.setItem('rookhub_puzzle_config', JSON.stringify({ stockfishDepth: d }));
  }, depth);
}

/** Die Leiste unter dem Brett (Am-Zug, Countdown, Auge). Sie steht ZWEIMAL im DOM — die Kopie
 *  in der Info-Spalte ist nur im App-Vollbild sichtbar. */
function hintBar(page: Page): Locator {
  return page.locator('.board-hint-slot--board .board-hint');
}

/** ⋮-Menue → Einstellungen; liefert die Auswahl der Visualisierungsstufe im Dialog. */
async function openVisualizationSetting(page: Page): Promise<Locator> {
  await page.locator('app-puzzle-action-bar').getByRole('button', { name: 'More', exact: true }).click();
  await page.getByRole('menuitem', { name: /Settings/ }).click();
  const select = page.locator('mat-dialog-container .psd-row')
    .filter({ hasText: 'Visualization' })
    .filter({ has: page.locator('mat-select') })
    .locator('mat-select');
  await expect(select).toBeVisible();
  return select;
}

async function chooseVisualization(page: Page, select: Locator, levelName: string) {
  await select.click();
  await page.getByRole('option', { name: levelName, exact: true }).click();
  await page.locator('mat-dialog-container').getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.locator('mat-dialog-container')).toHaveCount(0);
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

  test('settings dialog changes the visualization level and persists it', async ({ page }) => {
    await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
    await seedSolveMode(page, 'training');
    await page.goto('/puzzles');

    await expect(page.locator('cg-board')).toBeVisible({ timeout: 15_000 });
    await expectState(page, 'AWAITING_USER_MOVE');

    // Ohne gespeicherte Stufe gilt die Vorgabe 1 (Blindfold)
    const select = await openVisualizationSetting(page);
    await expect(select).toContainText('Blindfold');

    await chooseVisualization(page, select, 'Checker');
    await expect.poll(() => page.evaluate(() => localStorage.getItem('rookhub_visualization'))).toBe('2');
    // Der Wechsel startet das Puzzle neu — mit Stufe 2 laeuft der Countdown zum Verdecken
    await expect(hintBar(page).locator('.board-hint-countdown')).toContainText('Pieces disappear in', { timeout: 5_000 });

    // Stufe 0 zieht die gemerkte Spielweise auf „easy" mit, sonst widersprechen sich beide
    await chooseVisualization(page, await openVisualizationSetting(page), 'Normal');
    await expect.poll(() => page.evaluate(() => localStorage.getItem('rookhub_visualization'))).toBe('0');
    await expect.poll(() => page.evaluate(() =>
      JSON.parse(localStorage.getItem('rookhub_solve_modes') || '{}').puzzles?.mode)).toBe('easy');
  });

  test('hint bar and status card stack below the board on mobile', async ({ page }) => {
    await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
    await useVisualization(page, 1);
    await page.goto('/puzzles');

    const board = page.locator('cg-board');
    await expect(board).toBeVisible({ timeout: 15_000 });
    await expectState(page, 'AWAITING_USER_MOVE');

    const bar = hintBar(page);
    await expect(bar).toBeVisible({ timeout: 5_000 });
    // Stufe 1 (Blindspiel): das Auge deckt die Figuren kurz auf
    await expect(bar.locator('.board-hint-show')).toBeVisible();

    // Einspaltig: Leiste und Statuskarte liegen unter dem Brett
    const boardBox = await board.boundingBox();
    const barBox = await bar.boundingBox();
    const cardBox = await statusCard(page).boundingBox();
    expect(boardBox).toBeTruthy();
    expect(barBox).toBeTruthy();
    expect(cardBox).toBeTruthy();
    const boardBottom = boardBox!.y + boardBox!.height - 5;
    expect(barBox!.y).toBeGreaterThan(boardBottom);
    expect(cardBox!.y).toBeGreaterThan(boardBottom);
  });

  test('level 2 countdown + viz-hidden class + show button', async ({ page }) => {
    await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
    await useVisualization(page, 2);
    await page.goto('/puzzles');

    const board = page.locator('cg-board');
    await expect(board).toBeVisible({ timeout: 15_000 });
    // AWAITING_USER_MOVE startet den Countdown (beginSolving)
    await expectState(page, 'AWAITING_USER_MOVE');

    const countdown = hintBar(page).locator('.board-hint-countdown');
    await expect(countdown).toBeVisible({ timeout: 3_000 });
    await expect(countdown).toContainText('Pieces disappear in');

    // Countdown (3 s) laeuft ab → Figuren verdeckt
    await expect(countdown).not.toBeVisible({ timeout: 6_000 });
    await expect(page.locator('.board-section')).toHaveClass(/viz-hidden/, { timeout: 2_000 });

    // Show button should appear
    await expect(hintBar(page).locator('.board-hint-show')).toBeVisible({ timeout: 2_000 });
  });

  test('level 4 invisible: pieces hidden after countdown', async ({ page }) => {
    await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
    await useVisualization(page, 4);
    await page.goto('/puzzles');

    const board = page.locator('cg-board');
    await expect(board).toBeVisible({ timeout: 15_000 });
    await expectState(page, 'AWAITING_USER_MOVE');

    // Erst auf das Verdecken warten — „Countdown nicht sichtbar" gilt auch, bevor er ueberhaupt kam
    await expect(page.locator('.board-section')).toHaveClass(/viz-hidden/, { timeout: 8_000 });

    const vizCss = await page.evaluate(() => document.getElementById('viz-hide-css')?.textContent ?? null);
    expect(vizCss).toContain('opacity: 0');

    // Die Figuren stehen noch auf dem Brett, sind aber unsichtbar
    const opacity = await page.evaluate(() => {
      const piece = document.querySelector('.board-section cg-board piece');
      return piece ? getComputedStyle(piece).opacity : null;
    });
    expect(opacity).toBe('0');
  });

  test('pieces restored after puzzle solved', async ({ page }) => {
    await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
    await useVisualization(page, 2);
    await useStockfishDepth(page, 1);
    await page.goto('/puzzles');

    const board = page.locator('cg-board');
    await expect(board).toBeVisible({ timeout: 15_000 });
    await expectState(page, 'AWAITING_USER_MOVE');

    // Wait for countdown to finish so pieces are hidden
    await expect(page.locator('.board-section')).toHaveClass(/viz-hidden/, { timeout: 8_000 });

    // Solve the puzzle: correct move Qd1→h5 (visualization mode = click squares)
    await makeMove(page, board, 'd1', 'h5');

    await expectState(page, 'SOLVED');
    await expect(statusCard(page)).toContainText('Correct');

    // viz-hidden should be gone (pieces restored)
    await expect(page.locator('.board-section')).not.toHaveClass(/viz-hidden/, { timeout: 3_000 });

    // viz-hide-css should be removed
    await expect.poll(() => page.evaluate(() => !document.getElementById('viz-hide-css'))).toBe(true);
  });

  test('viz mode: illegal 2nd click becomes new origin (selection not lost)', async ({ page }) => {
    // Regression: ein illegaler 2. Klick im Viz-Modus liess vizFrom auf undefined
    // zurück → der naechste Klick startete ohne sichtbares Feedback wieder als orig.
    // Mit Fix wird der illegale 2. Klick selbst zum neuen Ausgangsfeld.
    await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
    await useVisualization(page, 1);
    await useStockfishDepth(page, 1);
    await page.goto('/puzzles');

    const board = page.locator('cg-board');
    await expect(board).toBeVisible({ timeout: 15_000 });
    await expectState(page, 'AWAITING_USER_MOVE');

    // Click chain d1 → a3 (illegal) → h5 (illegal von a3) → d1 (illegal von h5) → h5 (legal von d1).
    // Ohne Fix: nach jedem illegalen 2.-Klick verschwindet die Auswahl, die Sequenz löst nichts aus.
    // Mit Fix: vizFrom wandert d1→a3→h5→d1, dann legaler Qh5 → SOLVED.
    await clickSquare(page, board, 'd1');
    await page.waitForTimeout(100);
    await clickSquare(page, board, 'a3');
    await page.waitForTimeout(100);
    await clickSquare(page, board, 'h5');
    await page.waitForTimeout(100);
    await clickSquare(page, board, 'd1');
    await page.waitForTimeout(100);
    await clickSquare(page, board, 'h5');

    await expectState(page, 'SOLVED');
  });

  test('level 0 disables visualization entirely', async ({ page }) => {
    await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
    await useVisualization(page, 0);
    await page.goto('/puzzles');

    const board = page.locator('cg-board');
    await expect(board).toBeVisible({ timeout: 15_000 });
    await expectState(page, 'AWAITING_USER_MOVE');

    // Stufe 0 = normales Brett: Am-Zug-Anzeige statt Zugtext, kein Auge, nichts verdeckt
    await expect(hintBar(page).locator('.board-hint-tomove')).toContainText('White to move');
    await expect(hintBar(page).locator('.board-hint-show')).toHaveCount(0);
    await expect(page.locator('.board-section')).not.toHaveClass(/viz-hidden/);
    expect(await page.evaluate(() => !document.getElementById('viz-hide-css'))).toBe(true);
  });
});
