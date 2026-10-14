import { TestBed } from '@angular/core/testing';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { SanPipe } from '../../shared/chess/san.pipe';
import { CourseLanguageService } from './course-language.service';
import { of, throwError } from 'rxjs';
import { CourseBrowseComponent } from './course-browse.component';
import { BookPuzzleDto } from '../puzzles/puzzle.service';

/**
 * Durchsehen-Ansicht: Gruppierung nach Kapitel, Kapitel-Filter, Zug-Durchsicht (goTo) inkl.
 * Kommentar-Rückwärtssuche und Orientierung aus der Startstellung, plus Linien-Navigation.
 */
describe('CourseBrowseComponent', () => {
  const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

  function line(over: Partial<BookPuzzleDto>): BookPuzzleDto {
    return {
      id: 0, lineId: 'l', bookFileName: 'b.pgn', round: '1', fen: START, moves: 'e2e4 e7e5',
      ...over,
    } as BookPuzzleDto;
  }

  function build(
    puzzles: BookPuzzleDto[],
    chapterIndex: number | null = null,
    chapters: any[] = [],
    opts: { status?: { solvedIds: number[]; failedIds: number[] }; favorites?: any[]; loggedIn?: boolean; dialog?: any; favService?: any; marks?: number[]; setMark?: any } = {},
  ): CourseBrowseComponent {
    const route: any = { snapshot: { paramMap: { get: (k: string) => k === 'bookId' ? '5' : (k === 'chapterIndex' ? (chapterIndex == null ? null : String(chapterIndex)) : null) } } };
    const courseService: any = {
      getBookPuzzles: () => of(puzzles),
      getChapters: () => of(chapters),
      getLineStatus: () => of(opts.status ?? { solvedIds: [], failedIds: [] }),
      getFlashcardMarks: () => of({ lineIds: opts.marks ?? [] }),
      setFlashcardMark: opts.setMark ?? (() => of({ marked: true })),
    };
    const prefs: any = { boardTheme: 'brown', pieceSet: 'cburnett' };
    const auth: any = { isLoggedIn: opts.loggedIn ?? true };
    const favorites: any = opts.favService ?? { list: () => of(opts.favorites ?? []), add: () => of(true), remove: () => of(false) };
    const dialog: any = opts.dialog ?? { open: () => {} };
    const worksheets: any = { sendAndNotify: jasmine.createSpy('sendAndNotify') };
    const comp = new CourseBrowseComponent(
      route, {} as any, courseService, prefs, { info: () => {} } as any, { instant: (k: string) => k } as any,
      auth, favorites, dialog, worksheets,
      new CourseLanguageService({ currentLang: () => 'de', getFallbackLang: () => 'en' } as any),
    );
    comp.ngOnInit();
    return comp;
  }

  it('lädt persistente Flashcard-Marks und toggelt optimistisch über den Service', () => {
    const setMark = jasmine.createSpy('setMark').and.returnValue(of({ marked: true }));
    const comp = build([line({ id: 1 }), line({ id: 2 })], null, [], { marks: [2], setMark });
    expect([...comp.marked]).toEqual([2]);

    comp.toggleMark({ id: 1 }, new Event('click'));
    expect(comp.marked.has(1)).toBeTrue();
    expect(setMark).toHaveBeenCalledWith(5, 1, true);

    comp.toggleMark({ id: 2 }, new Event('click'));
    expect(comp.marked.has(2)).toBeFalse();
    expect(setMark).toHaveBeenCalledWith(5, 2, false);
  });

  it('rollt die Markierung bei Server-Fehler zurück', () => {
    const setMark = jasmine.createSpy('setMark').and.returnValue(throwError(() => new Error('down')));
    const comp = build([line({ id: 1 })], null, [], { setMark });
    comp.toggleMark({ id: 1 }, new Event('click'));
    expect(comp.marked.has(1)).toBeFalse();   // optimistisch gesetzt, nach Fehler zurückgenommen
  });

  it('loads all lines and groups them by chapter, selecting the first', () => {
    const comp = build([
      line({ id: 1, chapter: 'Intro' }),
      line({ id: 2, chapter: 'Intro' }),
      line({ id: 3, chapter: 'Tactics' }),
    ]);
    expect(comp.lines.length).toBe(3);
    expect(comp.groups.map(g => g.name)).toEqual(['Intro', 'Tactics']);
    expect(comp.groups[0].lines.length).toBe(2);
    expect(comp.selected?.id).toBe(1);
    expect(comp.totalPlies).toBe(2);
    expect(comp.sanMoves).toEqual(['e4', 'e5']);
  });

  it('filters to a single chapter when a chapter index is given', () => {
    const comp = build(
      [line({ id: 1, chapter: 'Intro' }), line({ id: 2, chapter: 'Tactics' }), line({ id: 3, chapter: 'Tactics' })],
      1,
      [{ index: 0, name: 'Intro' }, { index: 1, name: 'Tactics' }],
    );
    expect(comp.chapterName).toBe('Tactics');
    expect(comp.lines.map(l => l.id)).toEqual([2, 3]);
  });

  it('steps through the line and resolves comments backwards; orientation from the start FEN', () => {
    const comp = build([line({ id: 1, moveComments: { '-1': 'intro', '0': 'good' } })]);
    expect(comp.orientation).toBe('white'); // white to move in the start position

    comp.goTo(0);
    expect(comp.comment).toBe('intro');   // ply -1 (intro)
    comp.goTo(1);
    expect(comp.comment).toBe('good');     // ply 0
    comp.goTo(2);
    expect(comp.comment).toBe('good');     // ply 1 has none → keeps last seen
    expect(comp.lastMove).toEqual(['e7', 'e5']);
  });

  it('turns playable moves in a comment into clickable variation chips and previews them', () => {
    const comp = build([line({ id: 1, moveComments: { '-1': 'Instead 1.d4 is also good.' } })]);
    comp.goTo(0);
    expect(comp.comment).toContain('d4');

    const segs = comp.commentBlocks.flat();
    const chip = segs.find(s => s.move);
    expect(chip).toBeTruthy();
    expect(chip!.move).toContain('d4');

    comp.previewVariationMove(chip!);
    expect(comp.variationPreview).not.toBeNull();
    expect(comp.variationPreview!.fen).toBe(chip!.fen!);

    comp.goTo(1); // stepping ends the preview
    expect(comp.variationPreview).toBeNull();
  });

  it('numbers the move strip from the line FEN (Black to move, move 23), not „1." with White first', () => {
    // Chessable-Kurslinie ab einer Partiestellung: Schwarz am Zug im 23. Zug.
    const fen = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 23';
    const comp = build([line({ id: 1, fen, moves: 'e7e5 g1f3 b8c6' })]);
    expect(comp.sanMoves).toEqual(['e5', 'Nf3', 'Nc6']);
    expect(comp.moveNumbers).toEqual(['23…', '24.', null]);

    // Linie ab der Grundstellung: wie bisher „1." vor Weiß.
    const start = build([line({ id: 2 })]);
    expect(start.moveNumbers).toEqual(['1.', null]);
  });

  it('renders an illegal-FEN pattern line (no king) without throwing, stepping by coordinates', () => {
    // Chessable-Muster-/Info-Diagramme sind bewusst illegal (hier ohne Könige); chess.js verwirft sie.
    const badFen = '8/5pp1/6P1/8/8/8/8/7R w - - 0 1';
    const comp = build([line({
      id: 1, fen: badFen, moves: 'h1h8 g6f7',
      moveComments: { '-1': 'Pattern: 1.Rh8+ wins.' },
    })]);
    expect(comp.selected?.id).toBe(1);
    expect(comp.orientation).toBe('white');       // aus dem 2. FEN-Feld
    expect(comp.sanMoves).toEqual(['h1h8', 'g6f7']);   // kein SAN ableitbar → roher UCI-Zug
    expect(comp.boardFen).toBe(badFen);
    expect(() => comp.commentBlocks).not.toThrow();
    comp.goTo(1);
    expect(comp.boardFen).toBe('7R/5pp1/6P1/8/8/8/8/8 b - - 0 1');
    expect(comp.lastMove).toEqual(['h1', 'h8']);
    expect(comp.comment).toBe('Pattern: 1.Rh8+ wins.');
  });

  it('reports per-line status (solved ✓ / failed ✗ / none) from the loaded line status', () => {
    const comp = build(
      [line({ id: 1 }), line({ id: 2 }), line({ id: 3 })],
      null, [],
      { status: { solvedIds: [1], failedIds: [2] } },
    );
    expect(comp.lineStatus(line({ id: 1 }))).toBe('solved');
    expect(comp.lineStatus(line({ id: 2 }))).toBe('failed');
    expect(comp.lineStatus(line({ id: 3 }))).toBe('none');
  });

  it('marks favorited lines and toggles them optimistically', () => {
    const add = jasmine.createSpy('add').and.returnValue(of(true));
    const remove = jasmine.createSpy('remove').and.returnValue(of(false));
    const favService = { list: () => of([{ source: 'Book', puzzleId: 1 }]), add, remove };
    const comp = build([line({ id: 1 }), line({ id: 2 })], null, [], { favService });

    expect(comp.isFavorite(line({ id: 1 }))).toBe(true);
    expect(comp.isFavorite(line({ id: 2 }))).toBe(false);

    comp.toggleFavorite(line({ id: 2 }), { stopPropagation: () => {} } as any);
    expect(comp.isFavorite(line({ id: 2 }))).toBe(true); // optimistic
    expect(add).toHaveBeenCalledWith('book', 2);

    comp.toggleFavorite(line({ id: 1 }), { stopPropagation: () => {} } as any);
    expect(comp.isFavorite(line({ id: 1 }))).toBe(false);
    expect(remove).toHaveBeenCalledWith('book', 1);
  });

  it('opens the share dialog for a single line with a book deep-link', () => {
    const open = jasmine.createSpy('open');
    const comp = build([line({ id: 7 })], null, [], { dialog: { open } });
    comp.shareLine(line({ id: 7 }), { stopPropagation: () => {} } as any);
    expect(open).toHaveBeenCalled();
    const data = open.calls.mostRecent().args[1].data;
    expect(data.url).toContain('/puzzles/book/7?single=1');
    expect(data.source).toBe('book');
    expect(data.puzzleId).toBe(7);
  });

  it('navigates between lines', () => {
    const comp = build([line({ id: 1 }), line({ id: 2 }), line({ id: 3 })]);
    expect(comp.selectedIndex).toBe(0);
    comp.nextLine();
    expect(comp.selected?.id).toBe(2);
    comp.prevLine();
    expect(comp.selected?.id).toBe(1);
    comp.prevLine(); // clamp at start
    expect(comp.selected?.id).toBe(1);
  });

  describe('Sprache der Kommentare (Kurs-Übersetzung)', () => {
    beforeEach(() => localStorage.removeItem('rookhub_course_lang'));
    afterEach(() => localStorage.removeItem('rookhub_course_lang'));

    function withLang(puzzlesFor: (lang: string | null | undefined) => BookPuzzleDto[], chapterIndex: number | null = null,
                      chapters: any[] = []) {
      const langs: (string | null | undefined)[] = [];
      const route: any = { snapshot: { paramMap: { get: (k: string) => k === 'bookId' ? '5' : (k === 'chapterIndex' ? (chapterIndex == null ? null : String(chapterIndex)) : null) } } };
      const courseService: any = {
        getBookPuzzles: (_id: number, lang?: string | null) => { langs.push(lang); return of(puzzlesFor(lang)); },
        getChapters: () => of(chapters),
        getLineStatus: () => of({ solvedIds: [], failedIds: [] }),
        getFlashcardMarks: () => of({ lineIds: [] }),
        getTranslations: () => of({ sourceLanguage: 'en', languages: [{ language: 'de', linesTranslated: 2, linesTotal: 2 }] }),
      };
      const comp = new CourseBrowseComponent(
        route, {} as any, courseService, { boardTheme: 'brown', pieceSet: 'cburnett' } as any, { info: () => {} } as any,
        { instant: (k: string) => k } as any, { isLoggedIn: true } as any, { list: () => of([]) } as any, {} as any,
        { sendAndNotify: () => {} } as any,
        new CourseLanguageService({ currentLang: () => 'de', getFallbackLang: () => 'en' } as any),
      );
      comp.ngOnInit();
      return { comp, langs };
    }

    it('lädt in der Oberflächensprache und zeigt Kapitel-/Titel-Labels (gruppiert wird über das Original)', () => {
      const { comp, langs } = withLang(() => [
        line({ id: 1, chapter: 'Openings', chapterLabel: 'Eröffnungen', title: 'Main', titleLabel: 'Haupt' }),
        line({ id: 2, chapter: 'Openings', chapterLabel: 'Eröffnungen' }),
        line({ id: 3, chapter: 'Endgames' }),
      ]);
      expect(langs).toEqual(['de']);
      expect(comp.groups.map(g => [g.name, g.label, g.lines.length])).toEqual([
        ['Openings', 'Eröffnungen', 2], ['Endgames', 'Endgames', 1],
      ]);
      expect(comp.lineTitle(comp.lines[0])).toBe('Haupt');
      expect(comp.lineTitle(comp.lines[2])).toBeNull();
      expect(comp.courseLang.languages({ bookId: 5 })).toEqual(['en', 'de']);
    });

    it('Kapitel-Filter über den Schlüssel, Überschrift mit dem Label', () => {
      const { comp } = withLang(() => [
        line({ id: 1, chapter: 'Openings' }), line({ id: 2, chapter: 'Endgames' }),
      ], 0, [{ index: 0, name: 'Openings', label: 'Eröffnungen', puzzleCount: 1, solvedCount: 0, progressPercent: 0, infoCount: 0 }]);
      expect(comp.chapterName).toBe('Openings');
      expect(comp.chapterDisplay).toBe('Eröffnungen');
      expect(comp.lines.map(l => l.id)).toEqual([1]);
    });

    it('Sprachwechsel lädt neu und bleibt auf derselben Linie und demselben Zug', () => {
      const { comp, langs } = withLang(lang => [
        line({ id: 1 }),
        line({ id: 2, moves: 'e2e4 e7e5 g1f3', moveComments: { '1': lang === 'en' ? 'english' : 'deutsch' } }),
      ]);
      comp.selectLine(comp.lines[1]);
      comp.goTo(2);
      expect(comp.comment).toBe('deutsch');
      comp.pickLanguage('en');
      expect(langs).toEqual(['de', 'en']);
      expect(comp.selected?.id).toBe(2);
      expect(comp.plyIndex).toBe(2);
      expect(comp.comment).toBe('english');
    });
  });

  // Codereview 2026-09-29 (F3-015): Pfeil runter im offenen Menue „An Aufgabenblatt" waehlte das Ziel UND sprang zur
  // naechsten Linie — Enter schickte dann die Stellung einer ANDEREN Linie aufs Blatt.
  it('arrow keys inside an open menu move the menu selection, not the line', () => {
    const comp = build([line({ id: 1 }), line({ id: 2 })]);
    const press = (target: EventTarget, handled = false) => {
      const e = new KeyboardEvent('keydown', { key: 'ArrowDown', cancelable: true });
      Object.defineProperty(e, 'target', { value: target });
      if (handled) e.preventDefault();      // MatMenu/ListKeyManager: preventDefault, aber kein stopPropagation
      comp.onKeyDown(e);
    };
    const overlay = document.createElement('div');
    overlay.className = 'cdk-overlay-container';
    const pane = document.createElement('div');
    pane.className = 'cdk-overlay-pane';
    const item = document.createElement('button');
    item.setAttribute('mat-menu-item', '');
    pane.appendChild(item);
    overlay.appendChild(pane);
    document.body.appendChild(overlay);
    try {
      press(item);
      press(item, true);
      press(document.body, true);
      expect(comp.selected?.id).toBe(1);
    } finally {
      overlay.remove();
    }
    press(document.body);                   // auf der Seite: naechste Linie wie gewohnt
    expect(comp.selected?.id).toBe(2);
  });
});

