import { TestBed } from '@angular/core/testing';
import { OfflineService } from './offline.service';
import { STORAGE_KEYS, classifyStorageKey, clearedOnLogout } from './local-storage-keys';

/**
 * Codereview F1-010: jeder Browser-Speicher-Schlüssel der Oberflächen ist eingeordnet (`local-storage-keys.ts`), und das
 * Abmelden räumt genau die Klasse `user`. Die Quelltexte liefert karma.conf.js aus (wie für `i18n-usage.spec.ts`).
 */
type Served = { __karma__?: { files?: Record<string, unknown> } };

/** Literale mit einem Speicher-Präfix; `${` am Ende = Anfang eines zusammengesetzten Schlüssels (Template-Literal). */
const KEY_LITERAL = /(['"`])((?:rookhub_|rh\.|rh-kids-|lh-|leaguehub|public_fav_)[\w.\-]*)(\1|\$\{)/g;

/** Kommentare und Komponenten-Selektoren heraus — sie tragen dieselben Präfixe, sind aber keine Schlüssel. */
function stripNonKeys(src: string): string {
  return src
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/(^|[\s;{}(),])\/\/[^\n]*/g, '$1')
    .replace(/selector:\s*'[^']*'/g, '');
}

function storageLiterals(src: string): string[] {
  return [...stripNonKeys(src).matchAll(KEY_LITERAL)].map(m => (m[3] === '${' ? `${m[2]}\${` : m[2]));
}

function sourceUrls(): string[] {
  const served = (globalThis as Served).__karma__?.files ?? {};
  return Object.keys(served).filter(u => /^\/base\/src[\w-]*\/.+\.(ts|html)$/.test(u)
    && !u.endsWith('.spec.ts') && !u.includes('/environments/changelog') && !u.endsWith('/core/local-storage-keys.ts'));
}

describe('Browser-Speicher: jeder Schlüssel eingeordnet (F1-010)', () => {
  it('der Sucher findet Schlüssel, Präfix-Anfänge und überspringt Kommentare/Selektoren', () => {
    const src = `const A = 'rookhub_x'; const B = \`rh.ebooks.calc.\${id}\`; // 'rookhub_comment'
      /* 'lh-block' */ selector: 'lh-card', localStorage.getItem("lh-club")`;
    expect(storageLiterals(src)).toEqual(['rookhub_x', 'rh.ebooks.calc.${', 'lh-club']);
  });

  it('jedes Speicher-Literal in den Quelltexten aller Oberflächen hat eine Einordnung', async () => {
    const urls = sourceUrls();
    expect(urls.length).withContext('Quelltexte nicht ausgeliefert (karma.conf.js files)').toBeGreaterThan(500);
    const missing = new Set<string>();
    for (const url of urls) {
      const res = await fetch(url);
      for (const lit of storageLiterals(await res.text())) {
        const head = lit.endsWith('${') ? lit.slice(0, -2) : null;
        const entry = head !== null ? classifyStorageKey(head + 'x') : classifyStorageKey(lit);
        if (!entry || (head !== null && !entry.prefix)) missing.add(`${lit}  (${url.replace('/base/', '')})`);
      }
    }
    expect([...missing]).withContext('neuer Schlüssel: in core/local-storage-keys.ts einordnen').toEqual([]);
  });

  it('kein Gerätewert liegt unter einem Präfix, das beim Abmelden geräumt wird', () => {
    const userPrefixes = STORAGE_KEYS.filter(e => e.cls === 'user' && e.prefix).map(e => e.key);
    const shadowed = STORAGE_KEYS.filter(e => e.cls !== 'user' && userPrefixes.some(p => e.key.startsWith(p) && e.key !== p))
      .map(e => e.key);
    expect(shadowed).toEqual([]);
    const dup = STORAGE_KEYS.map(e => e.key).filter((k, i, all) => all.indexOf(k) !== i);
    expect(dup).toEqual([]);
  });

  // Handgespiegelt mit LITERALEN Werten (Konvention „Handgespiegelte Listen“): die im Fund genannten Spuren gehen,
  // die Gerätevorlieben bleiben.
  it('Einordnung der benannten Schlüssel', () => {
    for (const k of ['rookhub_dashboard_cache_v1_u7', 'rookhub_discord_link', 'rookhub_guess_session', 'rookhub_puzzle_session',
      'rh-kids-endless-v1', 'rh-kids-progress-v1', 'rh-kids-stars-v1', 'rookhub_endless_history', 'rh.turnier.directoryView.7',
      'rookhub_daily_elapsed', 'rh.ebooks.calc.12', 'lh-club'])
      expect(clearedOnLogout(k)).withContext(k).toBeTrue();
    for (const k of ['rookhub_lang', 'rookhub_board_theme', 'rookhub_piece_set', 'rookhub_theme', 'rookhub_theme_mode',
      'rookhub_ui_scale', 'rookhub_eink', 'rh.ebooks.panelWidth', 'rookhub_analysis_engine', 'rh-kids-stars-free'])
      expect(clearedOnLogout(k)).withContext(k).toBeFalse();
    expect(classifyStorageKey('rookhub_user')?.cls).toBe('auth');
    expect(classifyStorageKey('rookhub_offline_queue')?.cls).toBe('queue');
    expect(classifyStorageKey('rookhub_unbekannt')).toBeNull();
  });

  describe('OfflineService.clearOnLogout', () => {
    beforeEach(() => { localStorage.clear(); TestBed.configureTestingModule({}); });
    afterEach(() => localStorage.clear());

    it('räumt jeden Schlüssel der Klasse user und lässt alle anderen stehen', () => {
      const concrete = (k: string, prefix?: boolean) => (prefix ? `${k}probe` : k);
      for (const e of STORAGE_KEYS) localStorage.setItem(concrete(e.key, e.prefix), '1');
      TestBed.inject(OfflineService).clearOnLogout();
      for (const e of STORAGE_KEYS) {
        const k = concrete(e.key, e.prefix);
        const want = clearedOnLogout(k) ? null : '1';
        expect(localStorage.getItem(k)).withContext(`${k} (${e.cls})`).toBe(want);
      }
    });
  });
});
