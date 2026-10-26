import { CalcLabels } from './calc-labels';
import { CalcChapterModel } from './calc-chapter-model';
import { CalcReview } from './calc-review.util';

describe('CalcLabels', () => {
  function make(review: Partial<CalcReview> = {}, live = 0) {
    const chapters = new CalcChapterModel();
    const t = (key: string, params?: object) => params ? `${key} ${JSON.stringify(params)}` : key;
    const labels = new CalcLabels({
      t, chapters,
      review: () => ({ chosenSan: null, chosenUci: null, secondsSpent: 0, grade: null, ...review }) as CalcReview,
      liveSeconds: () => live,
      trainingStateKey: () => 'calc.timer.stopped',
    });
    return { labels, chapters };
  }

  it('ohne Kapitel leere Namen; Knopf unbewertet „Ergebnis"', () => {
    const { labels } = make();
    expect(labels.chapterName).toBe('');
    expect(labels.nextChapterName).toBe('');
    expect(labels.gradeButtonLabel).toBe('calc.review.result');
    expect(labels.positionPoints).toBe(0);
  });

  it('Zeit der Stellung zählt die laufende Sitzung mit; Uhr-Erklärung nennt den Laufzustand', () => {
    const { labels } = make({ secondsSpent: 60 }, 5);
    expect(labels.positionTimeDisplay).toBe(labels.timeDisplay(65));
    expect(labels.timerTooltip).toBe('calc.timer.stopped\ncalc.timer.editHint');
  });

  it('Kapitel-Kopf: Name plus Punkte, mit Zeit nur wenn gemessen', () => {
    const { labels } = make();
    const group = { chapter: 'K1', label: '', items: [], points: 2, maxPoints: 4, seconds: 0 } as any;
    expect(labels.chapterLabel(group)).toContain('K1 · calc.review.chapterScore');
    group.seconds = 90;
    expect(labels.chapterLabel(group)).toContain('K1 · calc.review.chapterSummary');
    expect(labels.scoreDisplay(2, 4)).toContain('2');
  });
});
