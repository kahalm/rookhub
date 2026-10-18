import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { FlashcardsComponent } from './flashcards.component';
import { FlashcardBoardComponent } from './flashcard-board.component';
import { BoardFullscreenButtonComponent } from '../../../shared/fullscreen/board-fullscreen-button.component';
import { CourseService } from '../course.service';
import { RepertoireTrainingService } from '../../repertoire/repertoire-training.service';
import { PreferencesService } from '../../../core/preferences.service';
import { BookPuzzleDto } from '../../puzzles/puzzle.service';
import { lineKeyFromSans } from '../../repertoire/repertoire-line-key.util';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

function puzzle(id: number, over: Partial<BookPuzzleDto> = {}): BookPuzzleDto {
  return {
    id, lineId: `b.pgn:${id}`, bookFileName: 'b.pgn', round: String(id),
    fen: START, moves: 'e2e4', startPly: -1, ...over,
  } as BookPuzzleDto;
}

/** Welche Sprache die Karteikarten beim Laden verlangten (letzter make()-Aufruf). */
let requestedLangs: (string | null | undefined)[] = [];

function make(puzzles: BookPuzzleDto[], query: Record<string, string>, marks: number[] = []) {
  requestedLangs = [];
  TestBed.resetTestingModule();   // erlaubt mehrere make()-Aufrufe in einem it
  TestBed.configureTestingModule({
    imports: [FlashcardsComponent],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      { provide: CourseService, useValue: {
        getBookPuzzles: (_id: number, lang?: string | null) => { requestedLangs.push(lang); return of(puzzles); },
        getFlashcardMarks: () => of({ lineIds: marks }),
      } },
      { provide: RepertoireTrainingService, useValue: {
        getPgn: () => of(''),
        getFlashcardMarks: () => of({ lineKeys: [] }),
      } },
      { provide: PreferencesService, useValue: { pieceSet: 'cburnett' } },
      { provide: ActivatedRoute, useValue: { snapshot: {
        paramMap: {
          get: (k: string) => k === 'bookId' ? '58' : null,
          has: (k: string) => k === 'bookId',
        },
        queryParamMap: {
          get: (k: string) => k in query ? query[k] : null,
          has: (k: string) => k in query,
        },
      } } },
    ],
  });
  const fixture = TestBed.createComponent(FlashcardsComponent);
  fixture.detectChanges();
  return fixture;
}

