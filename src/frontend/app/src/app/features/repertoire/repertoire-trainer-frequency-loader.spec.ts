import { EMPTY, Subject, of, throwError } from 'rxjs';
import { Chess } from 'chess.js';
import { TrainerFrequencyLoader } from './repertoire-trainer-frequency-loader';
import { DEFAULT_EXPLORER_SETTINGS, ExplorerAnalysisResult } from './repertoire-explorer.service';
import { parsePgnText } from '../../shared/pgn-viewer/pgn-parser';

function endKey(sans: string[]): string {
  const c = new Chess();
  for (const s of sans) c.move(s);
  return c.fen().split(' ').slice(0, 3).join(' ');
}

function result(extra: Partial<ExplorerAnalysisResult> = {}): ExplorerAnalysisResult {
  return {
    complete: true, positionsAnalyzed: 3, positionsPending: 0, rateLimited: false, retryAfterSeconds: null,
    tokenMissing: false, tokenInvalid: false, fetchFailed: false, holes: [],
    lineFrequencies: { [endKey(['e4', 'e5'])]: 0.1, [endKey(['d4', 'd5'])]: 0.4 },
    ...extra,
  };
}

const LINES = parsePgnText([
  '[Event "R"]', '[White "A"]', '', '1. e4 e5 *', '',
  '[Event "R"]', '[White "B"]', '', '1. d4 d5 *', '',
  '[Event "R"]', '[White "C"]', '', '1. c4 c5 *', '',
].join('\n'));

function make(run: (...a: any[]) => any) {
  const changed = jasmine.createSpy('changed');
  const done = jasmine.createSpy('done');
  const runSpy = jasmine.createSpy('run').and.callFake(run);
  const loader = new TrainerFrequencyLoader(() => ({ run: runSpy, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) }), changed, done);
  return { loader, changed, done, runSpy };
}

describe('TrainerFrequencyLoader', () => {
  it('fragt mit den Vorgabe-Einstellungen nach Linien-Häufigkeiten (ohne Löcher) und kennt sie danach je Linie', () => {
    const { loader, done, runSpy } = make(() => of(result()));
    loader.load(7, { A: 'w' });
    const [id, req] = runSpy.calls.mostRecent().args;
    expect(id).toBe(7);
    expect(req).toEqual(jasmine.objectContaining({ color: null, chapterColors: { A: 'w' }, includeHoles: false, includeLineFrequencies: true,
      source: DEFAULT_EXPLORER_SETTINGS.source, database: DEFAULT_EXPLORER_SETTINGS.database }));
    expect(done).toHaveBeenCalledTimes(1);
    expect(loader.running).toBeFalse();
    expect(loader.notice).toBeNull();
    // gereiht wird in orderLines (repertoire-trainer-queue.util) — hier nur die Werte dafür
    expect(LINES.map(l => loader.freqOf(l))).toEqual([0.1, 0.4, -1]);
    expect(loader.available).toBeTrue();
  });

  it('available: erst mit geladenen Häufigkeiten — nicht vor dem Abruf und nicht bei leerem Ergebnis', () => {
    const { loader } = make(() => of(result({ lineFrequencies: {} })));
    expect(loader.available).toBeFalse();
    expect(loader.freqOf(LINES[0])).toBe(-1);
    loader.load(1, {});
    expect(loader.lineFreq?.size).toBe(0);
    expect(loader.available).toBeFalse();
  });

  it('cancel bricht den laufenden Abruf ab, OHNE done zu melden; ein neuer Abruf geht danach wieder', () => {
    const runs = new Subject<ExplorerAnalysisResult>();
    const { loader, done, runSpy } = make(() => runs);
    loader.load(1, {});
    runs.next(result({ complete: false }));
    loader.cancel();
    expect(runs.observed).toBeFalse();
    expect(loader.running).toBeFalse();
    expect(done).not.toHaveBeenCalled();
    expect(loader.available).toBeFalse();     // abgebrochener erster Lauf hinterlässt keine Häufigkeiten
    loader.load(1, {});
    expect(runSpy).toHaveBeenCalledTimes(2);
  });

  it('meldet Fortschritt über changed und hält ihn', () => {
    const runs = new Subject<ExplorerAnalysisResult>();
    const { loader, changed, done } = make(() => runs);
    loader.load(1, {});
    expect(loader.running).toBeTrue();
    runs.next(result({ complete: false }));
    expect(changed).toHaveBeenCalledTimes(1);
    expect(loader.progress?.complete).toBeFalse();
    expect(done).not.toHaveBeenCalled();
    loader.finish();                       // „Überspringen"
    expect(done).toHaveBeenCalledTimes(1);
    expect(loader.notice).toBe('repertoireTrainer.freqIncomplete');
    expect(runs.observed).toBeFalse();
  });

  const notices: [Partial<ExplorerAnalysisResult>, string][] = [
    [{ tokenMissing: true }, 'repertoireTrainer.freqToken'],
    [{ tokenInvalid: true }, 'repertoireTrainer.freqToken'],
    [{ fetchFailed: true }, 'repertoireTrainer.freqFailed'],
    [{ complete: false }, 'repertoireTrainer.freqIncomplete'],
  ];
  for (const [extra, key] of notices) {
    it(`Hinweis ${key} bei ${JSON.stringify(extra)}`, () => {
      const { loader } = make(() => of(result(extra)));
      loader.load(1, {});
      expect(loader.notice).toBe(key);
    });
  }

  it('Fehler: Hinweis freqFailed, leere Häufigkeiten, trotzdem fertig', () => {
    const { loader, done } = make(() => throwError(() => new Error('x')));
    loader.load(1, {});
    expect(loader.notice).toBe('repertoireTrainer.freqFailed');
    expect(loader.lineFreq?.size).toBe(0);
    expect(done).toHaveBeenCalled();
  });

  it('ohne Antwort (EMPTY): leere Häufigkeiten, kein Hinweis', () => {
    const { loader } = make(() => EMPTY);
    loader.load(1, {});
    expect(loader.lineFreq?.size).toBe(0);
    expect(loader.notice).toBeNull();
  });

  it('dispose beendet den Lauf und verhindert neue (F3-025)', () => {
    const runs = new Subject<ExplorerAnalysisResult>();
    const { loader, runSpy, done } = make(() => runs);
    loader.load(1, {});
    loader.dispose();
    expect(runs.observed).toBeFalse();
    loader.load(1, {});
    expect(runSpy).toHaveBeenCalledTimes(1);
    expect(done).not.toHaveBeenCalled();
  });
});
