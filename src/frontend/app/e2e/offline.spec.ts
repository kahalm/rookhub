import { test, expect, BrowserContext, Page } from '@playwright/test';
import fs from 'fs';
import path from 'path';

/**
 * Offline-Modus Ende-zu-Ende (Prod-Build mit aktivem ngsw vorausgesetzt — im E2E-Stack
 * ist das Frontend das Prod-Image, der Service Worker also aktiv).
 *
 * Es gibt ZWEI Offline-Lagen, und beide müssen tragen (0.478.4):
 *  - „Server nicht erreichbar, Gerät online" — Funkloch, VPN, Server weg. Der ngsw beantwortet
 *    dann jede /api-Anfrage mit einer synthetischen 504, `navigator.onLine` bleibt `true`. Wer einen
 *    Offline-Rückfall nur an `!navigator.onLine` hängt, fällt hier durch (so brach Endless ab).
 *    Nachgestellt über `context.route('**\/api/**', abort)` — gemessen: die App bekommt 504,
 *    `onLine` bleibt `true`, die Shell lädt weiter aus dem Cache.
 *  - „Gerät offline" — `context.setOffline` + `navigator.onLine=false` per Init-Script. Das
 *    Init-Script ist nötig: bei aktivem Service Worker setzt Playwrights Offline-Schalter
 *    `navigator.onLine` nach einem Neuladen wieder auf `true` (gemessen).
 *
 * Alle Tests teilen EINEN Browser-Kontext (serial + beforeAll): der Service Worker registriert
 * sich erst nach `registerWhenStable:30000` und cacht dann rund hundert Dateien — je Test neu wären
 * das Minuten. Die Reihenfolge ist deshalb Absicht: das Init-Script „Gerät offline" lässt sich
 * nicht wieder entfernen und kommt zuletzt.
 *
 * Standalone (ohne API-Stack) gegen einen beliebigen servierten Prod-Build laufbar — der Test mit
 * dem echten Herunterladen wird dann übersprungen:
 *   E2E_OFFLINE_BASE=http://127.0.0.1:18099 npx playwright test --config=playwright.offline-local.config.ts
 */

const API_URL = process.env.E2E_API_URL || 'http://localhost:5002';
const AUTH_STATE = path.join(__dirname, '.auth-state.json');

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
const BOOK_TITLE = 'E2E Offline Book';
const bookPuzzle = {
  id: 990001,
  lineId: 'e2e-offline-1',
  bookFileName: BOOK_FILE,
  bookTitle: BOOK_TITLE,
  round: '001.001',
  fen: 'r6k/pp2r2p/4Rp1Q/3p4/8/1N1P2R1/PqP2bPP/7K b - - 0 24',
  moves: 'f2g3 e6e7 b2b1 b3c1 b1c1 h6c1',
  startPly: 0,
  title: 'E2E Offline',
};

/** Der Eintrag, den die Kursliste beim letzten Online-Aufruf zwischengespeichert hätte. */
const courseListItem = {
  bookId: BOOK_ID, fileName: BOOK_FILE, displayName: BOOK_TITLE,
  difficulty: null, rating: null, tags: null, description: null,
  puzzleCount: 1, solvedCount: 0, progressPercent: 0, lastMode: null, lastActivityAt: null,
  isOwned: false, isPinned: false,
};

/** Zwei kurze Eröffnungslinien — im Lernmodus sofort abfragbar (noch nichts gelernt). */
const REP_PGN = [
  '[Event "E2E Offline 1"]', '[White "?"]', '[Black "?"]', '[Result "*"]', '',
  '1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 *', '',
  '[Event "E2E Offline 2"]', '[White "?"]', '[Black "?"]', '[Result "*"]', '',
  '1. e4 c5 2. Nf3 d6 3. d4 cxd4 *', '',
].join('\n');

