import { of } from 'rxjs';
import { RepertoireLinesComponent } from './repertoire-lines.component';
import { RepertoireLine } from './repertoire-viewer.service';

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
    };
    const c = new RepertoireLinesComponent(training);
    c.repertoireId = 7;
    c.ngOnInit();
    expect(c.status(line('A', 0))).toBe('due');       // k0, in pool, due
    expect(c.status(line('A', 1))).toBe('paused');    // k1, paused
    expect(c.status(line('A', 2))).toBe('new');       // k2, kein Zustand
    expect(c.badge(line('A', 0))).toBe('S3');
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
