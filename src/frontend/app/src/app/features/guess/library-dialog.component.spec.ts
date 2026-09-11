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

  function open(items: LibraryGame[] = [game()], total = items.length) {
    const fixture = TestBed.createComponent(LibraryDialogComponent);
    fixture.detectChanges();
    http.expectOne(r => r.url === '/api/library-games').flush({ items, total, page: 1, pageSize: 25 });
    return fixture;
  }

  it('sucht am Server und blaettert dort', () => {
    const fixture = open([game()], 60);
    expect(fixture.componentInstance.pages).toBe(3);

    fixture.componentInstance.reload(2);
    const req = http.expectOne(r => r.url === '/api/library-games');
    expect(req.request.params.get('page')).toBe('2');
    req.flush({ items: [], total: 60, page: 2, pageSize: 25 });
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
    req.flush({ items: [], total: 0, page: 1, pageSize: 25 });
  }));

  it('gibt Sprache und Kommentardichte als Filter mit', () => {
    const fixture = open();

    fixture.componentInstance.language = 'de';
    fixture.componentInstance.minCommentedPlies = 20;
    fixture.componentInstance.reload(1);

    const req = http.expectOne(r => r.url === '/api/library-games');
    expect(req.request.params.get('language')).toBe('de');
    expect(req.request.params.get('minCommentedPlies')).toBe('20');
    req.flush({ items: [], total: 0, page: 1, pageSize: 25 });
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
