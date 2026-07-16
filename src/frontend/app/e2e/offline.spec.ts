import { test, expect, Page } from '@playwright/test';

/**
 * Offline-Modus Ende-zu-Ende (Prod-Build mit aktivem ngsw vorausgesetzt — im E2E-Stack
 * ist das Frontend das Prod-Image, der Service Worker also aktiv).
 *
 * Ablauf je Test: Phase 1 lädt die App ONLINE und wartet, bis der Angular Service Worker
 * installiert ist und alle Asset-Gruppen fertig gecacht hat (/ngsw/state + CacheStorage).
 * Phase 2 erzwingt Offline (context.setOffline + navigator.onLine=false via Init-Script —
 * Chromiums Offline-Emulation greift nicht zuverlässig für SW-vermittelte Fetches, die
 * onLine-Abfragen der Komponenten sind aber der maßgebliche Schalter der Offline-Pfade)
 * und seedet die Offline-Caches so, wie sie ein realer Nutzer nach Online-Nutzung hat
 * (Pools werden online automatisch vorgeladen; hier deterministisch statt API-abhängig).
 *
 * Standalone (ohne API-Stack) gegen einen beliebigen servierten Prod-Build laufbar:
 *   E2E_OFFLINE_BASE=http://127.0.0.1:18099 npx playwright test --config=playwright.offline-local.config.ts
 */

const BASE = process.env.E2E_OFFLINE_BASE || '';

/** Echtes lichess-Puzzle (00008) im Pool-Format: moves[0] = Gegner-Setup-Zug. */
const poolPuzzle = (id: number) => ({
  id,
  lichessId: '00008',
  fen: 'r6k/pp2r2p/4Rp1Q/3p4/8/1N1P2R1/PqP2bPP/7K b - - 0 24',
  moves: 'f2g3 e6e7 b2b1 b3c1 b1c1 h6c1',
  rating: 1902,
  themes: 'crushing hangingPiece long middlegame',
});

/** Dasselbe Puzzle als offline gespeicherte Kurs-/Buch-Linie. */
const BOOK_FILE = 'e2e-offline-book.pgn';
const BOOK_ID = 990077; // fiktive bookId — der Offline-Pfad fragt den Server nie
const bookPuzzle = {
  id: 990001,
  lineId: 'e2e-offline-1',
  bookFileName: BOOK_FILE,
  bookTitle: 'E2E Offline Book',
  round: '001.001',
  fen: 'r6k/pp2r2p/4Rp1Q/3p4/8/1N1P2R1/PqP2bPP/7K b - - 0 24',
  moves: 'f2g3 e6e7 b2b1 b3c1 b1c1 h6c1',
  startPly: 0,
  title: 'E2E Offline',
};

/** Nur clientseitig geprüfter JWT (exp weit in der Zukunft) — genügt authGuard/isLoggedIn. */
const FAKE_JWT = [
  Buffer.from(JSON.stringify({ alg: 'none', typ: 'JWT' })).toString('base64url'),
  Buffer.from(JSON.stringify({ sub: '1', unique_name: 'e2e-offline', exp: 4102444800 })).toString('base64url'),
  'e2e',
].join('.');

/**
 * Wartet, bis der ngsw installiert ist, NORMAL meldet und der Asset-Prefetch VOLLSTÄNDIG ist.
 * „Vollständig" = Cache-Anzahl über mehrere Poll-Ticks stabil, plausibel groß (App-Chunks ~100 +
 * i18n + Engine) UND das Stockfish-WASM (Teil der letzten Prefetch-Gruppe) liegt im Cache.
 * Ein zu frühes Offline-Schalten mitten im Prefetch hinterlässt sonst fehlende Chunks → weiße Seite.
 */
