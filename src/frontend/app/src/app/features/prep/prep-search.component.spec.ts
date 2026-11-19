import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { PrepApiService } from './prep-api.service';
import { PREP_SEARCH_DELAY_MS, PrepSearchComponent } from './prep-search.component';
import { PrepHistoryEntry, PrepHit } from './prep.models';
import { clickCenter, clickFreeSpot, expectWholeTileClickable, recordNavigation } from '../../testing/tile-hit';

const HITS: PrepHit[] = [
  { id: 31252, name: 'Carlsen, Magnus', fide: '1503014', games: 8530, firstYear: 2001, lastYear: 2026, maxElo: 2882 },
  { id: 9, name: 'Carlsen, Henrik', fide: null, games: 12, firstYear: 2004, lastYear: 2004, maxElo: null },
];

/** Die drei Formen einer früheren Vorbereitung: über die Bestands-Id, über die FIDE-ID (ohne Farbe/Elo) und nur mit Namen. */
const HISTORY: PrepHistoryEntry[] = [
  { playerId: 154, name: 'Schmid, Franz', fide: '990001', games: 1234, firstYear: 2001, lastYear: 2026, maxElo: 2234,
    repertoireId: 308, repertoireName: 'Prep: Schmid, Franz 2026', preparedAt: '2026-11-18T19:30:00Z', color: 'b', query: 'Schmid, Franz' },
  { playerId: 77, name: 'Wegscheider, Olaf', fide: '990010', games: 12, firstYear: 2019, lastYear: 2019, maxElo: null,
    repertoireId: 306, repertoireName: 'Prep: Wegscheider, Olaf 2026', preparedAt: '2026-11-10T08:00:00Z', color: 'w', query: 'Wegscheider, Olaf' },
  { playerId: null, name: 'Moser, Christian Mag.', fide: null, games: null, firstYear: null, lastYear: null, maxElo: null,
    repertoireId: 295, repertoireName: 'Prep: Moser, Christian Mag. 2026', preparedAt: '2026-10-30T10:00:00Z', color: null, query: 'Moser, Christian' },
];

