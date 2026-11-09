import { fakeAsync, tick } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { AnalysisNode, createRoot, playUci } from './analysis-tree';
import { LineStudySession } from './line-study-session';
import { LineStudy, LineStudyEvals, LineStudyService } from './line-study.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('LineStudySession (ohne Engine, 0.855.0)', () => {
  let root: AnalysisNode;
  let current: AnalysisNode;
  let revealedCalls = 0;
  let saved: { tree: { n?: { p: number; u: string }[] | null } }[];
  let study: LineStudy;
  let evals: LineStudyEvals;
  let api: LineStudyService;
  let saveErrors: unknown[];

  function host() {
    return {
      root: () => root, current: () => current,
      setTree: (r: AnalysisNode, c: AnalysisNode) => { root = r; current = c; },
      revealed: () => { revealedCalls++; }, changed: () => {},
      saveFailed: (err: unknown) => { saveErrors.push(err); },
    };
  }

  beforeEach(() => {
    root = createRoot(START);
    current = playUci(playUci(root, 'e2e4')!, 'e7e5')!;
    revealedCalls = 0;
    saved = [];
    saveErrors = [];
    study = { id: 7, gameKey: 'g:k', startFen: START, title: null, tree: {}, current: -1, revealed: false, analyzed: 0, total: 3 };
    evals = { revealed: false, analyzed: 1, total: 3, nodes: [] };
    api = {
      get: () => throwError(() => ({ status: 404 })),
      save: (req: { tree: { n?: { p: number; u: string }[] | null } }) => { saved.push(req); return of(study); },
      reveal: () => of({ ...study, revealed: true }),
      evals: () => of(evals),
    } as unknown as LineStudyService;
  });

  it('neu: legt den Partie-Baum an, Änderungen gehen gedrosselt hinterher', fakeAsync(() => {
    const s = new LineStudySession(api, host(), 'g:k', ['e2e4', 'e7e5'], null);
    s.open();
    expect(saved.length).toBe(1);
    expect(saved[0].tree.n!.map(d => d.u)).toEqual(['e2e4', 'e7e5']);
    expect(s.id).toBe(7);
    current = playUci(current, 'g1f3')!;
    s.schedule();
    tick(LineStudySession.SaveDelayMs - 1);
    expect(saved.length).toBe(1);
    tick(1);
    expect(saved.length).toBe(2);
    s.destroy();
  }));

  it('revealWhenReady vor der ersten Kennung: deckt nach dem Anlegen genau einmal auf (0.898.0)', () => {
    evals = { revealed: true, analyzed: 1, total: 3, nodes: [] };   // nach dem Aufdecken meldet der Server „aufgedeckt“
    const reveal = spyOn(api, 'reveal').and.callThrough();
    const s = new LineStudySession(api, host(), 'g:k', ['e2e4', 'e7e5'], null);
    s.revealWhenReady();          // noch keine Kennung → vorgemerkt
    expect(reveal).not.toHaveBeenCalled();
    s.open();                     // 404 → anlegen → Kennung 7 → aufdecken
    expect(reveal).toHaveBeenCalledOnceWith(7);
    expect(s.revealed).toBeTrue();
    s.revealWhenReady();          // schon aufgedeckt → nichts
    expect(reveal).toHaveBeenCalledTimes(1);
    s.destroy();
  });

  it('vorhanden: legt den gespeicherten Baum aufs Brett', () => {
    api.get = () => of({ ...study, tree: { n: [{ p: -1, u: 'd2d4' }] }, current: 0 });
    const s = new LineStudySession(api, host(), 'g:k', ['d2d4'], null);
    s.open();
    expect(root.children.map(c => c.uci)).toEqual(['d2d4']);
    expect(current.uci).toBe('d2d4');
    s.destroy();
  });

  it('aufdecken: Bewertungen an die Knoten der gespeicherten Reihenfolge, dazu der Vergleich', () => {
    const s = new LineStudySession(api, host(), 'g:k', ['e2e4'], null);
    s.open();                                   // speichert e4, e5 (Index 0, 1)
    evals = { revealed: true, analyzed: 3, total: 3, nodes: [
      { index: -1, cp: 30, bestUci: 'e2e4', depth: 20 }, { index: 0, cp: 25, bestUci: 'e7e5', depth: 20 }, { index: 1, cp: 35, depth: 20 }] };
    s.reveal();
    expect(revealedCalls).toBe(1);
    expect(s.revealed).toBeTrue();
    const e4 = root.children[0];
    expect(e4.evalText).toBe('+0.25');
    expect(e4.children[0].evalText).toBe('+0.35');
    expect(s.comparison?.rated).toBe(1);        // 1…e5 ist eigener Zug (die Partie war nur 1.e4)
    expect(s.comparison?.best).toBe(1);
    s.destroy();
  });

  it('neu (404): legt nur an (createOnly) — 409 = inzwischen vorhanden → laden statt ersetzen (F2-002)', () => {
    const reqs: { createOnly?: boolean }[] = [];
    let gets = 0;
    api.get = () => (++gets === 1 ? throwError(() => ({ status: 404 })) : of({ ...study, tree: { n: [{ p: -1, u: 'd2d4' }] }, current: 0 }));
    api.save = ((req: { createOnly?: boolean }) => { reqs.push(req); return throwError(() => ({ status: 409 })); }) as unknown as LineStudyService['save'];
    const s = new LineStudySession(api, host(), 'g:k', ['e2e4', 'e7e5'], null);
    s.open();
    expect(reqs.length).toBe(1);
    expect(reqs[0].createOnly).toBeTrue();
    expect(gets).toBe(2);                                   // nachgeladen
    expect(root.children.map(c => c.uci)).toEqual(['d2d4']); // der gespeicherte Baum, nicht die Partie
    expect(saveErrors.length).toBe(0);
    s.destroy();
  });

  it('Ladefehler ≠ 404: nichts speichern, Fehler melden, Neuversuch lädt (F2-002)', fakeAsync(() => {
    let fail = true;
    api.get = () => (fail ? throwError(() => ({ status: 500 })) : of({ ...study, tree: { n: [{ p: -1, u: 'd2d4' }] }, current: 0 }));
    const save = spyOn(api, 'save').and.callThrough();
    const s = new LineStudySession(api, host(), 'g:k', ['e2e4', 'e7e5'], null);
    s.open();
    expect(save).not.toHaveBeenCalled();
    expect(s.loadFailed).toBeTrue();
    current = playUci(current, 'g1f3')!;
    s.schedule();
    tick(LineStudySession.SaveDelayMs * 2);
    s.flush();
    expect(save).not.toHaveBeenCalled();                    // Speichern gesperrt (schedule/flush)
    fail = false;
    s.open();                                               // Neuversuch
    expect(s.loadFailed).toBeFalse();
    expect(root.children.map(c => c.uci)).toEqual(['d2d4']);
    expect(save).not.toHaveBeenCalled();                    // geladen, nichts Offenes
    s.destroy();
    expect(save).not.toHaveBeenCalled();
  }));

  it('Ladefehler: auch destroy() speichert nicht (F2-002)', () => {
    api.get = () => throwError(() => ({ status: 0 }));
    const save = spyOn(api, 'save').and.callThrough();
    const s = new LineStudySession(api, host(), 'g:k', ['e2e4'], null);
    s.open();
    s.schedule();
    s.destroy();
    expect(save).not.toHaveBeenCalled();
  });

  it('Speichern scheitert: Meldung, Änderung bleibt offen, flush() schickt sie erneut (F2-003)', () => {
    const s = new LineStudySession(api, host(), 'g:k', ['e2e4', 'e7e5'], null);
    s.open();                                               // 404 → angelegt
    expect(saved.length).toBe(1);
    api.save = (() => throwError(() => ({ status: 500 }))) as unknown as LineStudyService['save'];
    current = playUci(current, 'g1f3')!;
    s.schedule();
    s.flush();
    expect(saveErrors.length).toBe(1);
    const retry = spyOn(api, 'save').and.returnValue(of(study));
    s.flush();
    expect(retry).toHaveBeenCalledTimes(1);
    s.flush();
    expect(retry).toHaveBeenCalledTimes(1);                 // gelungen → nichts mehr offen
    s.destroy();
  });

  it('„Engine einschalten“ während eines laufenden Speicherns wird nach dem Speichern ausgeführt (F2-004)', () => {
    const s = new LineStudySession(api, host(), 'g:k', ['e2e4', 'e7e5'], null);
    s.open();                                               // 404 → angelegt, Kennung 7
    const pending = new Subject<LineStudy>();
    api.save = (() => pending) as unknown as LineStudyService['save'];
    const reveal = spyOn(api, 'reveal').and.callThrough();
    current = playUci(current, 'g1f3')!;
    s.schedule();
    s.flush();                                              // PUT läuft
    s.reveal();                                             // nichts offen, aber Speichern läuft
    expect(reveal).not.toHaveBeenCalled();
    pending.next(study);
    pending.complete();
    expect(reveal).toHaveBeenCalledOnceWith(7);
    s.destroy();
  });
});