async function waitForServiceWorkerReady(page: Page): Promise<void> {
  await page.goto(BASE + '/', { waitUntil: 'load' });
  // registerWhenStable:30000 → Registrierung kann bis ~30 s auf App-Stabilität warten.
  await page.evaluate(() => navigator.serviceWorker.ready, undefined);

  const snapshot = () =>
    page.evaluate(async () => {
      const state = await fetch('/ngsw/state').then(r => r.text()).catch(() => '');
      let cached = 0;
      let wasm = false;
      for (const key of await caches.keys()) {
        if (!key.includes(':assets:')) continue;
        const cache = await caches.open(key);
        cached += (await cache.keys()).length;
        if (await cache.match('/assets/stockfish/stockfish-18-lite-single.wasm', { ignoreSearch: true })) wasm = true;
      }
      return { normal: /Driver state: NORMAL/.test(state), cached, wasm };
    });

  const deadline = Date.now() + 120_000;
  let last = -1;
  let stableTicks = 0;
  while (Date.now() < deadline) {
    const s = await snapshot();
    stableTicks = s.normal && s.wasm && s.cached > 100 && s.cached === last ? stableTicks + 1 : 0;
    last = s.cached;
    if (stableTicks >= 2) return;
    await page.waitForTimeout(1_000);
  }
  throw new Error(`Service-Worker-Prefetch wurde nicht vollständig (zuletzt ${last} Cache-Einträge)`);
}

/** Ab der nächsten Navigation: offline erzwingen + Offline-Caches eines „Rückkehrers" seeden. */
async function goOffline(page: Page): Promise<void> {
  await page.context().addInitScript(
    (seed: { puzzles: unknown[]; book: typeof bookPuzzle; bookId: number; jwt: string }) => {
      Object.defineProperty(Navigator.prototype, 'onLine', { get: () => false, configurable: true });
      try {
        localStorage.setItem('rookhub_menu_keys', JSON.stringify(['puzzles', 'endless', 'courses', 'analysis']));
        localStorage.setItem('rookhub_puzzle_offline_pool', JSON.stringify(seed.puzzles));
        localStorage.setItem('rookhub_endless_offline_pool', JSON.stringify(seed.puzzles));
        localStorage.setItem('rookhub_book_offline_' + encodeURIComponent(seed.book.bookFileName), JSON.stringify([seed.book]));
        localStorage.setItem('rookhub_book_idmap', JSON.stringify({ [String(seed.bookId)]: seed.book.bookFileName }));
        localStorage.setItem('rookhub_user', JSON.stringify({ token: seed.jwt, username: 'e2e-offline', isAdmin: false }));
      } catch { /* Storage nicht verfügbar → Test schlägt an den Asserts fehl */ }
    },
    { puzzles: [poolPuzzle(990101), poolPuzzle(990102), poolPuzzle(990103)], book: bookPuzzle, bookId: BOOK_ID, jwt: FAKE_JWT },
  );
  await page.context().setOffline(true);
}

async function expectBoardWithPieces(page: Page): Promise<void> {
  await expect(page.locator('cg-board').first()).toBeVisible({ timeout: 15_000 });
  await expect.poll(() => page.locator('piece').count(), { timeout: 15_000 }).toBeGreaterThan(4);
}

test.describe('Offline-Modus (Service Worker + lokale Pools)', () => {
  test.describe.configure({ mode: 'serial' });

  test('App-Shell und alle drei Lösemodi funktionieren offline', async ({ page }) => {
    test.setTimeout(180_000);

    await test.step('Phase 1: online laden, Service Worker installieren + Caches füllen', async () => {
      await waitForServiceWorkerReady(page);
    });

    await test.step('Phase 2: offline schalten + Rückkehrer-Caches seeden', async () => {
      await goOffline(page);
    });

    await test.step('App-Shell lädt offline aus dem SW-Cache', async () => {
      await page.goto(BASE + '/', { waitUntil: 'domcontentloaded' });
      await expect(page.locator('app-navbar')).toBeVisible({ timeout: 15_000 });
      await expect(page.locator('.app-footer .version-link')).toContainText('v0.', { timeout: 15_000 });
    });

    await test.step('Standard-Puzzle offline aus dem vorgeladenen Pool', async () => {
      await page.goto(BASE + '/puzzles', { waitUntil: 'domcontentloaded' });
      await expectBoardWithPieces(page);
    });

    await test.step('Endless offline aus dem Run-Cache starten', async () => {
      await page.goto(BASE + '/puzzles/endless', { waitUntil: 'domcontentloaded' });
      await expect(page.locator('.config-screen')).toBeVisible({ timeout: 15_000 });
      await page.getByRole('button', { name: /start/i }).first().click();
      await expectBoardWithPieces(page);
    });

    await test.step('Kurs offline aus dem gecachten Buch', async () => {
      await page.goto(BASE + `/courses/${BOOK_ID}/sequential`, { waitUntil: 'domcontentloaded' });
      await expectBoardWithPieces(page);
    });
  });
});