describe('FlashcardsComponent', () => {
  it('lädt die Linien in der gewählten Sprache des Kurses (Kurs-Übersetzung)', () => {
    localStorage.setItem('rookhub_course_lang', JSON.stringify({ c: { b58: 'fr' }, f: {} }));
    try {
      const fixture = make([puzzle(1, { chapter: 'A', chapterLabel: 'Ä', title: 'T', titleLabel: 'Ü' })], {});
      expect(requestedLangs).toEqual(['fr']);
      expect(fixture.componentInstance.cards[0].heading).toBe('Ü');
      expect(fixture.componentInstance.cards[0].chapter).toBe('Ä');
    } finally {
      localStorage.removeItem('rookhub_course_lang');
    }
  });

  it('filtert per lines=…-Auswahl und baut 4er-Blätter', () => {
    const fixture = make([puzzle(1), puzzle(2), puzzle(3), puzzle(4), puzzle(5)], { lines: '1,3,5' });
    const c = fixture.componentInstance;
    expect(c.cards.length).toBe(3);
    expect(c.sheets.length).toBe(1);
    expect(c.sheets[0].fronts.map(f => f?.lineNo)).toEqual([1, 3, 5, undefined as never]);
  });

  it('Rückseiten-Blatt ist je Zeile spaltenvertauscht (Duplex an der langen Kante)', () => {
    const fixture = make([puzzle(1), puzzle(2), puzzle(3), puzzle(4)], {});
    const s = fixture.componentInstance.sheets[0];
    expect(s.fronts.map(f => f?.lineNo)).toEqual([1, 2, 3, 4]);
    expect(s.backs.map(b => b?.lineNo)).toEqual([2, 1, 4, 3]);
  });

  it('marked=1 zeigt nur die persistent markierten Linien (schlägt lines/chapter)', () => {
    const fixture = make(
      [puzzle(1), puzzle(2), puzzle(3)],
      { marked: '1', lines: '1,2,3', chapter: 'A' },
      [2],
    );
    const c = fixture.componentInstance;
    expect(c.markedOnly).toBeTrue();
    expect(c.cards.map(x => x.lineNo)).toEqual([2]);
  });

  it('marked=1 ohne Markierungen → leerer Stapel (eigener Leertext)', () => {
    const fixture = make([puzzle(1)], { marked: '1' }, []);
    expect(fixture.componentInstance.cards.length).toBe(0);
  });

  // UI-Sweep v1 (v1-rk-flashcards-marked-leer): Leerzustand nach DESIGN 5.10 — zentriert, ein Knopf ins
  // Durchsehen, kein Bedienhinweis und keine Misch-/Druck-Knöpfe ohne Karten.
  it('leerer Stapel: Knopf „Linien durchsehen", keine Werkzeuge, kein Bedienhinweis', () => {
    const fixture = make([puzzle(1)], { marked: '1' }, []);
    const el = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance.isEmpty).toBeTrue();
    const empty = el.querySelector('.empty-state');
    expect(empty).not.toBeNull();
    const links = empty!.querySelectorAll('a');
    expect(links.length).toBe(1);
    expect(links[0].getAttribute('href')).toBe('/courses/58/browse');
    expect(el.querySelector('.fc-hint')).toBeNull();
    expect(el.querySelectorAll('.fc-toolbar button').length).toBe(0);
  });

  it('mit Karten bleiben Bedienhinweis und Werkzeuge', () => {
    const fixture = make([puzzle(1), puzzle(2)], {});
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.empty-state')).toBeNull();
    expect(el.querySelector('.fc-hint')).not.toBeNull();
    expect(el.querySelectorAll('.fc-toolbar button').length).toBeGreaterThan(0);
  });

  it('filtert per chapter=… (leer = „ohne Kapitel")', () => {
    const fixture = make([
      puzzle(1, { chapter: 'A' }), puzzle(2, { chapter: 'B' }), puzzle(3),
    ], { chapter: 'A' });
    expect(fixture.componentInstance.cards.map(c => c.lineNo)).toEqual([1]);

    const fixture2 = make([puzzle(1, { chapter: 'A' }), puzzle(2)], { chapter: '' });
    expect(fixture2.componentInstance.cards.map(c => c.lineNo)).toEqual([2]);
  });

  it('rendert die Bretter als druckfestes SVG (Figuren als <image>, kein CSS-Hintergrund)', () => {
    const fixture = make([puzzle(1)], {});
    const svg: SVGElement | null = fixture.nativeElement.querySelector('app-flashcard-board svg');
    expect(svg).not.toBeNull();
    // 32 Figuren nach 1.e4 — als echte <image>-Knoten (Hintergrundbilder druckt der Browser nicht).
    expect(svg!.querySelectorAll('image').length).toBe(32);
    expect(svg!.querySelectorAll('rect').length).toBe(64);
    const href = svg!.querySelector('image')!.getAttribute('href');
    expect(href).toMatch(/^\/piece\/cburnett\/[wb][KQRBNP]\.svg$/);
  });
});