describe('PrepSearchComponent', () => {
  let fixture: ComponentFixture<PrepSearchComponent>;
  let api: { search: jasmine.Spy; history: jasmine.Spy; lastQuery: WritableSignal<string> };
  const el = () => fixture.nativeElement as HTMLElement;

  /** Baut die Seite; `history` = Antwort von `/api/prep/history` (Vorgabe: keine Vorbereitungen). */
  async function create(url = '/prep', history: PrepHistoryEntry[] | Error = []): Promise<void> {
    api = { search: jasmine.createSpy('search').and.resolveTo(HITS), history: jasmine.createSpy('history'), lastQuery: signal('') };
    if (history instanceof Error) api.history.and.rejectWith(history); else api.history.and.resolveTo(history);
    TestBed.configureTestingModule({
      imports: [PrepSearchComponent],
      providers: [provideRouter([{ path: 'prep', component: PrepSearchComponent }, { path: 'prep/:id', children: [] },
        { path: 'repertoires/:id', children: [] }]),
        provideTranslateService({ fallbackLang: 'en' }), provideNoopAnimations(), { provide: PrepApiService, useValue: api }],
    });
    await TestBed.inject(Router).navigateByUrl(url);
    fixture = TestBed.createComponent(PrepSearchComponent);
    fixture.detectChanges();
  }

  function type(value: string): void {
    const input = el().querySelector('input')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('sucht nach einer kurzen Pause, nicht bei jedem Tastendruck, und erst ab zwei Zeichen', fakeAsync(() => {
    void create();
    tick();
    type('C');
    tick(PREP_SEARCH_DELAY_MS);
    expect(api.search).not.toHaveBeenCalled();
    type('Ca');
    tick(100);
    type('Carlsen');
    tick(PREP_SEARCH_DELAY_MS);
    expect(api.search).toHaveBeenCalledOnceWith('Carlsen');
    tick();
    fixture.detectChanges();
    const links = Array.from(el().querySelectorAll<HTMLAnchorElement>('.hits a'));
    expect(links.length).toBe(2);
    expect(links[0].getAttribute('href')).toBe('/prep/31252');
    expect(links[0].textContent).toContain('Carlsen, Magnus');
    expect(links[0].textContent).toContain('FIDE\u00a01503014');                 // bricht nicht zwischen FIDE und Nummer
    expect(links[0].textContent).toContain('2001–2026');
    expect(links[1].textContent).not.toContain('FIDE 1');
    expect(links[1].textContent).toContain('prep.noFide');              // ohne FIDE-ID sagt es das
    expect(api.lastQuery()).toBe('Carlsen');
  }));

  it('die Suche steht in der Adresse und läuft beim Zurückkommen wieder', fakeAsync(() => {
    void create('/prep?q=Hoecher');
    tick();
    expect(api.search).toHaveBeenCalledOnceWith('Hoecher');
    tick();
    fixture.detectChanges();
    expect(el().querySelector('input')!.value).toBe('Hoecher');
  }));

  it('keine Treffer bzw. Fehler sagt es', fakeAsync(() => {
    void create();
    tick();
    api.search.and.resolveTo([]);
    type('Zzyzx');
    tick(PREP_SEARCH_DELAY_MS);
    tick();
    fixture.detectChanges();
    expect(el().textContent).toContain('prep.noHits');
    api.search.and.rejectWith(new Error('500'));
    type('Zzyzxx');
    tick(PREP_SEARCH_DELAY_MS);
    tick();
    fixture.detectChanges();
    expect(el().querySelector('.err')).not.toBeNull();
    expect(el().querySelector('.hits')).toBeNull();
  }));

  // ── Frühere Vorbereitungen (0.995.0) ──
  // Angular-22-Falle: nach der Antwort KEIN detectChanges — die Liste muss über Signale von selbst erscheinen.
  describe('frühere Vorbereitungen', () => {
    const rows = () => Array.from(el().querySelectorAll<HTMLLIElement>('.history-list li'));

    async function open(history: PrepHistoryEntry[] | Error = HISTORY, url = '/prep'): Promise<void> {
      await create(url, history);
      fixture.autoDetectChanges();
      await fixture.whenStable();
    }

    // Zeile mit zweitem Ziel (DESIGN 5.3) über den Baustein aus 5.2: die ganze Zeile trifft ihr Hauptziel, der
    // Repertoire-Knopf daneben sich selbst.
    it('jede Zeile ist als Ganzes klickbar; der Repertoire-Knopf bleibt eigenes Ziel', async () => {
      await open();
      const went = recordNavigation();
      for (const li of rows()) expectWholeTileClickable(li, li.querySelector('.main')!);
      const first = rows()[0];
      clickFreeSpot(first, first.querySelector('.main')!);
      clickCenter(first.querySelector('a.rep')!);
      expect(went()).toEqual(['/prep/154', '/repertoires/' + HISTORY[0].repertoireId]);
    });

    it('stehen unter dem leeren Suchfeld: ein zugeordneter Gegner ist ein Link auf seine Spielerseite, mit Datum, Farbe und Kennzahlen', async () => {
      await create('/prep', HISTORY);
      const translate = TestBed.inject(TranslateService);
      translate.setTranslation('en', { prep: { history: { preparedOn: 'Prepared on {{date}}' } } });
      translate.use('en');
      fixture.autoDetectChanges();
      await fixture.whenStable();

      expect(api.history).toHaveBeenCalledTimes(1);
      expect(el().querySelector('.history h2')!.textContent).toContain('prep.history.title');
      expect(el().querySelector('.history')!.getAttribute('aria-labelledby')).toBe(el().querySelector('.history h2')!.id);
      expect(rows().length).toBe(3);
      const first = rows()[0].querySelector<HTMLAnchorElement>('a.main')!;
      expect(first.getAttribute('href')).toBe('/prep/154');
      expect(first.querySelector('.name')!.textContent).toBe('Schmid, Franz');
      const meta = Array.from(first.querySelectorAll('.meta > span')).map(x => x.textContent!.trim());
      // jede Angabe ein eigenes Kind (.rh-dots setzt die Punkte), kein Text-„·“ im Markup
      expect(meta).toEqual(['Prepared on Nov 18, 2026', 'prep.trainingRepertoire.asBlack', 'prep.games', '2001–2026', 'prep.maxElo']);
      expect(first.querySelector('.meta')!.classList).toContain('rh-dots');
      expect(first.textContent).not.toContain('·');
      // ohne Elo keine Elo-Angabe, ein einzelnes Jahr nicht als Spanne; MEINE Farbe Weiß
      const second = Array.from(rows()[1].querySelectorAll('a.main .meta > span')).map(x => x.textContent!.trim());
      expect(second).toEqual(['Prepared on Nov 10, 2026', 'prep.trainingRepertoire.asWhite', 'prep.games', '2019']);
      expect(rows()[1].querySelector('a.main')!.getAttribute('href')).toBe('/prep/77');
      expect(el().querySelectorAll('a.main .find').length).toBe(0);
    });

    it('ein nicht zugeordneter Eintrag ist ein Knopf mit „Spieler suchen“: füllt das Feld mit seinem Namen und sucht sofort', async () => {
      await open();
      const row = rows()[2];
      expect(row.querySelector('a.main')).toBeNull();                       // kein Link auf eine Spielerseite, die es nicht gibt
      const button = row.querySelector<HTMLButtonElement>('button.main')!;
      expect(button.type).toBe('button');
      expect(button.querySelector('.name')!.textContent).toBe('Moser, Christian Mag.');
      expect(button.querySelector('.find')!.textContent).toContain('prep.history.find');
      expect(button.textContent).not.toContain('prep.games');

      // Maus/Finger: detail 1 (HTMLElement.click() meldet 0 wie die Tastatur)
      button.dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 1 }));
      await fixture.whenStable();
      expect(api.search).toHaveBeenCalledOnceWith('Moser, Christian');      // ohne den Titel, sonst fände die Suche niemanden
      expect(el().querySelector('input')!.value).toBe('Moser, Christian');
      expect(api.lastQuery()).toBe('Moser, Christian');
      expect(TestBed.inject(Router).url).toBe('/prep?q=Moser,%20Christian');
      expect(el().querySelector('.history')).toBeNull();                    // die Treffer stehen jetzt an ihrer Stelle
      expect(el().querySelectorAll('.hits a').length).toBe(2);
      expect(document.activeElement).not.toBe(el().querySelector('input')); // am Handy soll die Tastatur nicht aufklappen
    });

    it('per Tastatur ausgelöst geht der Fokus ins Suchfeld statt mit dem Knopf zu verschwinden', async () => {
      await open();
      rows()[2].querySelector<HTMLButtonElement>('button.main')!.dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 0 }));
      await fixture.whenStable();
      expect(api.search).toHaveBeenCalledOnceWith('Moser, Christian');
      expect(document.activeElement).toBe(el().querySelector('input'));
    });

    it('je Zeile ein eigener Link zum Trainings-Repertoire — daneben, nicht in der Zeile verschachtelt', async () => {
      await open();
      for (const [i, id] of [308, 306, 295].entries()) {
        const li = rows()[i];
        const rep = li.querySelector<HTMLAnchorElement>('a.rep')!;
        expect(rep.getAttribute('href')).toBe(`/repertoires/${id}`);
        expect(rep.getAttribute('aria-label')).toBe('prep.history.openRepertoire');   // Name für Vorleser, zugleich Tooltip
        expect(rep.parentElement).toBe(li);
        expect(li.querySelector('.main a, .main button, a a, button a')).toBeNull();
        expect(Array.from(li.children).map(c => c.tagName)).toEqual([i === 2 ? 'BUTTON' : 'A', 'A']);
      }
    });

    it('weichen den Treffern, sobald gesucht wird, und kommen mit leerem Feld zurück', fakeAsync(() => {
      void create('/prep', HISTORY);
      tick();
      fixture.detectChanges();
      expect(rows().length).toBe(3);
      type('C');                                                            // zu kurz für eine Suche: die Liste bleibt
      fixture.detectChanges();
      expect(rows().length).toBe(3);
      type('Ca');                                                           // gleich weg, nicht erst nach der Antwort
      fixture.detectChanges();
      expect(el().querySelector('.history')).toBeNull();
      tick(PREP_SEARCH_DELAY_MS);
      tick();
      fixture.detectChanges();
      expect(el().querySelectorAll('.hits a').length).toBe(2);
      expect(el().querySelector('.history')).toBeNull();
      type('');
      tick(PREP_SEARCH_DELAY_MS);
      tick();
      fixture.detectChanges();
      expect(el().querySelector('.hits')).toBeNull();
      expect(rows().length).toBe(3);
      expect(api.history).toHaveBeenCalledTimes(1);                         // einmal geladen, nicht bei jedem Leeren neu
    }));

    it('kommt die Seite mit einer Suche in der Adresse, stehen die Treffer da und die Liste erst nach dem Leeren', async () => {
      await open(HISTORY, '/prep?q=Carlsen');
      expect(el().querySelectorAll('.hits a').length).toBe(2);
      expect(el().querySelector('.history')).toBeNull();
      expect(api.history).toHaveBeenCalledTimes(1);
    });

    it('ohne Vorbereitungen gibt es den Abschnitt nicht', async () => {
      await open([]);
      expect(api.history).toHaveBeenCalledTimes(1);
      expect(el().querySelector('.history')).toBeNull();
      expect(el().querySelector('app-load-error')).toBeNull();
    });

    it('ein Ladefehler sieht nicht aus wie „keine Vorbereitungen“: Meldung mit „Wiederholen“, das die Liste holt', async () => {
      await open(new Error('500'));
      expect(el().querySelector('.history h2')).not.toBeNull();
      const failed = el().querySelector('.history app-load-error')!;
      expect(failed.querySelector('[role="alert"]')).not.toBeNull();
      expect(el().querySelector('.history-list')).toBeNull();
      // die Suche bleibt benutzbar
      expect(el().querySelector('input')).not.toBeNull();

      api.history.and.resolveTo(HISTORY);
      failed.querySelector<HTMLButtonElement>('button.retry')!.click();
      await fixture.whenStable();
      expect(api.history).toHaveBeenCalledTimes(2);
      expect(el().querySelector('app-load-error')).toBeNull();
      expect(rows().length).toBe(3);
    });

    it('bleibt bei 390 px in der Seite: ein sehr langer Name bricht um, der Repertoire-Knopf bleibt daneben stehen', async () => {
      const long: PrepHistoryEntry = { ...HISTORY[0], playerId: 9, repertoireId: 400,
        name: 'Wolfeschlegelsteinhausenbergerdorffwelchevoralternwarengewissenhaftschaferswessenschafewarenwohlgepflege, Hubert Blaine' };
      const host = document.createElement('div');
      host.style.width = '390px';
      document.body.appendChild(host);
      try {
        await open([long, ...HISTORY]);
        host.appendChild(fixture.nativeElement);
        await fixture.whenStable();
        const page = el().querySelector<HTMLElement>('.prep')!;
        expect(page.scrollWidth).toBeLessThanOrEqual(page.clientWidth);
        const li = rows()[0].getBoundingClientRect();
        const rep = rows()[0].querySelector('a.rep')!.getBoundingClientRect();
        const name = rows()[0].querySelector('.name')!.getBoundingClientRect();
        expect(rep.right).toBeLessThanOrEqual(li.right + 0.5);
        expect(name.right).toBeLessThanOrEqual(rep.left + 0.5);                // der Name läuft nicht unter den Knopf
        expect(name.height).toBeGreaterThan(30);                              // … sondern bricht um
        expect(rep.width).toBeGreaterThanOrEqual(40);                         // Tippziel bleibt
        // der Hinweis unter dem Suchfeld bricht hier in zwei Zeilen: sein Platz wächst mit (DESIGN 5.8), statt dass die
        // zweite Zeile auf der Überschrift liegt
        expect(el().querySelector('.q .mat-mdc-form-field-subscript-dynamic-size')).not.toBeNull();
        const hint = el().querySelector('.q mat-hint')!.getBoundingClientRect();
        expect(el().querySelector('.history h2')!.getBoundingClientRect().top).toBeGreaterThanOrEqual(hint.bottom + 8);
      } finally {
        host.remove();
      }
    });
  });

  // b1-rk-h1-uneinheitlich: „Gegner vorbereiten" stand mit 26 px/500 kleiner und leichter als die übrigen Seitentitel.
  it('setzt keine eigene Titelschrift und lässt dem Erklärtext Luft', () => {
    const css = ((PrepSearchComponent as any).ɵcmp.styles as string[]).join('\n').replace(/\s/g, '');
    const h1 = /\.prep(\[[^\]]*\])?h1(\[[^\]]*\])?\{([^}]*)\}/.exec(css);
    expect(h1).withContext('.prep h1').not.toBeNull();
    expect(h1![3]).not.toMatch(/font/);
    expect(h1![3]).toContain('margin:4px012px');
  });
});
