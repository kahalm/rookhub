import { of, throwError } from 'rxjs';
import { RepertoireLinesComponent } from './repertoire-lines.component';
import { RepertoireLine, RepertoireViewerService } from './repertoire-viewer.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

function line(chapter: string, gameIndex: number, lastMoveSide: 'w' | 'b' | null = 'w'): RepertoireLine {
  return {
    gameIndex, summary: '1. e4', opening: '', white: 'W', black: chapter,
    result: '*', moveCount: 1, chapter, lineKey: 'k' + gameIndex,
    startFen: START, lastMoveSide,
  };
}

function makeComponent(): RepertoireLinesComponent {
  const training: any = {
    getLineStates: () => of([]),
    getFlashcardMarks: () => of({ lineKeys: [] }),
    setFlashcardMark: () => of({ marked: true }),
    promote: () => of({ affected: 1 }),
    makeDue: () => of({ affected: 1 }),
    setPaused: () => of({ affected: 1 }),
  };
  return new RepertoireLinesComponent(training);
}

describe('RepertoireLinesComponent chapterGroups reactivity', () => {
  it('recomputes chapterGroups when lines are set AFTER init (async load)', () => {
    const c = makeComponent();
    // Erstzugriff mit leeren Linien (wie beim Öffnen, bevor loadPgn fertig ist).
    expect(c.chapterGroups().length).toBe(0);

    // Linien laden asynchron nach — das computed MUSS jetzt reagieren (Regression:
    // vorher blieb es leer, bis die Komponente neu aufgebaut wurde).
    c.lines = [line('Chapter A', 0), line('Chapter A', 1), line('Chapter B', 2)];

    const groups = c.chapterGroups();
    expect(groups.length).toBe(2);
    expect(groups[0].chapter).toBe('Chapter A');
    expect(groups[0].lines.length).toBe(2);
    expect(groups[1].chapter).toBe('Chapter B');
  });

  it('toggleChapter collapses/expands a group', () => {
    const c = makeComponent();
    c.lines = [line('Chapter A', 0)];
    expect(c.chapterGroups()[0].expanded).toBeTrue();
    c.toggleChapter('Chapter A');
    expect(c.chapterGroups()[0].expanded).toBeFalse();
    c.toggleChapter('Chapter A');
    expect(c.chapterGroups()[0].expanded).toBeTrue();
  });

  it('promote calls the service with the given line keys and reloads', () => {
    const training: any = {
      getLineStates: jasmine.createSpy('get').and.returnValue(of([])),
      getFlashcardMarks: () => of({ lineKeys: [] }),
      promote: jasmine.createSpy('promote').and.returnValue(of({ affected: 2 })),
    };
    const c = new RepertoireLinesComponent(training);
    c.repertoireId = 7;
    c.promote(['a', 'b']);
    expect(training.promote).toHaveBeenCalledWith(7, ['a', 'b']);
    expect(training.getLineStates).toHaveBeenCalled();   // Reload nach der Aktion
  });

  it('status reflects the loaded line state', () => {
    const past = new Date(Date.now() - 3600_000).toISOString();
    const training: any = {
      getLineStates: () => of([
        { lineKey: 'k0', level: 3, reps: 3, lapses: 0, dueAt: past, lastReviewedAt: past, inPool: true, paused: false },
        { lineKey: 'k1', level: 1, reps: 1, lapses: 0, dueAt: past, lastReviewedAt: past, inPool: true, paused: true },
      ]),
      getFlashcardMarks: () => of({ lineKeys: [] }),
    };
    const c = new RepertoireLinesComponent(training);
    c.repertoireId = 7;
    c.ngOnInit();
    expect(c.status(line('A', 0))).toBe('due');       // k0, in pool, due
    expect(c.status(line('A', 1))).toBe('paused');    // k1, paused
    expect(c.status(line('A', 2))).toBe('new');       // k2, kein Zustand
    expect(c.badge(line('A', 0))).toBe('S3');
  });

  it('lädt persistente Flashcard-Marks und toggelt optimistisch über den Service', () => {
    const training: any = {
      getLineStates: () => of([]),
      getFlashcardMarks: () => of({ lineKeys: ['k1'] }),
      setFlashcardMark: jasmine.createSpy('setMark').and.returnValue(of({ marked: true })),
    };
    const c = new RepertoireLinesComponent(training);
    c.repertoireId = 7;
    c.ngOnInit();
    expect([...c.marked]).toEqual(['k1']);

    c.toggleMark(line('A', 0), new Event('click'));   // k0 → markieren
    expect(c.marked.has('k0')).toBeTrue();
    expect(training.setFlashcardMark).toHaveBeenCalledWith(7, 'k0', true);

    c.toggleMark(line('A', 1), new Event('click'));   // k1 → entmarkieren
    expect(c.marked.has('k1')).toBeFalse();
    expect(training.setFlashcardMark).toHaveBeenCalledWith(7, 'k1', false);
  });

  it('rollt die Markierung bei Server-Fehler zurück', () => {
    const training: any = {
      getLineStates: () => of([]),
      getFlashcardMarks: () => of({ lineKeys: [] }),
      setFlashcardMark: () => throwError(() => new Error('down')),
    };
    const c = new RepertoireLinesComponent(training);
    c.repertoireId = 7;
    c.ngOnInit();
    c.toggleMark(line('A', 0), new Event('click'));
    expect(c.marked.has('k0')).toBeFalse();
  });
});

