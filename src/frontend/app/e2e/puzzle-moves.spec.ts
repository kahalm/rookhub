import { test as base, expect, Page, Locator } from '@playwright/test';
import { expectState, seedSolveMode, statusCard } from './fixtures/solver';

const test = base;

// ─── Deterministic test puzzles ─────────────────────────────────────────
// FEN = position after 1.e4 (black to move).
// Setup move is always e7e5 (black pawn). User plays white (orientation='white').
const PUZZLE_FEN = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1';

// 2-move: setup e7e5, user must play Qd1-h5
const TWO_MOVE_PUZZLE = {
  id: 99001, lichessId: 'e2eWrong',
  fen: PUZZLE_FEN, moves: 'e7e5 d1h5', rating: 1000, themes: 'test',
};

// 4-move: setup e7e5, user Qd1-h5, opponent Ng8-f6, user Qh5xe5+
const FOUR_MOVE_PUZZLE = {
  id: 99002, lichessId: 'e2ePremove',
  fen: PUZZLE_FEN, moves: 'e7e5 d1h5 g8f6 h5e5', rating: 1000, themes: 'test',
};

// Complex position for Stockfish timeout test: Sicilian Najdorf, white to move
const COMPLEX_PUZZLE = {
  id: 99003, lichessId: 'e2eTimeout',
  fen: 'r1bqkb1r/pp3ppp/2nppn2/6B1/3NP3/2N5/PPP2PPP/R2QKB1R w KQkq - 0 6',
  moves: 'd4c6 b7c6', rating: 1500, themes: 'test',
};

// ─── Helpers ────────────────────────────────────────────────────────────

/** Pixel center of a board square. orientation='white': a1=bottom-left; 'black': a8=bottom-right. */
function squareCenter(boardWidth: number, square: string, orientation: 'white' | 'black' = 'white') {
  const sq = boardWidth / 8;
  const file = square.charCodeAt(0) - 97;
  const rank = parseInt(square[1]) - 1;
  if (orientation === 'white') {
    return { x: (file + 0.5) * sq, y: (7 - rank + 0.5) * sq };
  }
  return { x: (7 - file + 0.5) * sq, y: (rank + 0.5) * sq };
}

/** Click on a specific board square. */
async function clickSquare(page: Page, board: Locator, square: string, orientation: 'white' | 'black' = 'white') {
  const box = await board.boundingBox();
  if (!box) throw new Error('Board bounding box not available');
  const pos = squareCenter(box.width, square, orientation);
  await page.mouse.click(box.x + pos.x, box.y + pos.y);
}

/** Click-click move: select piece on `from`, then click `to`. */
async function makeMove(page: Page, board: Locator, from: string, to: string, orientation: 'white' | 'black' = 'white') {
  await clickSquare(page, board, from, orientation);
  await page.waitForTimeout(150);
  await clickSquare(page, board, to, orientation);
}

/** Drag-based premove (more reliable than click-click for premovable state). */
async function dragMove(page: Page, board: Locator, from: string, to: string, orientation: 'white' | 'black' = 'white') {
  const box = await board.boundingBox();
  if (!box) throw new Error('Board bounding box not available');
  const f = squareCenter(box.width, from, orientation);
  const t = squareCenter(box.width, to, orientation);
  await page.mouse.move(box.x + f.x, box.y + f.y);
  await page.mouse.down();
  await page.mouse.move(box.x + t.x, box.y + t.y, { steps: 5 });
  await page.mouse.up();
}

/**
 * Waits until `piece` (chessground classes, e.g. 'black knight') stands on `square`. chessground
 * keeps the square as `cgKey` on the element. Needed because the solver's reply is scripted: the
 * state is AWAITING_USER_MOVE both before the user's move and after the reply, so waiting for the
 * state alone could pass on the old position.
 */
async function expectPieceOn(page: Page, square: string, piece: string, timeout = 10_000) {
  await expect.poll(() => page.evaluate(({ sq, cls }) =>
    Array.from(document.querySelectorAll('cg-board piece')).some(p =>
      (p as HTMLElement & { cgKey?: string }).cgKey === sq
      && cls.split(' ').every(c => p.classList.contains(c))),
  { sq: square, cls: piece }), { timeout }).toBe(true);
}

/** Mock /api/puzzles endpoints (no auth → stats/attempt return 401). */
async function mockPuzzleApi(page: Page, puzzle: object) {
  await page.route('**/api/puzzles/random**', r =>
    r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(puzzle) }));
  await page.route('**/api/puzzles/*/attempt**', r => r.fulfill({ status: 401 }));
  await page.route('**/api/puzzles/stats**', r => r.fulfill({ status: 401 }));
}

/**
 * Mock puzzle API + the endless endpoints. A run fetches its whole rating chain in ONE
 * POST /api/puzzles/random-batch (one puzzle per rating window). The single-puzzle route above
 * matches that URL too and would hand the chain an object instead of an array — the run then
 * never leaves LOADING. Registered last, so this route takes precedence.
 */
