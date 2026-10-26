import { Subject } from 'rxjs';
import { CalcTrainingSession, CalcTrainingSessionHost } from './calc-training-session';

function make(loaded = true) {
  const credited: { id: number; seconds: number; token: string }[] = [];
  const warned: { key: string; action?: { actionKey: string; duration: number } }[] = [];
  const action$ = new Subject<void>();
  let positionId: number | null = 7;
  const host: CalcTrainingSessionHost = {
    positionLoaded: () => loaded,
    currentPositionId: () => positionId,
    creditSeconds: (id, seconds, token) => credited.push({ id, seconds, token }),
    warn: (key, action) => { warned.push({ key, action }); return { onAction: () => action$ }; },
  };
  const session = new CalcTrainingSession({
    storageKey: () => 'rookhub_calc_timer_spec', userId: () => null, onPersistFailed: () => {},
  }, host);
  return { session, credited, warned, action$, setPosition: (id: number | null) => positionId = id };
}

describe('CalcTrainingSession', () => {
  afterEach(() => { try { localStorage.removeItem('rookhub_calc_timer_spec'); } catch { /* ignore */ } });

  it('ohne Kapitel-Topf kein Start, sondern ein Hinweis', () => {
    const { session, warned } = make();
    expect(session.canTrain).toBeFalse();
    expect(session.disabled).toBeTrue();
    expect(session.actionKey).toBe('calc.timer.unavailable');
    session.start();
    expect(session.running).toBeFalse();
    expect(warned).toEqual([{ key: 'calc.timer.unavailable', action: undefined }]);
  });

  it('misst die Stellungszeit erst bei laufendem Training und rechnet beim Stopp ab', () => {
    const { session, credited } = make();
    session.timer.syncChapter('A');
    spyOn(session.watch, 'stop').and.returnValue(25);
    session.beginWatch(7);               // Training läuft nicht → keine Uhr
    session.harvestWatch();
    expect(credited.length).toBe(0);
    session.start();
    expect(session.stateKey).toBe('calc.timer.running');
    session.stop();
    expect(credited.length).toBe(1);
    expect(credited[0].id).toBe(7);
    expect(credited[0].seconds).toBe(25);
    expect(credited[0].token).toBeTruthy();
    session.timer.stop();
  });

  it('bietet den Start nur EINMAL an; der Knopf startet', () => {
    const { session, warned, action$ } = make();
    session.timer.syncChapter('A');
    const onStart = jasmine.createSpy('onStart');
    session.offerStart(onStart);
    session.offerStart(onStart);
    expect(warned.length).toBe(1);
    expect(warned[0]).toEqual({ key: 'calc.timer.notRunning', action: { actionKey: 'calc.timer.start', duration: 10000 } });
    action$.next();
    expect(onStart).toHaveBeenCalledTimes(1);
  });
});
