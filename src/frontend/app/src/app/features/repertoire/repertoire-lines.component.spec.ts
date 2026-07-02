import { of } from 'rxjs';
import { RepertoireLinesComponent } from './repertoire-lines.component';
import { RepertoireLine } from './repertoire-viewer.service';

function line(chapter: string, gameIndex: number): RepertoireLine {
  return {
    gameIndex, summary: '1. e4', opening: '', white: 'W', black: chapter,
    result: '*', moveCount: 1, chapter, lineKey: 'k' + gameIndex,
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