/** Ein heruntergeladenes Repertoire im Format von repertoire-offline.util.ts. */
const REP_ID = 990201;
const REP_NAME = 'E2E Offline-Repertoire';
const offlineRepertoire = {
  meta: {
    id: REP_ID, name: REP_NAME, description: null, isPublic: false, kind: 'opening', fileCount: 1,
    useForExtension: false, createdAt: '2026-09-15T00:00:00Z', updatedAt: '2026-09-15T00:00:00Z',
    chessableCourseId: null,
  },
  pgn: REP_PGN, states: [], config: null, savedAt: '2026-09-15T00:00:00Z',
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
  await page.goto('/', { waitUntil: 'load' });
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

/**
 * Ab der nächsten Navigation die Offline-Caches eines „Rückkehrers" seeden — so, wie sie ein realer
 * Nutzer nach Online-Nutzung hat (Pools werden online automatisch vorgeladen, Kurs und Repertoire
 * per ☁ heruntergeladen; hier deterministisch statt API-abhängig). `deviceOffline` zusätzlich:
 * `navigator.onLine=false` + Netz aus.
 */
async function seedReturningUser(context: BrowserContext, deviceOffline: boolean): Promise<void> {
  await context.addInitScript(
    (seed: {
      deviceOffline: boolean; puzzles: unknown[]; book: typeof bookPuzzle; bookId: number;
      course: typeof courseListItem; repertoire: typeof offlineRepertoire; jwt: string;
    }) => {
      if (seed.deviceOffline) {
        Object.defineProperty(Navigator.prototype, 'onLine', { get: () => false, configurable: true });
      }
      try {
        const at = Date.now();
        localStorage.setItem('rookhub_menu_keys', JSON.stringify(['puzzles', 'endless', 'courses', 'repertoires', 'analysis']));
        localStorage.setItem('rookhub_solve_modes', JSON.stringify({ puzzles: { mode: 'easy', at }, endless: { mode: 'easy', at } }));
        localStorage.setItem('rookhub_puzzle_offline_pool', JSON.stringify(seed.puzzles));
        localStorage.setItem('rookhub_endless_offline_pool', JSON.stringify(seed.puzzles));
        localStorage.setItem('rookhub_book_offline_' + encodeURIComponent(seed.book.bookFileName), JSON.stringify([seed.book]));
        localStorage.setItem('rookhub_book_idmap', JSON.stringify({ [String(seed.bookId)]: seed.book.bookFileName }));
        localStorage.setItem('rookhub_courses_cache', JSON.stringify([seed.course]));
        localStorage.setItem('rookhub_repertoire_offline_' + seed.repertoire.meta.id, JSON.stringify(seed.repertoire));
        localStorage.setItem('rookhub_user', JSON.stringify({ token: seed.jwt, username: 'e2e-offline', isAdmin: false }));
      } catch { /* Storage nicht verfügbar → Test schlägt an den Asserts fehl */ }
    },
    {
      deviceOffline, puzzles: [poolPuzzle(990101), poolPuzzle(990102), poolPuzzle(990103)],
      book: bookPuzzle, bookId: BOOK_ID, course: courseListItem, repertoire: offlineRepertoire, jwt: FAKE_JWT,
    },
  );
  if (deviceOffline) await context.setOffline(true);
}

/** Server nicht erreichbar: jede /api-Anfrage scheitert, der ngsw macht daraus eine 504. */
async function blockApi(context: BrowserContext): Promise<void> {
  await context.route('**/api/**', route => route.abort('connectionrefused'));
}

async function expectBoardWithPieces(page: Page): Promise<void> {
  await expect(page.locator('cg-board').first()).toBeVisible({ timeout: 20_000 });
  await expect.poll(() => page.locator('piece').count(), { timeout: 20_000 }).toBeGreaterThan(4);
}

/** Die Seiten, die mit heruntergeladenen Inhalten in BEIDEN Lagen tragen müssen. */
async function expectDownloadedContentWorks(page: Page): Promise<void> {
  await test.step('Standard-Puzzle aus dem vorgeladenen Pool', async () => {
    await page.goto('/puzzles', { waitUntil: 'domcontentloaded' });
    await expectBoardWithPieces(page);
  });

  await test.step('Endless startet aus der vorab geladenen Kette', async () => {
    await page.goto('/puzzles/endless', { waitUntil: 'domcontentloaded' });
    await expect(page.locator('.config-screen')).toBeVisible({ timeout: 20_000 });
    // Der obere Knopf startet IMMER einen neuen Lauf — er heißt „Start" oder, wenn ein Lauf aus
    // einem früheren Schritt noch offen ist, „New Game". Über den Text gesucht fand der zweite
    // Durchgang ihn nicht mehr.
    await page.locator('button.start-btn-top').click();
    await expectBoardWithPieces(page);
  });

  await test.step('Kurs aus dem heruntergeladenen Buch', async () => {
    await page.goto(`/courses/${BOOK_ID}/sequential`, { waitUntil: 'domcontentloaded' });
    await expectBoardWithPieces(page);
  });

  await test.step('Kursliste zeigt den heruntergeladenen Kurs', async () => {
    await page.goto('/courses', { waitUntil: 'domcontentloaded' });
    await expect(page.locator('.offline-banner')).toBeVisible({ timeout: 20_000 });
    await expect(page.locator('app-course-card', { hasText: BOOK_TITLE })).toBeVisible();
  });

  await test.step('Repertoireliste zeigt das heruntergeladene Repertoire', async () => {
    await page.goto('/repertoires', { waitUntil: 'domcontentloaded' });
    await expect(page.locator('.offline-banner')).toBeVisible({ timeout: 20_000 });
    await expect(page.locator('mat-card', { hasText: REP_NAME })).toBeVisible();
  });

  await test.step('Repertoire-Training läuft aus der Offline-Kopie', async () => {
    await page.goto(`/repertoires/${REP_ID}/train?mode=learn`, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('.offline-chip')).toBeVisible({ timeout: 20_000 });
    await expectBoardWithPieces(page);
  });
}

function readSharedAuth(): { auth: { token: string } } | null {
  try { return JSON.parse(fs.readFileSync(AUTH_STATE, 'utf-8')); } catch { return null; }
}

async function createRepertoireViaApi(token: string, name: string): Promise<number> {
  const headers = { Authorization: `Bearer ${token}` };
  const created = await fetch(`${API_URL}/api/repertoires`, {
    method: 'POST',
    headers: { ...headers, 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, description: 'E2E Offline', kind: 'opening', isPublic: false }),
  });
  if (!created.ok) throw new Error(`Repertoire anlegen: ${created.status} ${await created.text()}`);
  const { id } = await created.json() as { id: number };

  const form = new FormData();
  form.append('file', new Blob([REP_PGN], { type: 'application/x-chess-pgn' }), 'e2e-offline.pgn');
  const uploaded = await fetch(`${API_URL}/api/repertoires/${id}/files`, { method: 'POST', headers, body: form });
  if (!uploaded.ok) throw new Error(`PGN hochladen: ${uploaded.status} ${await uploaded.text()}`);
  return id;
}