describe('RepertoireLinesComponent trained color per chapter', () => {
  afterEach(() => localStorage.removeItem('rookhub_rep_train_chaptercolor_7'));

  it('auto-detects color from the chapter majority of last-move sides', () => {
    const c = makeComponent();
    c.repertoireId = 7;
    c.ngOnInit();
    // Kapitel „W": beide Linien enden auf Weiß → Weiß. Kapitel „B": beide auf Schwarz → Schwarz.
    c.lines = [line('W', 0, 'w'), line('W', 1, 'w'), line('B', 2, 'b'), line('B', 3, 'b')];
    const groups = c.chapterGroups();
    expect(c.chapterColor(groups.find(g => g.chapter === 'W')!)).toBe('w');
    expect(c.chapterColor(groups.find(g => g.chapter === 'B')!)).toBe('b');
  });

  it('setChapterColor persists an override that wins over auto-detection', () => {
    const c = makeComponent();
    c.repertoireId = 7;
    c.ngOnInit();
    c.lines = [line('Caro', 0, 'w'), line('Caro', 1, 'b')];   // Gleichstand → Auto-Fallback
    const group = c.chapterGroups()[0];
    c.setChapterColor(group, 'w');
    expect(c.chapterColor(group)).toBe('w');
    expect(JSON.parse(localStorage.getItem('rookhub_rep_train_chaptercolor_7')!)['Caro']).toBe('w');
  });
});

const REAL_LINE = '[Event "Lifetime Repertoires: Martinovićs Französisch"]\n[Round "004.002"]\n'
  + '[White "1A | 2.Sf3 | Weiß spielt 6.Lxa3"]\n[Black "1) Weiß spielt ohne 2.d4"]\n'
  + '[FEN "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"]\n[Result "*"]\n\n'
  + '{Liebe Schachfreunde. Es gibt nichts weniger kritisches als wenn unser Gegner nicht ausnutzt, dass er \n2.d4\n'
  + 'spielen kann.} 1. e4 {[%tqu "En","find the move","","","e7e6","",10]} e6 {[%cal Gd7d5][%alt c5 e5]Bereits '
  + 'nach diesem Zug.} 2. Nf3 {Dieser Zug hat fast keine eigenständige Bedeutung.} d5 3. e5 (3.Nc3 Nf6 4.e5 Nfd7 5.d4 '
  + '{werden wir später sehen.}) (3.exd5 exd5 4.d4 {geht über in die Abtauschvariante.}) (3.d3 {ist hier in der '
  + 'Zugfolge }) {2.d3 d5 3.Nf3 analysiert.} c5 4. b4 {[%cal Bb4c5][%csl Rc5]} ({Dieses Gambit nach 2.Sf3.} 4.c3 '
  + '{ist besser:} 4...Nc6 5.d4 {würde überleiten.}) cxb4 {[%alt b6]Es gibt auch gute Alternativen.} 5. a3 *\n';

describe('RepertoireLinesComponent klickbare Kommentar-Züge', () => {
  it('echte Chessable-Linie: Einleitung und eingefaltete Varianten werden anklickbar', () => {
    const viewer = new RepertoireViewerService();
    viewer.loadPgn(REAL_LINE);
    viewer.selectLine(0);
    const c = makeComponent();
    c.lines = viewer.lines;
    c.selectedIndex = 0;
    c.moves = viewer.currentMoves;
    c.comments = viewer.currentComments;
    c.ngOnChanges({ moves: {} as any });

    const chips = (i: number) => (c.commentSegments[i] ?? []).filter(s => s.move).map(s => s.move);
    expect(chips(-1)).toEqual(['2.d4']);
    expect(chips(4)).toEqual(['3.Nc3', 'Nf6', '4.e5', 'Nfd7', '5.d4', '3.exd5', 'exd5', '4.d4', '3.d3', '2.d3', 'd5', '3.Nf3']);
    expect(chips(6)).toEqual(['2.Sf3', '4.c3', '4...Nc6', '5.d4']);
    // „2.d4" aus der Einleitung zeigt die Stellung nach 1.e4 e6 2.d4 (wie auf Chessable)
    const d4 = c.commentSegments[-1].find(s => s.move === '2.d4')!;
    expect(d4.fen!.split(' ').slice(0, 2).join(' ')).toBe('rnbqkbnr/pppp1ppp/4p3/8/3PP3/8/PPP2PPP/RNBQKBNR b');
  });

  it('ohne gewählte Linie gibt es keine Stücke', () => {
    const c = makeComponent();
    c.lines = [];
    c.selectedIndex = -1;
    c.comments = { 0: '2.d4' };
    c.ngOnChanges({ comments: {} as any });
    expect(c.commentSegments).toEqual({});
  });
});
