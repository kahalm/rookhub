import { RepertoireLinesComponent } from './repertoire-lines.component';
import { RepertoireLine } from './repertoire-viewer.service';

function line(chapter: string, gameIndex: number): RepertoireLine {
  return {
    gameIndex, summary: '1. e4', opening: '', white: 'W', black: chapter,
    result: '*', moveCount: 1, chapter,
  };
}

describe('RepertoireLinesComponent chapterGroups reactivity', () => {
  it('recomputes chapterGroups when lines are set AFTER init (async load)', () => {
    const c = new RepertoireLinesComponent();
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
    const c = new RepertoireLinesComponent();
    c.lines = [line('Chapter A', 0)];
    expect(c.chapterGroups()[0].expanded).toBeTrue();
    c.toggleChapter('Chapter A');
    expect(c.chapterGroups()[0].expanded).toBeFalse();
    c.toggleChapter('Chapter A');
    expect(c.chapterGroups()[0].expanded).toBeTrue();
  });
});