describe('FlashcardBoardComponent', () => {
  function board(inputs: Partial<FlashcardBoardComponent>) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [FlashcardBoardComponent] });
    const fixture = TestBed.createComponent(FlashcardBoardComponent);
    Object.assign(fixture.componentInstance, inputs);
    fixture.componentInstance.ngOnChanges();
    return fixture.componentInstance;
  }

  it('zeichnet Pfeile und Feld-Markierungen mit Ausrichtung', () => {
    const c = board({
      fen: '8/8/8/4k3/8/8/4K3/8 w - - 0 1',
      orientation: 'white',
      shapes: [{ orig: 'e2' as never, dest: 'e4' as never, brush: 'green' }, { orig: 'e5' as never, brush: 'red' }],
    });
    expect(c.arrows.length).toBe(1);
    expect(c.circles.length).toBe(1);
    expect(c.pieces.length).toBe(2);
    // e2 aus Weiß-Sicht: Spalte 4, Zeile 6 → Pfeil startet in deren Feldmitte-Nähe.
    expect(c.arrows[0].y1).toBeGreaterThan(c.arrows[0].y2);   // e2→e4 zeigt nach oben
  });

  it('spiegelt aus Schwarz-Sicht', () => {
    const white = board({ fen: '8/8/8/8/8/8/8/K7 w - - 0 1', orientation: 'white', shapes: [] });
    const black = board({ fen: '8/8/8/8/8/8/8/K7 w - - 0 1', orientation: 'black', shapes: [] });
    expect(white.pieces[0].x).toBe(0);
    expect(white.pieces[0].y).toBe(7);
    expect(black.pieces[0].x).toBe(7);
    expect(black.pieces[0].y).toBe(0);
  });
});

