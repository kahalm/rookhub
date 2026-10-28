import { of, Subscription, throwError } from 'rxjs';
import { CalcSeriesController, CalcSeriesHost } from './calc-series.controller';
import { CourseManageChapter } from './course.types';

function chapter(name: string | null): CourseManageChapter {
  return { name, lineCount: 1, quizCount: 0, solvedCount: 0, progressPercent: 0, solverIndex: null, firstLineId: 1 };
}

describe('CalcSeriesController', () => {
  function make(canManage = true, dialogResult: unknown = undefined, editionsOver: Record<string, unknown> = {}) {
    const log: string[] = [];
    const busy: boolean[] = [];
    const subs: Subscription[] = [];
    const host: CalcSeriesHost = { bookId: 9, canManage, setBusy: b => busy.push(b), track: s => subs.push(s) };
    const calc = { getBook: () => of({ chapters: [{ chapter: 'A', points: 3, maxPoints: 4, ratedCount: 1 }] }) };
    const editions = {
      manage: () => { log.push('manage'); return of([{ id: 2, chapter: 'A', publishAt: 'x', videoUrl: null, released: true }]); },
      visible: () => { log.push('visible'); return of([]); },
      upsert: () => { log.push('upsert'); return of({}); },
      remove: (_b: number, id: number) => { log.push('remove:' + id); return of(undefined); },
      ...editionsOver,
    };
    const dialog = { open: () => { log.push('dialog'); return { afterClosed: () => of(dialogResult) }; } };
    const warnings: string[] = [];
    const snackbar = { quick: (m: string) => log.push('quick:' + m), warn: (m: string) => warnings.push(m) };
    const ctrl = new CalcSeriesController(host, calc as never, editions as never, dialog as never, snackbar as never,
      { instant: (k: string) => k } as never);
    return { ctrl, log, busy, subs, warnings };
  }

  it('reload(true) lädt Punkte und Ausgaben, reload(false) leert beides', () => {
    const { ctrl, log, subs } = make();
    ctrl.reload(true);
    expect(ctrl.chapterScore(chapter('A'))).toBe('3 / 4');
    expect(ctrl.edition(chapter('A'))?.id).toBe(2);
    expect(log).toEqual(['manage']);
    expect(subs.length).toBe(2);
    ctrl.reload(false);
    expect(ctrl.chapterScore(chapter('A'))).toBe('');
    expect(ctrl.edition(chapter('A'))).toBeNull();
  });

  it('Löschen: setzt busy, entfernt die Ausgabe, meldet und lädt neu', () => {
    const { ctrl, log, busy } = make(true, { delete: true });
    ctrl.reload(true);
    log.length = 0;
    ctrl.openEdition(chapter('A'));
    expect(log).toEqual(['dialog', 'remove:2', 'quick:calc.series.deleted', 'manage']);
    expect(busy).toEqual([true, false]);
  });

  it('Fehler beim Speichern: Warnung, busy frei, kein Neuladen', () => {
    const { ctrl, log, busy, warnings } = make(true, { save: {} }, { upsert: () => throwError(() => new Error('x')) });
    ctrl.openEdition(chapter('A'));
    expect(warnings).toEqual(['calc.series.saveFailed']);
    expect(busy).toEqual([true, false]);
    expect(log).toEqual(['dialog']);
  });

  it('Betrachter: lädt nur freigegebene, öffnet keine Dialoge', () => {
    const { ctrl, log } = make(false, { save: {} });
    ctrl.reload(true);
    ctrl.openEdition(chapter('A'));
    ctrl.openMembers();
    expect(log).toEqual(['visible']);
  });
  it('Serien-Verteiler öffnet als kurzes Formular mit maxWidth 94vw (DESIGN 5.6, d7-rk-verteiler-tester-rand)', () => {
    const configs: Record<string, unknown>[] = [];
    const dialog = { open: (_c: unknown, cfg: Record<string, unknown>) => { configs.push(cfg); return { afterClosed: () => of(undefined) }; } };
    const host: CalcSeriesHost = { bookId: 9, canManage: true, setBusy: () => {}, track: () => {} };
    const ctrl = new CalcSeriesController(host, {} as never, {} as never, dialog as never, {} as never,
      { instant: (k: string) => k } as never);
    ctrl.openMembers();
    expect(configs.length).toBe(1);
    expect(configs[0]['width']).toBe('440px');
    expect(configs[0]['maxWidth']).toBe('94vw');
  });
});
