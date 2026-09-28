import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { GameEditComponent } from './game-edit.component';

describe('GameEditComponent', () => {
  async function setup() {
    await TestBed.configureTestingModule({
      imports: [GameEditComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '5' }), data: {} } } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(GameEditComponent);
    return { fixture, c: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  const pgn = '[Event "Klub"]\n[Site "?"]\n[Date "2026.06.05"]\n[Round "2"]\n[White "A"]\n[Black "B"]\n[Result "*"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 *';
  const detail = (extra: object = {}) => ({
    id: 5, source: 'lichess', white: 'A', black: 'B', result: '*', moveCount: 5, shareToken: 't',
    createdAt: '2026-09-25T00:00:00Z', pgn, ...extra,
  });

  it('an ordinary game: loads moves and headers, a board move replaces the move and keeps what stays legal', async () => {
    const { fixture, c, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/games/5').flush(detail());
    fixture.detectChanges();

    expect(c.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Nf3', 'Nc6', 'Bb5']);
    expect(c.header.event).toBe('Klub');
    expect(c.header.date).toBe('2026-06-05');
    expect(c.isScoresheet()).toBeFalse();
    // Meine Seite umstellen dreht das Brett gleich mit.
    c.header.ownerSide = 'black';
    c.onSide('black');
    expect(c.flipped()).toBeTrue();

    // 2. Nf3 → 2. Bc4: …Nc6 bleibt legal, und 3. Bb5 ist jetzt der Läufer von c4 — der Rest bleibt stehen.
    c.go(2);
    c.onBoardMove({ from: 'f1', to: 'c4', san: 'Bc4', fen: '' });
    expect(c.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Bc4', 'Nc6', 'Bb5']);
    expect(c.plies().map(p => p.illegal)).toEqual([false, false, false, false, false]);
    expect(c.cursor()).toBe(3);

    // 3. Bb5 wegnehmen und am Ende 3. Nf3 anhängen.
    c.go(4);
    c.remove();
    expect(c.plies().length).toBe(4);
    c.go(4);
    c.onBoardMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: '' });
    expect(c.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Bc4', 'Nc6', 'Nf3']);
  });

  // Gemeldet 2026-09-28: am Handy wanderte das Brett beim Durchklicken nach oben — scrollIntoView rollte die ganze Seite.
  it('stepping through the moves never scrolls the page, only the move list itself', async () => {
    const { fixture, c, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/games/5').flush(detail());
    fixture.detectChanges();
    const spy = spyOn(Element.prototype, 'scrollIntoView');
    for (const i of [1, 2, 3, 4, 0]) { c.go(i); fixture.detectChanges(); }
    await new Promise(r => setTimeout(r, 20));                                 // revealCursor läuft im nächsten Takt
    expect(spy).not.toHaveBeenCalled();
  });

  it('an ordinary game: an insert that breaks the rest marks it illegal, saving drops it after asking', async () => {
    const { fixture, c, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/games/5').flush(detail());
    c.go(0);
    c.mode.set('insert');
    c.onBoardMove({ from: 'd2', to: 'd4', san: 'd4', fen: '' });
    // Nach 1. d4 steht Weiß nicht mehr am Zug — „e4" ist jetzt Schwarz' Zug und geht nicht.
    expect(c.plies()[0].san).toBe('d4');
    expect(c.illegalCount()).toBe(5);
  });

  it('a scanned game: shows the sheet state and rebuilds the rest after choosing a reading', async () => {
    const { fixture, c, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/games/5').flush(detail({ source: 'scoresheet', scanId: 9 }));
    http.expectOne('/api/games/5/photo').flush(new Blob([new Uint8Array([1])], { type: 'image/jpeg' }));
    http.expectOne('/api/games/5/scoresheet').flush({
      scanId: 9, notationLanguage: 'de', written: ['e4', 'e5', 'Sf3', 'Sc6', 'Lb5'], unresolved: [], unresolvedFrom: null,
      plies: [
        { w: 0, written: 'e4', san: 'e4', uci: 'e2e4', match: 'written', uncertain: false },
        { w: 1, written: 'e5', san: 'e5', uci: 'e7e5', match: 'written', uncertain: false },
        { w: 2, written: 'Sf3', san: 'Nf3', uci: 'g1f3', match: 'fuzzy', uncertain: true, options: [
          { san: 'Nf3', uci: 'g1f3', match: 'fuzzy', reach: 2, preview: ['Nc6', 'Bb5'] },
          { san: 'Nc3', uci: 'b1c3', match: 'fuzzy', reach: 1, preview: ['Nc6'] },
        ] },
        { w: 3, written: 'Sc6', san: 'Nc6', uci: 'b8c6', match: 'written', uncertain: false },
        { w: 4, written: 'Lb5', san: 'Bb5', uci: 'f1b5', match: 'written', uncertain: false },
      ],
    });
    fixture.detectChanges();

    expect(c.isScoresheet()).toBeTrue();
    expect(c.cursor()).toBe(2); // springt auf die erste unsichere Stelle
    expect(c.uncertainLeft()).toBe(1);

    c.choose(c.current()!.options![1]);
    const req = http.expectOne({ method: 'POST', url: '/api/games/5/scoresheet/resolve' });
    expect(req.request.body).toEqual({ prefix: ['e4', 'e5', 'Nc3'], writtenFrom: 3 });
    req.flush({ plies: [{ w: 3, written: 'Sc6', san: 'Nc6', uci: 'b8c6', match: 'written', uncertain: false }],
      unresolved: ['Lb5'], unresolvedFrom: 4 });

    expect(c.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Nc3', 'Nc6']);
    expect(c.plies()[2].confirmed).toBeTrue();
    expect(c.unresolved()).toEqual(['Lb5']);
    expect(c.cursor()).toBe(3);

    // Speichern: Züge, Kopfdaten, Formular-Hinweis für den offenen Eintrag, Stand je Halbzug.
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    c.save();
    const put = http.expectOne({ method: 'PUT', url: '/api/games/5' });
    const body = put.request.body;
    expect(body.moves.map((m: { san: string }) => m.san)).toEqual(['e4', 'e5', 'Nc3', 'Nc6']);
    expect(body.moves[3].comment).toBe('sheet, not resolved: Lb5');
    expect(body.scoresheetPlies.length).toBe(4);
    expect(body.round).toBe('2');
    expect(body.ownerSide).toBe('');
    put.flush(detail());
    expect(router.navigate).toHaveBeenCalledWith(['/games', 5]);
  });

  it('a scanned game: confirming a move clears the mark', async () => {
    const { fixture, c, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/games/5').flush(detail({ source: 'scoresheet', scanId: 9 }));
    http.expectOne('/api/games/5/photo').flush(new Blob([new Uint8Array([1])]));
    http.expectOne('/api/games/5/scoresheet').flush({
      scanId: 9, notationLanguage: 'de', written: ['e4', 'e5', 'Sf3', 'Sc6', 'Lb5'], unresolved: [],
      plies: ['e4', 'e5', 'Nf3', 'Nc6', 'Bb5'].map((san, i) => ({
        w: i, written: san, san, uci: '', match: i === 1 ? 'fuzzy' : 'written', uncertain: i === 1,
      })),
    });
    expect(c.cursor()).toBe(1);
    c.confirm();
    expect(c.uncertainLeft()).toBe(0);
    expect(c.plies()[1].confirmed).toBeTrue();
  });

  it('a scanned game: shows the entry of the selected move cut out of the photo, and nothing without a box', async () => {
    const { fixture, c, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/games/5').flush(detail({ source: 'scoresheet', scanId: 9 }));
    http.expectOne('/api/games/5/photo').flush(new Blob([new Uint8Array([1])], { type: 'image/jpeg' }));
    http.expectOne('/api/games/5/scoresheet').flush({
      scanId: 9, notationLanguage: 'de', written: ['e4', 'e5', 'Sf3', 'Sc6', 'Lb5'], unresolved: [],
      boxes: [[100, 50, 180, 80], [400, 50, 470, 80], null, [400, 90, 480, 120], [100, 130, 190, 160]],
      plies: ['e4', 'e5', 'Nf3', 'Nc6', 'Bb5'].map((san, i) => ({
        w: i, written: san, san, uci: '', match: i === 1 ? 'fuzzy' : 'written', uncertain: i === 1,
      })),
    });
    // Das Foto hat im Test keine echten Pixel — die Maße meldet sonst das load-Ereignis.
    c.photoSize.set({ w: 1500, h: 2000 });
    fixture.detectChanges();

    expect(c.cursor()).toBe(1);
    expect(c.crop()?.written).toBe('e5');
    expect(c.crop()?.uncertain).toBeTrue();
    const frame = fixture.nativeElement.querySelector('.crop-frame') as HTMLElement;
    expect(frame).not.toBeNull();
    expect(frame.classList).toContain('uncertain');
    expect(frame.querySelector('.crop-mark')).not.toBeNull();

    c.go(2);                                   // Sf3: kein Kasten
    fixture.detectChanges();
    expect(c.crop()).toBeNull();
    expect(fixture.nativeElement.querySelector('.crop-frame')).toBeNull();

    c.go(3);                                   // Sc6: Kasten, sicher gelesen
    expect(c.crop()?.written).toBe('Sc6');
    expect(c.crop()?.uncertain).toBeFalse();
  });

  // Gewünscht 2026-09-27: nach dem Bestätigen/Wählen einer Lesart gleich zur nächsten unsicheren Stelle.
  it('a scanned game: choosing a reading jumps to the next uncertain move, clicking the chosen one just confirms', async () => {
    const { fixture, c, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/games/5').flush(detail({ source: 'scoresheet', scanId: 9 }));
    http.expectOne('/api/games/5/photo').flush(new Blob([new Uint8Array([1])], { type: 'image/jpeg' }));
    const opts = [
      { san: 'Nf3', uci: 'g1f3', match: 'fuzzy', reach: 2, preview: [] },
      { san: 'Nc3', uci: 'b1c3', match: 'fuzzy', reach: 1, preview: [] },
    ];
    http.expectOne('/api/games/5/scoresheet').flush({
      scanId: 9, notationLanguage: 'de', written: ['e4', 'e5', 'Sf3', 'Sc6', 'Lb5'], unresolved: [], unresolvedFrom: null,
      plies: [
        { w: 0, written: 'e4', san: 'e4', uci: 'e2e4', match: 'written', uncertain: false },
        { w: 1, written: 'e5', san: 'e5', uci: 'e7e5', match: 'written', uncertain: true },
        { w: 2, written: 'Sf3', san: 'Nf3', uci: 'g1f3', match: 'fuzzy', uncertain: true, options: opts },
        { w: 3, written: 'Sc6', san: 'Nc6', uci: 'b8c6', match: 'written', uncertain: false },
        { w: 4, written: 'Lb5', san: 'Bb5', uci: 'f1b5', match: 'fuzzy', uncertain: true },
      ],
    });
    fixture.detectChanges();
    expect(c.cursor()).toBe(1);

    c.go(2);
    c.choose(opts[0]);                                     // die schon gewählte Lesart = passt so
    http.expectNone('/api/games/5/scoresheet/resolve');
    expect(c.plies()[2].confirmed).toBeTrue();
    expect(c.cursor()).toBe(4);                            // nächste offene nach vorn

    c.go(2);
    c.choose(opts[1]);                                     // eine andere Lesart → Rest neu aufbereitet
    http.expectOne({ method: 'POST', url: '/api/games/5/scoresheet/resolve' }).flush({
      plies: [
        { w: 3, written: 'Sc6', san: 'Nc6', uci: 'b8c6', match: 'written', uncertain: false },
        { w: 4, written: 'Lb5', san: 'Bb5', uci: 'f1b5', match: 'fuzzy', uncertain: true },
      ],
      unresolved: [], unresolvedFrom: null,
    });
    expect(c.plies()[2].san).toBe('Nc3');
    expect(c.cursor()).toBe(4);                            // nicht bloß 3 (der nächste Halbzug)
  });

  // Gemeldet 2026-09-27 (Prod-Partie 27, Zug 36): ein vergessener Zug, vom Auflöser eingefügt. Etwas an ihm zu tun, las
  // den Rest einen Eintrag zu spät weiter — der nächste Eintrag fiel weg und die Partie verrutschte.
  it('a scanned game: redoing an inserted move keeps the next sheet entry, and the move stays without one', async () => {
    const { fixture, c, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/games/5').flush(detail({ source: 'scoresheet', scanId: 9 }));
    http.expectOne('/api/games/5/photo').flush(new Blob([new Uint8Array([1])], { type: 'image/jpeg' }));
    http.expectOne('/api/games/5/scoresheet').flush({
      scanId: 9, notationLanguage: 'en', written: ['e4', 'e5', 'Nf3', 'Bb5'], unresolved: [], unresolvedFrom: null,
      plies: [
        { w: 0, written: 'e4', san: 'e4', uci: 'e2e4', match: 'written', uncertain: false },
        { w: 1, written: 'e5', san: 'e5', uci: 'e7e5', match: 'written', uncertain: false },
        { w: 2, written: 'Nf3', san: 'Nf3', uci: 'g1f3', match: 'written', uncertain: false },
        { w: null, written: '', san: 'Nc6', uci: 'b8c6', match: 'inserted', uncertain: true },
        { w: 3, written: 'Bb5', san: 'Bb5', uci: 'f1b5', match: 'written', uncertain: false },
      ],
    });
    fixture.detectChanges();
    expect(c.cursor()).toBe(3);

    c.onBoardMove({ from: 'b8', to: 'c6', san: 'Nc6', fen: '' });
    const req = http.expectOne({ method: 'POST', url: '/api/games/5/scoresheet/resolve' });
    expect(req.request.body).toEqual({ prefix: ['e4', 'e5', 'Nf3', 'Nc6'], writtenFrom: 3 });   // vorher 4
    req.flush({ plies: [{ w: 3, written: 'Bb5', san: 'Bb5', uci: 'f1b5', match: 'written', uncertain: false }],
      unresolved: [], unresolvedFrom: null });
    expect(c.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Nf3', 'Nc6', 'Bb5']);
    expect(c.plies()[3].w).toBeNull();
  });
});