test.describe('Offline-Modus (Service Worker + heruntergeladene Inhalte)', () => {
  test.describe.configure({ mode: 'serial', timeout: 240_000 });

  let context: BrowserContext;
  let page: Page;

  test.beforeAll(async ({ browser }, testInfo) => {
    test.setTimeout(180_000);
    // Eigener Kontext → die baseURL der Config muss ausdrücklich mit.
    const baseURL = process.env.E2E_OFFLINE_BASE || testInfo.project.use.baseURL;
    context = await browser.newContext({ baseURL });
    page = await context.newPage();
    await waitForServiceWorkerReady(page);
  });

  test.afterAll(async () => {
    await context?.close();
  });

  test('online heruntergeladenes Repertoire trägt, wenn der Server danach nicht antwortet', async () => {
    const shared = readSharedAuth();
    test.skip(!shared, 'braucht den API-Stack (global-setup.e2e.ts legt den Testnutzer an)');
    const name = `E2E Download ${Date.now().toString(36)}`;
    const id = await createRepertoireViaApi(shared!.auth.token, name);

    await test.step('online: anmelden und per ☁ herunterladen', async () => {
      await page.goto('/', { waitUntil: 'domcontentloaded' });
      await page.evaluate(auth => localStorage.setItem('rookhub_user', JSON.stringify(auth)), shared!.auth);
      await page.goto('/repertoires', { waitUntil: 'domcontentloaded' });
      const toggle = page.locator('mat-card', { hasText: name }).locator('.offline-toggle');
      await expect(toggle).toContainText('cloud_download', { timeout: 20_000 });
      await toggle.click();
      await expect(toggle).toContainText('cloud_done', { timeout: 20_000 });
    });

    await blockApi(context);
    try {
      await test.step('Server weg: Liste zeigt die Kopie', async () => {
        await page.goto('/repertoires', { waitUntil: 'domcontentloaded' });
        await expect(page.locator('.offline-banner')).toBeVisible({ timeout: 20_000 });
        await expect(page.locator('mat-card', { hasText: name })).toBeVisible();
      });
      await test.step('Server weg: Training aus der Kopie', async () => {
        await page.goto(`/repertoires/${id}/train?mode=learn`, { waitUntil: 'domcontentloaded' });
        await expect(page.locator('.offline-chip')).toBeVisible({ timeout: 20_000 });
        await expectBoardWithPieces(page);
      });
    } finally {
      await context.unrouteAll({ behavior: 'ignoreErrors' });
    }
  });

  test('Server nicht erreichbar, Gerät online: heruntergeladene Inhalte tragen', async () => {
    await seedReturningUser(context, false);
    await blockApi(context);
    try {
      await expectDownloadedContentWorks(page);
      await test.step('Banner meldet „Server nicht erreichbar" (nicht „offline")', async () => {
        await expect(page.locator('.conn-banner')).toBeVisible({ timeout: 25_000 });
        await expect(page.locator('.conn-banner')).not.toHaveClass(/conn-offline/);
      });
    } finally {
      await context.unrouteAll({ behavior: 'ignoreErrors' });
    }
  });

  test('Gerät offline: App-Shell und heruntergeladene Inhalte funktionieren', async () => {
    await seedReturningUser(context, true);

    await test.step('App-Shell lädt offline aus dem SW-Cache', async () => {
      await page.goto('/', { waitUntil: 'domcontentloaded' });
      await expect(page.locator('app-navbar')).toBeVisible({ timeout: 15_000 });
      await expect(page.locator('.app-footer .version-link')).toContainText('v0.', { timeout: 15_000 });
      await expect(page.locator('.conn-banner.conn-offline')).toBeVisible({ timeout: 15_000 });
    });

    await expectDownloadedContentWorks(page);
  });
});
