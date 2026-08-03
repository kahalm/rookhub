import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { FlashcardsComponent } from './flashcards.component';
import { FlashcardBoardComponent } from './flashcard-board.component';
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

function make(puzzles: BookPuzzleDto[], query: Record<string, string>) {
  TestBed.resetTestingModule();   // erlaubt mehrere make()-Aufrufe in einem it
  TestBed.configureTestingModule({
    imports: [FlashcardsComponent],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      { provide: CourseService, useValue: { getBookPuzzles: () => of(puzzles) } },
      { provide: RepertoireTrainingService, useValue: { getPgn: () => of('') } },
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
  it('filtert per lines=…-Auswahl und baut 4er-Blätter', () => {
    const fixture = make([puzzle(1), puzzle(2), puzzle(3), puzzle(4), puzzle(5)], { lines: '1,3,5' });
    const c = fixture.componentInstance;
    expect(c.cards.length).toBe(3);
    expect(c.sheets.length).toBe(1);
    expect(c.sheets[0].fronts.map(f => f?.heading)).toEqual(['#1', '#3', '#5', undefined as never]);
  });

  it('Rückseiten-Blatt ist je Zeile spaltenvertauscht (Duplex an der langen Kante)', () => {
    const fixture = make([puzzle(1), puzzle(2), puzzle(3), puzzle(4)], {});
    const s = fixture.componentInstance.sheets[0];
    expect(s.fronts.map(f => f?.heading)).toEqual(['#1', '#2', '#3', '#4']);
    expect(s.backs.map(b => b?.heading)).toEqual(['#2', '#1', '#4', '#3']);
  });

  it('filtert per chapter=… (leer = „ohne Kapitel")', () => {
    const fixture = make([
      puzzle(1, { chapter: 'A' }), puzzle(2, { chapter: 'B' }), puzzle(3),
    ], { chapter: 'A' });
    expect(fixture.componentInstance.cards.map(c => c.heading)).toEqual(['#1']);

    const fixture2 = make([puzzle(1, { chapter: 'A' }), puzzle(2)], { chapter: '' });
    expect(fixture2.componentInstance.cards.map(c => c.heading)).toEqual(['#2']);
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
    expect(c.current!.heading).toBe('#1');

    c.flip();
    expect(c.flipped).toBeTrue();
    c.next();                                   // Weiterblättern dreht zurück auf die Vorderseite
    expect(c.flipped).toBeFalse();
    expect(c.current!.heading).toBe('#2');
    c.prev(); c.prev();
    expect(c.current!.heading).toBe('#3');      // zyklisch
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
    expect(c.current!.heading).toBe('#2');
    c.onKey(new KeyboardEvent('keydown', { key: ' ' }));
    expect(c.flipped).toBeTrue();

    c.view = 'print';
    c.onKey(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    expect(c.current!.heading).toBe('#2');      // unverändert
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

  function makeRep(query: Record<string, string>) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [FlashcardsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: CourseService, useValue: { getBookPuzzles: () => of([]) } },
        { provide: RepertoireTrainingService, useValue: { getPgn: () => of(REP_PGN) } },
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
});
