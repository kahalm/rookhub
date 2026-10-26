import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { LibraryDialogComponent } from './library-dialog.component';
import { LibraryGame } from './library.service';

function game(over: Partial<LibraryGame> = {}): LibraryGame {
  return {
    id: 1, white: 'Anderssen', black: 'Kieseritzky', whiteElo: null, blackElo: null,
    result: '1-0', event: 'London', playedOn: '1851-06-21', eco: 'C33', plyCount: 46,
    annotator: 'Aagaard,Jacob', commentedPlies: 18, commentChars: 2400, languages: 'en',
    score: 88, sourceTitle: 'CBM 104', inPool: false, requested: false, gameAnalysisId: null, ...over,
  };
}

/**
 * Der Rohbestand als Nachschlagewerk. Geprüft wird vor allem, was die Menge erzwingt: gesucht wird
 * am SERVER, getippt wird gedrosselt, und eine Partie, die schon spielbar ist, wird nicht noch
 * einmal angefordert.
 */
describe('LibraryDialogComponent', () => {
  let http: HttpTestingController;
  let closed: number | undefined;

  beforeEach(() => {
    closed = undefined;
    TestBed.configureTestingModule({
      imports: [LibraryDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: (v?: number) => { closed = v; } } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  function open(items: LibraryGame[] = [game()], total = items.length, semantic = { available: false, indexed: 0 }) {
    const fixture = TestBed.createComponent(LibraryDialogComponent);
    fixture.detectChanges();
    http.expectOne(r => r.url === '/api/library-games').flush({ items, total, page: 1, pageSize: 50 });
    // „Frag die Kommentare" (0.536.0): beim Öffnen einmal fragen, ob es die Suche gibt.
    http.expectOne(r => r.url === '/api/library-games/semantic' && !r.params.has('q')).flush({ ...semantic, items: [] });
    fixture.detectChanges();
    return fixture;
  }

  it('Frag die Kommentare: Reiter nur mit Modell UND eingebettetem Bestand', () => {
    const el = open().nativeElement as HTMLElement;
    expect(el.querySelector('.modes')).toBeNull();
  });

  it('Frag die Kommentare: gedrosselte Frage, Treffer mit Auszug, Spielen/Anfordern wie in der Namenssuche', fakeAsync(() => {
    const fixture = open([game()], 1, { available: true, indexed: 1200 });
    const el = fixture.nativeElement as HTMLElement;
    const modes = el.querySelectorAll('.modes button');
    expect(modes.length).toBe(2);
    (modes[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.textContent).toContain('guess.library.askEmpty');

    const c = fixture.componentInstance;
    c.ask = 'bishop sacrifice on h7';
    c.asked.next();
    tick(599);
    http.expectNone(r => r.url === '/api/library-games/semantic' && r.params.has('q'));
    tick(1);
    const req = http.expectOne(r => r.url === '/api/library-games/semantic' && r.params.get('q') === 'bishop sacrifice on h7');
    req.flush({
      available: true, indexed: 1200,
      items: [{ game: game({ id: 7, white: 'Tal', inPool: true, gameAnalysisId: 70 }),
                matches: [{ fromPly: 22, text: '12. Bxh7+: The classic sacrifice.', score: 0.81 }] }],
    });
    fixture.detectChanges();
    expect(el.querySelector('.snippet')!.textContent).toContain('The classic sacrifice.');
    (Array.from(el.querySelectorAll('button')).find(b => b.textContent!.includes('guess.play')) as HTMLButtonElement).click();
    expect(closed).toBe(70);
  }));

  it('sucht am Server und blaettert dort', () => {
    const fixture = open([game()], 120);
    expect(fixture.componentInstance.pages).toBe(3);

    fixture.componentInstance.reload(2);
    const req = http.expectOne(r => r.url === '/api/library-games');
    expect(req.request.params.get('page')).toBe('2');
    req.flush({ items: [], total: 120, page: 2, pageSize: 50 });
  });

  // Codereview F4-010: eine langsame ältere Antwort darf die neuere nicht überschreiben.
  it('Namenssuche: die neuere Anfrage gewinnt, auch wenn die ältere zuletzt antwortet', () => {
    const fixture = open([game()], 120);
    const c = fixture.componentInstance;
    c.reload(2);
    c.reload(3);
    const reqs = http.match(r => r.url === '/api/library-games');
    expect(reqs.length).toBe(2);
    reqs[1].flush({ items: [game({ id: 3, white: 'Neu' })], total: 120, page: 3, pageSize: 50 });
    if (!reqs[0].cancelled) reqs[0].flush({ items: [game({ id: 2, white: 'Alt' })], total: 120, page: 2, pageSize: 50 });
    expect(c.items.map(g => g.id)).toEqual([3]);
    expect(c.page).toBe(3);
    expect(c.loading).toBeFalse();
  });

  it('Frag die Kommentare: die neuere Frage gewinnt, eine verkürzte Frage verwirft die laufende', () => {
    const fixture = open([game()], 1, { available: true, indexed: 1200 });
    const c = fixture.componentInstance;
    const hit = (id: number) => ({ game: game({ id }), matches: [] });
    c.ask = 'erste frage'; c.runAsk();
    c.ask = 'zweite frage'; c.runAsk();
    const reqs = http.match(r => r.url === '/api/library-games/semantic' && r.params.has('q'));
    expect(reqs.length).toBe(2);
    reqs[1].flush({ available: true, indexed: 1200, items: [hit(2)] });
    if (!reqs[0].cancelled) reqs[0].flush({ available: true, indexed: 1200, items: [hit(1)] });
    expect(c.hits.map(h => h.game.id)).toEqual([2]);

    c.ask = 'dritte frage'; c.runAsk();
    const third = http.expectOne(r => r.url === '/api/library-games/semantic' && r.params.get('q') === 'dritte frage');
    c.ask = 'x'; c.runAsk();
    if (!third.cancelled) third.flush({ available: true, indexed: 1200, items: [hit(3)] });
    expect(c.hits).toEqual([]);
    expect(c.asking).toBeFalse();
  });

  /** Ein Umlauf je Buchstabe waere bei 130 000 Zeilen das Gegenteil von schnell. */
  it('drosselt das Tippen', fakeAsync(() => {
    const fixture = open();

    fixture.componentInstance.query = 'Cap';
    fixture.componentInstance.typed.next();
    fixture.componentInstance.query = 'Capablanca';
    fixture.componentInstance.typed.next();
    http.expectNone(r => r.url === '/api/library-games');   // noch nichts

    tick(400);
    const req = http.expectOne(r => r.url === '/api/library-games');
    expect(req.request.params.get('q')).toBe('Capablanca');
    req.flush({ items: [], total: 0, page: 1, pageSize: 50 });
  }));

  it('gibt Sprache und Kommentardichte als Filter mit', () => {
    const fixture = open();

    fixture.componentInstance.language = 'de';
    fixture.componentInstance.minCommentedPlies = 20;
    fixture.componentInstance.reload(1);

    const req = http.expectOne(r => r.url === '/api/library-games');
    expect(req.request.params.get('language')).toBe('de');
    expect(req.request.params.get('minCommentedPlies')).toBe('20');
    req.flush({ items: [], total: 0, page: 1, pageSize: 50 });
  });

  it('fordert eine Partie an und merkt sich das an der Zeile', () => {
    const fixture = open();
    const row = fixture.componentInstance.items[0];

    fixture.componentInstance.request(row);
    http.expectOne('/api/library-games/1/request')
      .flush({ analysis: { id: 42 }, alreadyPlayable: false });

    expect(row.requested).toBeTrue();
    expect(row.gameAnalysisId).toBe(42);
    expect(fixture.componentInstance.busy).toBeNull();
  });

  it('gibt den Knopf nach einer Absage wieder frei', () => {
    const fixture = open();

    fixture.componentInstance.request(fixture.componentInstance.items[0]);
    http.expectOne('/api/library-games/1/request')
      .flush({ reason: 'too-many-open' }, { status: 400, statusText: 'Bad Request' });

    expect(fixture.componentInstance.busy).toBeNull();
    expect(fixture.componentInstance.items[0].requested).toBeFalse();
  });

  /** Eine schon spielbare Partie wird nicht angefordert, sondern geoeffnet — der Dialog gibt dem
   *  Aufrufer die Analyse-Id zurueck und kennt die Route selbst nicht. */
  it('schliesst mit der Analyse-Id, wenn die Partie schon spielbar ist', () => {
    const fixture = open([game({ inPool: true, gameAnalysisId: 7 })]);

    fixture.componentInstance.play(fixture.componentInstance.items[0]);

    expect(closed).toBe(7);
  });

  it('zeigt Namen und Randdaten zusammengesetzt', () => {
    const fixture = open();
    const row = fixture.componentInstance.items[0];

    expect(fixture.componentInstance.names(row)).toBe('Anderssen – Kieseritzky');
    const meta = fixture.componentInstance.meta(row);
    expect(meta).toContain('London');
    expect(meta).toContain('1851');
    expect(meta).toContain('Aagaard,Jacob');
  });
});
