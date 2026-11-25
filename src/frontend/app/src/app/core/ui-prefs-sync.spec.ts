import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { OfflineService } from './offline.service';
import { PreferencesService } from './preferences.service';
import { STORAGE_KEYS } from './local-storage-keys';
import {
  SNAPSHOT_KEY, SYNCED_UI_PREF_KEYS, UI_PREFS_APPLIED_EVENT, collectUiPrefChanges, commitUiPrefChanges,
  mergeUiPrefsFromProfile,
} from './ui-prefs-sync';
import { flushBeforeLogout } from './logout-flush';

/** Anzeige-Vorlieben im Profil (1.018.0) — vorher nur am Gerät, konto-gebundene gingen beim Abmelden verloren. */
describe('ui-prefs-sync', () => {
  const clear = () => { for (const k of [...SYNCED_UI_PREF_KEYS, SNAPSHOT_KEY]) localStorage.removeItem(k); };
  beforeEach(clear);
  afterEach(clear);

  it('Whitelist wie am Server (UiPrefKeysTests, LITERALE Werte)', () => {
    expect([...SYNCED_UI_PREF_KEYS].sort()).toEqual([
      'rookhub_puzzle_config', 'rookhub_visualization', 'rookhub_viz_arrow', 'rookhub_offpath_warn_moves',
      'rookhub_en_passant_forced', 'rookhub_dashboard_layout_v2', 'rookhub_notifications_hidden_categories',
      'rookhub_guess_accept', 'rookhub_guess_comment_lang', 'rookhub_explorer_settings',
      'rookhub_rep_train_show_comments', 'rookhub_rep_train_review_engine', 'rookhub_deviation_threshold',
      'rookhub_game_arrow', 'rookhub_game_badges', 'rookhub_game_engine_view', 'rookhub_game_lines',
      'rookhub_analysis_compare', 'rookhub_analysis_depth', 'rookhub_analysis_lines', 'rookhub_analysis_maia_elo',
      'rookhub_analysis_maia_evalbar', 'rookhub_analysis_maia_warn', 'rh.ebooks.solveStyle', 'rh.ebooks.autoNext',
      'rh.repertoire.treeArrows', 'rookhub_live_eval', 'rh.turnier.historyView', 'lh-club', 'lh-training-lines',
      'lh-tree-filter', 'lh-auto-my-games', 'lh-all-leagues', 'lh-overview-newest-first',
    ].sort());
  });

  it('jeder synchronisierte Schlüssel ist eingeordnet, das Abbild als Konto-Spur', () => {
    const cls = (k: string) => STORAGE_KEYS.find(e => e.prefix ? k.startsWith(e.key) : e.key === k)?.cls;
    for (const k of SYNCED_UI_PREF_KEYS) expect(cls(k)).withContext(k).toMatch(/^(user|device)$/);
    expect(cls(SNAPSHOT_KEY)).toBe('user');
  });

  it('Profil gewinnt, bei Konflikt der jüngere; Gerätewerte ohne Abgleich gehen mit at=1 hoch', () => {
    localStorage.setItem('rookhub_visualization', '3');
    localStorage.setItem('lh-club', '5');
    const seen: string[][] = [];
    const on = (e: Event) => seen.push((e as CustomEvent<string[]>).detail);
    window.addEventListener(UI_PREFS_APPLIED_EVENT, on);
    const r = mergeUiPrefsFromProfile({ 'lh-club': { v: '9', at: 50 }, 'rookhub_live_eval': { v: 'false', at: 40 } }, 1000);
    window.removeEventListener(UI_PREFS_APPLIED_EVENT, on);
    expect(localStorage.getItem('lh-club')).toBe('9');
    expect(localStorage.getItem('rookhub_live_eval')).toBe('false');
    expect(r.upload).toEqual({ rookhub_visualization: { v: '3', at: 1 } });
    expect(seen.length).toBe(1);
    expect(seen[0].sort()).toEqual(['lh-club', 'rookhub_live_eval']);
    // Danach am Gerät geändert → gilt als jünger und schlägt den Profilwert.
    localStorage.setItem('lh-club', '7');
    const r2 = mergeUiPrefsFromProfile({ 'lh-club': { v: '9', at: 50 } }, 2000);
    expect(localStorage.getItem('lh-club')).toBe('7');
    expect(r2.upload['lh-club']).toEqual({ v: '7', at: 2000 });
  });

  it('sammelt nur Änderungen seit dem Abgleich, das Abbild erst nach dem Hochladen', () => {
    mergeUiPrefsFromProfile({}, 10);
    expect(collectUiPrefChanges(20)).toEqual({});
    localStorage.setItem('rookhub_deviation_threshold', '80');
    const ch = collectUiPrefChanges(30);
    expect(ch).toEqual({ rookhub_deviation_threshold: { v: '80', at: 1 } });   // nie abgeglichen → at 1
    expect(collectUiPrefChanges(31)).toEqual(ch);                              // noch nicht übernommen
    commitUiPrefChanges(ch);
    expect(collectUiPrefChanges(40)).toEqual({});
    localStorage.setItem('rookhub_deviation_threshold', '120');
    expect(collectUiPrefChanges(50)).toEqual({ rookhub_deviation_threshold: { v: '120', at: 50 } });
    // Ungültiges aus dem Profil (unbekannter Schlüssel, zu lang) bleibt draußen.
    mergeUiPrefsFromProfile({ rookhub_user: { v: 'x', at: 99 }, 'lh-tree-filter': { v: 'x'.repeat(2001), at: 99 } }, 60);
    expect(localStorage.getItem('lh-tree-filter')).toBeNull();
  });

  describe('mit PreferencesService', () => {
    let auth: { isLoggedIn: boolean; currentUser: { userId: number } | null; token?: string };
    let http: HttpTestingController;
    let prefs: PreferencesService;

    beforeEach(() => {
      auth = { isLoggedIn: true, currentUser: { userId: 4 } };
      TestBed.configureTestingModule({
        providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: auth }],
      });
      http = TestBed.inject(HttpTestingController);
      prefs = TestBed.inject(PreferencesService);
    });
    afterEach(() => http.verify());

    it('neues Gerät: Werte aus dem Profil landen am Gerät, die Visualisierung gilt sofort', () => {
      prefs.loadFromServer();
      http.expectOne('/api/profile').flush({ uiPrefs: { rookhub_visualization: { v: '3', at: 5 }, 'lh-club': { v: '2', at: 5 } } });
      expect(prefs.visualization).toBe(3);
      expect(localStorage.getItem('lh-club')).toBe('2');
      http.expectNone(r => r.method === 'PUT');
    });

    it('Abmelden: geänderte Werte gehen mit dem Token der endenden Sitzung hoch, danach ist das Gerät geräumt', () => {
      prefs.loadFromServer();
      http.expectOne('/api/profile').flush({ uiPrefs: { 'lh-club': { v: '2', at: 5 } } });
      localStorage.setItem('lh-club', '8');
      flushBeforeLogout('tok-alt');
      const put = http.expectOne(r => r.method === 'PUT' && r.url === '/api/profile');
      expect(put.request.headers.get('Authorization')).toBe('Bearer tok-alt');
      expect((put.request.body as { uiPrefs: Record<string, { v: string }> }).uiPrefs['lh-club'].v).toBe('8');
      put.flush({});
      TestBed.inject(OfflineService).clearOnLogout();
      expect(localStorage.getItem('lh-club')).toBeNull();          // konto-gebunden: vom Gerät
      expect(localStorage.getItem(SNAPSHOT_KEY)).toBeNull();
      // Wieder angemeldet: kommt aus dem Profil zurück.
      prefs.loadFromServer();
      http.expectOne(r => r.method === 'GET').flush({ uiPrefs: { 'lh-club': { v: '8', at: Date.now() } } });
      expect(localStorage.getItem('lh-club')).toBe('8');
    });

    it('Gast: kein Hochladen', () => {
      auth.isLoggedIn = false; auth.currentUser = null;
      localStorage.setItem('rookhub_live_eval', 'true');
      prefs.flushUiPrefs();
      http.expectNone(() => true);
    });
  });
});