/** UI-Sweep 2026-10-11 (d2-rk-browse-*): Brett ist die Hauptsache — am Handy vor der Liste, groß mit der Höhe wachsend. */
describe('CourseBrowseComponent Layout', () => {
  const css = ((CourseBrowseComponent as any).ɵcmp.styles as string[]).join('\n').replace(/\s+/g, ' ');
  it('stellt einspaltig das Brett vor die Linienliste und kürzt am Handy den Titel', () => {
    const single = css.slice(css.indexOf('@media (max-width: 899.98px)'));
    expect(single).toMatch(/\.board-pane[^{]*\{ ?order: -1/);
    const mobile = css.slice(css.indexOf('@media (max-width: 768px)'));
    expect(mobile).toContain('text-overflow: ellipsis');
  });
  // b1-rk-browse-tablet-eng (DESIGN 4.2/5.12): einspaltig bis 899.98 px, Brett mittig bis Fensterhöhe.
  // b1-rk-browse-liste-kasten-einspaltig (DESIGN 5.3): einspaltig läuft die Liste mit der Seite, Kapitelköpfe kleben.
  it('einspaltig bis 899.98 px: Liste ohne eigenen Kasten, Brett bis Fensterhöhe, Kapitelköpfe kleben', () => {
    const plain = css.replace(/\[_ngcontent-%COMP%\]/g, '');
    const at = plain.indexOf('@media (max-width: 899.98px)');
    expect(at).toBeGreaterThan(-1);
    const single = plain.slice(at, plain.indexOf('@media', at + 10));
    expect(single).toMatch(/\.browse-body \{ ?flex-direction: column/);
    expect(single).toMatch(/\.line-list \{[^}]*max-height: none;[^}]*overflow: visible/);
    expect(single).not.toContain('260px');
    expect(single).toMatch(/\.chapter-head \{[^}]*position: sticky; top: 0/);
    expect(single).toMatch(/\.board-wrap \{ ?max-width: min\(100%, max\(320px, (calc\()?100vh - 300px\)?\)\)/);
    // die 768-px-Stufe stellt das Layout nicht mehr um
    const tab = plain.slice(plain.indexOf('@media (max-width: 768px)'));
    expect(tab.slice(0, tab.indexOf('}', tab.indexOf('}') + 1))).not.toContain('flex-direction');
  });
  // UI-Sweep z1 (z1-rk-browse-titel) + z3 (z3-rk-browse-symbolknopf): unter 600 px Titel auf zwei Zeilen,
  // die Flashcards-Knöpfe wandern ausgeschrieben ins ⋮-Menü.
  it('lässt dem Titel am Handy zwei Zeilen und legt Flashcards ins ⋮-Menü', () => {
    const phone = css.slice(css.indexOf('@media (max-width: 599.98px)'));
    expect(phone).toContain('-webkit-line-clamp: 2');
    expect(phone).toMatch(/\.fc-btn[^{]*\{ ?display: none/);
    expect(phone).toMatch(/\.fc-menu-btn[^{]*\{ ?display: inline-flex/);
    const desk = css.slice(0, css.indexOf('@media (max-width: 599.98px)'));
    expect(desk).toMatch(/\.fc-menu-btn[^{]*\{ ?display: none/);
  });
  // UI-Sweep v1 (v1-rk-browse-zugliste-handy-abgeschnitten): Zugleiste ohne eigenen Scrollkasten (DESIGN 5.11).
  it('sperrt die Zugleiste nicht in einen Scrollkasten', () => {
    const plain = css.replace(/\[_ngcontent-%COMP%\]/g, '');
    const strip = plain.match(/\.move-strip \{[^}]*\}/)![0];
    expect(strip).not.toContain('max-height');
    expect(strip).not.toContain('overflow');
  });
  // v1-rk-course-comment-ueberschrift-angeklebt: abgesetzte Kapitelüberschrift im Kommentar fett.
  it('setzt eine Überschrift im Kommentar fett', () => {
    expect(css.replace(/\[_ngcontent-%COMP%\]/g, '')).toMatch(/\.comment-card p\.cmt-heading[^{]*\{[^}]*font-weight: 700/);
  });
  // v1-rk-browse-zugliste-englische-notation: Zugleiste in den Figurenbuchstaben der Oberflächensprache (DESIGN 5.14).
  it('gibt die Zugleiste über die SanPipe aus (deutsch „Sc3")', () => {
    const deps = (CourseBrowseComponent as any).ɵcmp.dependencies;
    const list = typeof deps === 'function' ? deps() : deps;
    expect(list.some((d: any) => d === SanPipe || d?.ɵpipe?.name === 'san')).toBeTrue();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideTranslateService({ fallbackLang: 'en' })] });
    TestBed.inject(TranslateService).use('de');
    const pipe = TestBed.runInInjectionContext(() => new SanPipe());
    expect(pipe.transform('Nc3')).toBe('Sc3');
  });
  // a1-rk-browse-laptop-linienknoepfe-falz: Untergrenze 400 px statt 560 px — am Laptop (768 px hoch) folgt das Brett
  // der Höhe, „Vorige/Nächste Linie“ bleiben über der Falz.
  it('bindet die Brettbreite an die Fensterhöhe (bis 820 px, Untergrenze 400 px)', () => {
    expect(css).toMatch(/\.board-wrap[^{]*\{[^}]*max\(400px, (calc\()?100vh - 300px[^}]*820px/);
    expect(css).not.toContain('max(560px');
  });
  // a1-rk-zugnummer-allein: Zugnummer und weißer Zug in einem nicht umbrechenden Stück (DESIGN 5.13).
  it('hält Zugnummer und weißen Zug beim Umbruch zusammen', () => {
    const plain = css.replace(/\[_ngcontent-%COMP%\]/g, '');
    expect(plain).toMatch(/\.move-pair \{[^}]*display: inline-flex[^}]*white-space: nowrap/);
    const def = (CourseBrowseComponent as any).ɵcmp;
    const consts = typeof def.consts === 'function' ? def.consts() : def.consts;
    expect(JSON.stringify(consts)).toContain('move-pair');
  });
  // a1-rk-browse-breitstufe (DESIGN 4.1): ab 2400 px breites Token, Brett + Zugleiste + Kommentar bis Fensterhöhe.
  it('nimmt ab 2400 px die Breitbild-Stufe, Brett höchstens bis zur Fensterhöhe', () => {
    const plain = css.replace(/\[_ngcontent-%COMP%\]/g, '').replace(/%NS%/g, '');
    const at = plain.indexOf('@media (min-width: 2400px)');
    expect(at).toBeGreaterThan(-1);
    const wide = plain.slice(at);
    expect(wide).toMatch(/\.browse-container \{ ?max-width: min\(var\(--page-max-width-wide, 1880px\), 96vw\)/);
    expect(wide).toMatch(/\.board-wrap, \.move-strip, \.comment-card \{ ?max-width: min\(60vw, (calc\()?100vh - 300px\)?\)/);
    expect(plain.slice(0, at)).not.toContain('--page-max-width-wide');
  });
});