describe('FlashcardsComponent digitale Lern-Ansicht', () => {
  it('startet digital auf Karte 1, blättert zyklisch und dreht per flip() um', () => {
    const fixture = make([puzzle(1), puzzle(2), puzzle(3)], {});
    const c = fixture.componentInstance;
    expect(c.view).toBe('digital');
    expect(c.current!.lineNo).toBe(1);

    c.flip();
    expect(c.flipped).toBeTrue();
    c.next();                                   // Weiterblättern dreht zurück auf die Vorderseite
    expect(c.flipped).toBeFalse();
    expect(c.current!.lineNo).toBe(2);
    c.prev(); c.prev();
    expect(c.current!.lineNo).toBe(3);      // zyklisch
  });

  it('Flip ist animiert: beide Seiten gleichzeitig im DOM, Blättern schnappt ohne Animation zurück', () => {
    const fixture = make([puzzle(1), puzzle(2)], {});
    const c = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.fc-flip-front')).toBeTruthy();   // 3D-Flip braucht BEIDE Faces
    expect(el.querySelector('.fc-flip-back')).toBeTruthy();

    c.flip();
    fixture.detectChanges();
    expect(el.querySelector('.fc-flip')!.classList).toContain('fc-flip--flipped');

    c.next();                                   // Kartenwechsel: Vorderseite OHNE Rückwärts-Drehung
    fixture.detectChanges();
    expect(c.flipSnap).toBeTrue();
    expect(el.querySelector('.fc-flip')!.classList).toContain('fc-flip--noanim');
    expect(el.querySelector('.fc-flip')!.classList).not.toContain('fc-flip--flipped');
  });

  // a1-rk-flashcards-tastaturhinweis-handy / a1-rk-flashcards-ultrawide
  it('Tastaturhinweis trägt fc-hint--keys; Seite meldet die Digital-Ansicht (Breitbild nur dort)', () => {
    const fixture = make([puzzle(1)], {});
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.fc-hint.fc-hint--keys')).toBeTruthy();
    expect(el.querySelector('.fc-page')!.classList).toContain('fc-page--digital');
    fixture.componentInstance.view = 'print';
    fixture.detectChanges();
    expect(el.querySelector('.fc-hint--keys')).toBeNull();   // Duplex-Hinweis bleibt auch am Touch-Gerät
    expect(el.querySelector('.fc-page')!.classList).not.toContain('fc-page--digital');
  });

  it('CSS: Tastaturhinweis bei pointer: coarse aus; ab 2400 px Lernkarte bis Fensterhöhe (≤ 1 100 px)', () => {
    const css = ((FlashcardsComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles)
      .join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
    const coarse = css.indexOf('@media(pointer:coarse)');
    expect(coarse).toBeGreaterThan(-1);
    expect(css.slice(coarse)).toMatch(/^@media\(pointer:coarse\)\{\.fc-hint--keys(\[[^\]]*\])?\{display:none/);
    const at = css.indexOf('@media(min-width:2400px)');
    expect(at).toBeGreaterThan(-1);
    const wide = css.slice(at, css.indexOf('}}', at) + 2);
    expect(wide).toMatch(/\.fc-page--digital(\[[^\]]*\])?\{max-width:min\(var\(--page-max-width-wide,1880px\),96vw\)/);
    expect(wide).toMatch(/\.fc-study-card(\[[^\]]*\])?,\.fc-fs-btn(\[[^\]]*\])?\{width:min\((calc\()?100vh-320px\)?,1100px\)/);
    expect(wide).toMatch(/\.fc-flip-face(\[[^\]]*\])?\{min-height:min\((calc\()?100vh-320px\)?,1100px\)/);
    // Druckvorschau bleibt A4-breit
    expect(css).toMatch(/\.fc-page(\[[^\]]*\])?\{max-width:1000px/);
    expect(css.slice(0, at)).not.toContain('--page-max-width-wide');
  });

  it('Vollbild-Knopf zielt auf die Lern-Sektion (Karte + Leiste = Vollbild-Teilbaum)', () => {
    const fixture = make([puzzle(1)], {});
    const el: HTMLElement = fixture.nativeElement;
    const host = el.querySelector('.fc-study');
    const btn = fixture.debugElement.query(
      d => d.componentInstance instanceof BoardFullscreenButtonComponent);
    expect(host).toBeTruthy();
    expect(btn).toBeTruthy();
    expect((btn.componentInstance as BoardFullscreenButtonComponent).target).toBe(host as HTMLElement);
  });

  it('Mischen permutiert nur die Reihenfolge; ausschalten stellt die Kurs-Reihenfolge wieder her', () => {
    const fixture = make([puzzle(1), puzzle(2), puzzle(3), puzzle(4)], {});
    const c = fixture.componentInstance;
    c.toggleShuffle();
    expect(c.shuffled).toBeTrue();
    expect([...c.order].sort()).toEqual([0, 1, 2, 3]);   // Permutation, nichts verloren
    c.toggleShuffle();
    expect(c.order).toEqual([0, 1, 2, 3]);
    expect(c.pos).toBe(0);
  });

  it('Tastatur: ←/→ blättern, Leertaste dreht um — aber nicht in der Druckvorschau', () => {
    const fixture = make([puzzle(1), puzzle(2)], {});
    const c = fixture.componentInstance;
    c.onKey(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    expect(c.current!.lineNo).toBe(2);
    c.onKey(new KeyboardEvent('keydown', { key: ' ' }));
    expect(c.flipped).toBeTrue();

    c.view = 'print';
    c.onKey(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    expect(c.current!.lineNo).toBe(2);      // unverändert
  });

  it('rendert die Druck-Blätter auch im Digital-Modus (nur am Schirm versteckt) — Drucken klappt immer', () => {
    const fixture = make([puzzle(1)], {});
    const el: HTMLElement = fixture.nativeElement;
    expect(fixture.componentInstance.view).toBe('digital');
    const sheets = el.querySelector('.fc-sheets');
    expect(sheets).not.toBeNull();
    expect(sheets!.classList).toContain('fc-sheets--screen-hidden');
  });
});

describe('FlashcardsComponent Repertoire-Quelle', () => {
  const REP_PGN = `[Event "Kurs"]
[White "Hauptlinie"]
[Black "Kap 1"]
[Result "*"]

1. e4 e6 {[%cal Ge4e5] Franzose.} *

[Event "Kurs"]
[White "Nebenlinie"]
[Black "Kap 1"]
[Result "*"]

1. d4 d5 *`;

  function makeRep(query: Record<string, string>, pgn = REP_PGN, markedKeys: string[] = []) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [FlashcardsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: CourseService, useValue: { getBookPuzzles: () => of([]) } },
        { provide: RepertoireTrainingService, useValue: {
          getPgn: () => of(pgn),
          getFlashcardMarks: () => of({ lineKeys: markedKeys }),
        } },
        { provide: PreferencesService, useValue: { pieceSet: 'cburnett' } },
        { provide: ActivatedRoute, useValue: { snapshot: {
          paramMap: { get: (k: string) => k === 'id' ? '7' : null, has: (k: string) => k === 'id' },
          queryParamMap: { get: (k: string) => k in query ? query[k] : null, has: (k: string) => k in query },
        } } },
      ],
    });
    const fixture = TestBed.createComponent(FlashcardsComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('lädt Repertoire-Linien (umgekehrte Karten) und verlinkt zurück zum Repertoire', () => {
    const fixture = makeRep({});
    const c = fixture.componentInstance;
    expect(c.source).toBe('repertoire');
    expect(c.backLink).toEqual(['/repertoires', 7]);
    expect(c.cards.map(x => x.heading)).toEqual(['Hauptlinie', 'Nebenlinie']);
    // Endstellung vorn: nach 1.e4 e6 ist Weiß am Zug; Pfeil aus dem letzten Kommentar dabei.
    expect(c.cards[0].frontFen).toContain(' w ');
    expect(c.cards[0].shapes.length).toBe(1);
    expect(c.cards[0].closing).toBe('Franzose.');
  });

  it('filtert per lines=<lineKey>', () => {
    const key = lineKeyFromSans(['d4', 'd5']);
    const fixture = makeRep({ lines: key });
    expect(fixture.componentInstance.cards.map(x => x.heading)).toEqual(['Nebenlinie']);
  });

  // Drei Linien mit je EIGENEN Markern, wie ein Windows-/ChessBase-Export sie ablegt. Die Linienliste
  // (derselbe Zerleger) zeigt alle drei zum Markieren an; die Karten müssen dieselben drei sein.
  const THREE = [
    '[Event "Rep"]\n[White "L1"]\n[Black "Kap"]\n[Result "*"]\n\n1. e4 e5 2. Nf3 {[%cal Gf3e5]} *',
    '[Event "Rep"]\n[White "L2"]\n[Black "Kap"]\n[Result "*"]\n\n1. d4 d5 {[%csl Rd5]} *',
    '[Event "Rep"]\n[White "L3"]\n[Black "Kap"]\n[Result "*"]\n\n1. c4 {[%cal Rc4c5]} *',
  ];
  const shapesOf = (c: FlashcardsComponent) =>
    c.cards.map(x => x.shapes.map(s => `${s.brush}:${s.orig}${s.dest ?? ''}`));

  it('CRLF-Datei mit drei Linien ergibt drei Karten, jede mit ihren eigenen Pfeilen', () => {
    const crlf = THREE.join('\n\n').replace(/\n/g, '\r\n') + '\r\n';
    const c = makeRep({}, crlf).componentInstance;
    expect(c.cards.map(x => x.heading)).toEqual(['L1', 'L2', 'L3']);
    expect(c.cards.map(x => x.notation)).toEqual(['1.e4 e5 2.Nf3', '1.d4 d5', '1.c4']);
    expect(shapesOf(c)).toEqual([['green:f3e5'], ['red:d5'], ['red:c4c5']]);
  });

  it('ohne Leerzeile zwischen den Partien: lines=/marked= finden auch die hinteren Linien', () => {
    const tight = THREE.join('\n');
    const l3 = lineKeyFromSans(['c4']);
    const byLines = makeRep({ lines: l3 }, tight).componentInstance;
    expect(byLines.cards.map(x => x.heading)).toEqual(['L3']);
    expect(shapesOf(byLines)).toEqual([['red:c4c5']]);

    const l2 = lineKeyFromSans(['d4', 'd5']);
    const byMarks = makeRep({ marked: '1' }, tight.replace(/\n/g, '\r\n'), [l2]).componentInstance;
    expect(byMarks.cards.map(x => x.heading)).toEqual(['L2']);
    expect(shapesOf(byMarks)).toEqual([['red:d5']]);
  });
});

/** UI-Sweep 2026-10-11 (d2-rk-flashcards-gross-schmal): Lernkarte wächst mit der Fensterhöhe bis 720 px. */
describe('FlashcardsComponent Lernkarten-Breite', () => {
  it('bindet die Kartenbreite an die Höhe statt fest 480 px', () => {
    const css = ((FlashcardsComponent as any).ɵcmp.styles as string[]).join('\n').replace(/\s+/g, ' ');
    expect(css).toMatch(/\.fc-study-card[^{]*\{ ?width: min\(92vw, max\(480px, (calc\()?100vh - 320px\)?\), 720px\)/);
  });

  // b1-rk-flashcards-vollbild-knopf-verrutscht: die Knopfzeile ist so breit wie die Karte, das Symbol sitzt an ihrer Ecke.
  it('Vollbild-Knopfzeile hat die Kartenbreite, nicht fest 480 px', () => {
    const css = ((FlashcardsComponent as any).ɵcmp.styles as string[]).join('\n').replace(/\s+/g, ' ');
    expect(css).toMatch(/\.fc-fs-btn[^{]*\{ ?width: min\(92vw, max\(480px, (calc\()?100vh - 320px\)?\), 720px\)/);
    expect(css).not.toContain('min(92vw, 480px)');
  });
});

/** b1-w-flashcard-flip: in Safari/WebKit lag die gespiegelte Rückseite über dem Brett. */
describe('FlashcardsComponent 3D-Flip (WebKit)', () => {
  const css = () => ((FlashcardsComponent as any).ɵcmp.styles as string[]).join('\n')
    .replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, ' ');

  it('CSS: eigene Ebene der Vorderseite, verdeckte Seite unsichtbar, Scrollen innen', () => {
    const c = css();
    expect(c).toMatch(/\.fc-flip \{[^}]*transform-style: preserve-3d/);
    expect(c).toMatch(/\.fc-flip-face \{[^}]*backface-visibility: hidden[^}]*transition: visibility 0s linear 0\.22s/);
    expect(c).toMatch(/\.fc-flip-front \{[^}]*transform: rotateY\(0deg\)/);
    const back = c.match(/\.fc-flip-back \{[^}]*\}/)![0];
    expect(back).toContain('visibility: hidden');
    expect(back).not.toContain('overflow');
    expect(c).toMatch(/\.fc-flip--flipped \.fc-flip-back \{ ?visibility: visible/);
    expect(c).toMatch(/\.fc-flip--flipped \.fc-flip-front \{ ?visibility: hidden/);
    expect(c).toMatch(/\.fc-study-back \{[^}]*overflow-y: auto/);
  });

  it('gerendert: ungedreht ist nur die Vorderseite sichtbar, gedreht nur die Rückseite', async () => {
    const fixture = make([puzzle(1)], {});
    const el: HTMLElement = fixture.nativeElement;
    document.body.appendChild(el);
    try {
      const vis = (sel: string) => getComputedStyle(el.querySelector(sel)!).visibility;
      expect(vis('.fc-flip-front')).toBe('visible');
      expect(vis('.fc-flip-back')).toBe('hidden');
      fixture.componentInstance.flip();
      fixture.detectChanges();
      // Wechsel nach halber Drehzeit (0,22 s)
      await new Promise(r => setTimeout(r, 400));
      expect(vis('.fc-flip-front')).toBe('hidden');
      expect(vis('.fc-flip-back')).toBe('visible');
    } finally {
      el.remove();
    }
  });
  it('Karte ohne Titel heißt „Line 3“ wie in der Linienliste, nicht „#3“ (d3-rk-flashcards-karte-titel-nummer)', () => {
    const fixture = make([puzzle(3, { title: '' })], {});
    TestBed.inject(TranslateService).setTranslation('en', { repertoire: { lines: { lineN: 'Line {{n}}' } } }, true);
    TestBed.inject(TranslateService).use('en');
    fixture.detectChanges();
    const heads = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.fc-heading')).map(h => h.textContent!.trim());
    expect(heads.length).toBeGreaterThan(0);
    expect(heads.every(h => h === 'Line 3')).toBeTrue();
  });
});