async function mockEndlessApi(page: Page, puzzle: { id: number; lichessId: string }) {
  await mockPuzzleApi(page, puzzle);
  await page.route('**/api/puzzles/rating-range**', r =>
    r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ min: 600, max: 3000 }) }));
  await page.route('**/api/puzzles/random-batch**', r => {
    const windows = (r.request().postDataJSON()?.windows ?? []) as unknown[];
    const chain = Array.from({ length: Math.max(1, windows.length) }, (_, i) =>
      ({ ...puzzle, id: puzzle.id + i, lichessId: `${puzzle.lichessId}${i}` }));
    return r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(chain) });
  });
}

async function useStockfishDepth(page: Page, depth: number) {
  await page.addInitScript((d: number) => {
    localStorage.setItem('rookhub_puzzle_config', JSON.stringify({ stockfishDepth: d }));
  }, depth);
}

async function useEndlessConfig(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('rookhub_endless_config', JSON.stringify({
      startElo: 1000, step: 100, themes: '', fasttrack: false, stockfishDepth: 1,
    }));
  });
}

// ─── Tests ──────────────────────────────────────────────────────────────

test.describe('Puzzle Moves', () => {

  // Figuren normal ziehbar (Stufe 0) — und kein Spielweisen-Dialog vor dem Brett.
  test.beforeEach(async ({ page }) => {
    await seedSolveMode(page, 'easy');
  });

  // ── Normal Puzzle Mode ──────────────────────────────────────────────

  test.describe('Normal Puzzle Mode', () => {

    test('wrong move transitions to PLAYING with action buttons', async ({ page }) => {
      await mockPuzzleApi(page, TWO_MOVE_PUZZLE);
      await useStockfishDepth(page, 1);

      await page.goto('/puzzles');
      const board = page.locator('cg-board');
      await expect(board).toBeVisible({ timeout: 15_000 });
      // Wait for AWAITING_USER_MOVE (after 600ms setup move)
      await expectState(page, 'AWAITING_USER_MOVE');

      // Wrong move: a2→a3 (correct would be Qd1→h5)
      await makeMove(page, board, 'a2', 'a3');

      // Stockfish responds (THINKING) → 400ms delay → PLAYING
      await expectState(page, 'PLAYING', 15_000);
      const card = statusCard(page);
      await expect(card).toContainText('Your turn');

      // All PLAYING-state action buttons visible
      await expect(card.getByRole('button', { name: /Show Eval/ })).toBeVisible();
      await expect(card.getByRole('button', { name: /Reset/ })).toBeVisible();
      await expect(card.getByRole('button', { name: /Mouseslip/ })).toBeVisible();
      await expect(card.getByRole('button', { name: /Give Up/ })).toBeVisible();
    });

    test('wrong move with Stockfish timeout keeps PLAYING (no instant Incorrect)', async ({ page }) => {
      // Eigenes Zeitbudget: das Warten unten reicht bis 40 s (siehe dort), dazu Laden + Zug.
      test.setTimeout(90_000);
      // Use a complex middlegame position where Stockfish WASM Lite (single-thread)
      // at depth 24 exceeds the 10s timeout in runSearch, triggering the catch block.
      await mockPuzzleApi(page, COMPLEX_PUZZLE);
      await useStockfishDepth(page, 24);

      await page.goto('/puzzles');
      const board = page.locator('cg-board');
      await expect(board).toBeVisible({ timeout: 15_000 });
      await expectState(page, 'AWAITING_USER_MOVE');

      // Wrong move: a7→a5 (correct would be b7xc6). User is black (orientation=black).
      await makeMove(page, board, 'a7', 'a5', 'black');

      // After wrong move, Stockfish (white) should be thinking
      await expectState(page, 'THINKING', 5_000);
      await expect(statusCard(page)).toContainText('Stockfish is thinking');

      // Wait for the search timeout to fire → state changes away from THINKING. Die Suchen laufen in
      // EINER Warteschlange (StockfishService.getBestMove): vor dieser stehen im schlimmsten Fall der
      // Engine-Start (15 s), eine fruehere Suche (10 s) samt Stopp-Frist (2 s) — erst dann laufen die
      // eigenen 10 s. Gesichert wird hier „nicht FAILED nach dem Timeout“, nicht die Geschwindigkeit.
      await expect(statusCard(page)).not.toHaveAttribute('data-state', 'THINKING', { timeout: 40_000 });

      // Regression: the catch block used to set FAILED ('Incorrect') instead of letting the user continue
      await expect(statusCard(page)).not.toHaveAttribute('data-state', 'FAILED');
      await expect(statusCard(page).getByRole('button', { name: /Reset/ })).toBeVisible({ timeout: 5_000 });
    });

    test('4-move puzzle: second correct move is recognised as solved (no premove)', async ({ page }) => {
      // Regression: nach dem Refactor in v0.38.2 wurde der state nach dem
      // Stockfish-Antwortzug fälschlich auf PLAYING gesetzt. onMoveMade
      // behandelt PLAYING aber als off-path → der 2. Lösungszug zählte nicht
      // mehr als gelöst, der User konnte einfach weiterspielen.
      await mockPuzzleApi(page, FOUR_MOVE_PUZZLE);
      await useStockfishDepth(page, 1);

      await page.goto('/puzzles');
      const board = page.locator('cg-board');
      await expect(board).toBeVisible({ timeout: 15_000 });
      await expectState(page, 'AWAITING_USER_MOVE');

      // 1. korrekter User-Zug: Qd1→h5
      await makeMove(page, board, 'd1', 'h5');
      // Solver antwortet (g8f6) → 400ms später wieder dran
      await expectPieceOn(page, 'f6', 'black knight');
      await expectState(page, 'AWAITING_USER_MOVE');
      // 2. korrekter User-Zug: Qh5→e5 → muss SOLVED auslösen
      await makeMove(page, board, 'h5', 'e5');
      await expectState(page, 'SOLVED');
      await expect(statusCard(page)).toContainText('Correct');
    });

    test('premove during THINKING solves the puzzle', async ({ page }) => {
      await mockPuzzleApi(page, FOUR_MOVE_PUZZLE);
      await useStockfishDepth(page, 1);

      await page.goto('/puzzles');
      const board = page.locator('cg-board');
      await expect(board).toBeVisible({ timeout: 15_000 });
      await expectState(page, 'AWAITING_USER_MOVE');

      // Correct first move: Qd1→h5
      await makeMove(page, board, 'd1', 'h5');

      // Brief wait for board to enter THINKING/premovable mode, then drag premove
      await page.waitForTimeout(200);
      await dragMove(page, board, 'h5', 'e5');

      // Premove fires after opponent responds → puzzle solved
      // If premove didn't register, the move executes as regular move after AWAITING_USER_MOVE
      await expectState(page, 'SOLVED');
    });

    test('mouseslip restores solution path: wrong move undone, correct solution still wins', async ({ page }) => {
      // Regression: mouseslip ließ onSolutionPath=false und state=PLAYING zurück,
      // dadurch wurde der danach gespielte korrekte Zug wieder als off-path
      // behandelt und das Puzzle endete nie.
      await mockPuzzleApi(page, FOUR_MOVE_PUZZLE);
      await useStockfishDepth(page, 1);

      await page.goto('/puzzles');
      const board = page.locator('cg-board');
      await expect(board).toBeVisible({ timeout: 15_000 });
      await expectState(page, 'AWAITING_USER_MOVE');

      // Falscher 1. Zug: a2→a3 (statt Qd1→h5)
      await makeMove(page, board, 'a2', 'a3');
      // Stockfish antwortet → PLAYING (Mouseslip-Button sichtbar)
      const mouseslipBtn = statusCard(page).getByRole('button', { name: /Mouseslip/ });
      await expect(mouseslipBtn).toBeVisible({ timeout: 15_000 });
      await mouseslipBtn.click();

      // Nun korrekte Lösung in zwei Zügen
      await expectState(page, 'AWAITING_USER_MOVE', 5_000);
      await makeMove(page, board, 'd1', 'h5');
      await expectPieceOn(page, 'f6', 'black knight');
      await expectState(page, 'AWAITING_USER_MOVE');
      await makeMove(page, board, 'h5', 'e5');
      await expectState(page, 'SOLVED');
    });
  });

  // ── Endless Mode ────────────────────────────────────────────────────

  test.describe('Endless Mode', () => {

    test('wrong move transitions to PLAYING with action buttons', async ({ page }) => {
      await mockEndlessApi(page, TWO_MOVE_PUZZLE);
      await useEndlessConfig(page);

      await page.goto('/puzzles/endless');

      // Start game from config screen
      await page.getByRole('button', { name: /start/i }).click();

      const board = page.locator('cg-board');
      await expect(board).toBeVisible({ timeout: 15_000 });
      await expectState(page, 'AWAITING_USER_MOVE');

      // Wrong move: a2→a3
      await makeMove(page, board, 'a2', 'a3');

      // Stockfish responds → PLAYING
      await expectState(page, 'PLAYING', 15_000);
      const card = statusCard(page);
      await expect(card).toContainText('Your move');

      // All action buttons visible
      await expect(card.getByRole('button', { name: /Show Eval/ })).toBeVisible();
      await expect(card.getByRole('button', { name: /Reset/ })).toBeVisible();
      await expect(card.getByRole('button', { name: /Mouseslip/ })).toBeVisible();
      await expect(card.getByRole('button', { name: /Give Up/ })).toBeVisible();
    });

    test('premove during THINKING solves the puzzle', async ({ page }) => {
      await mockEndlessApi(page, FOUR_MOVE_PUZZLE);
      await useEndlessConfig(page);

      await page.goto('/puzzles/endless');
      await page.getByRole('button', { name: /start/i }).click();

      const board = page.locator('cg-board');
      await expect(board).toBeVisible({ timeout: 15_000 });
      await expectState(page, 'AWAITING_USER_MOVE');

      // Correct first move: Qd1→h5
      await makeMove(page, board, 'd1', 'h5');

      // Brief wait for THINKING/premovable mode, then drag premove Qh5→e5
      await page.waitForTimeout(200);
      await dragMove(page, board, 'h5', 'e5');

      // Premove fires → CORRECT
      await expectState(page, 'SOLVED');
    });
  });
});
