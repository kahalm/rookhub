import { ScanExport, ScanExportDeps } from './scan-export';

const GAME = { moves: ['e4', 'e5'], white: 'Oberschmid', black: 'Hengl', result: '1-0', event: null, year: 2026 };

describe('ScanExport', () => {
  let deps: ScanExportDeps;
  beforeEach(() => {
    deps = { input: () => GAME, addToMyGames: jasmine.createSpy('add').and.resolveTo({ imported: 1, ids: [42] }),
      download: jasmine.createSpy('download'), copy: jasmine.createSpy('copy').and.resolveTo() };
  });

  it('herunterladen: PGN mit den Namen wie im Formular', async () => {
    new ScanExport(deps).downloadPgn();
    const [blob, name] = (deps.download as jasmine.Spy).calls.mostRecent().args as [Blob, string];
    expect(blob.type).toBe('application/x-chess-pgn');
    expect(await blob.text()).toContain('[White "Oberschmid"]');
    expect(name).toMatch(/\.pgn$/);
  });

  it('kopieren: Meldung je nach Erfolg', async () => {
    const x = new ScanExport(deps);
    await x.copyPgn();
    expect((deps.copy as jasmine.Spy).calls.mostRecent().args[0]).toContain('1. e4 e5 1-0');
    expect(x.pgnMsg()).toEqual({ text: 'PGN kopiert.', err: false });
    (deps.copy as jasmine.Spy).and.rejectWith(new Error('nein'));
    await x.copyPgn();
    expect(x.pgnMsg()?.err).toBeTrue();
  });

  it('zu „Meine Partien": Id gemerkt; schon vorhanden und Fehler sagen es', async () => {
    const x = new ScanExport(deps);
    await x.addToMyGames();
    expect(x.myGameId()).toBe(42);
    expect(x.pgnMsg()?.text).toBe('In deinen Partien gespeichert.');
    expect(x.adding()).toBeFalse();
    (deps.addToMyGames as jasmine.Spy).and.resolveTo({ imported: 0, ids: [42] });
    await x.addToMyGames();
    expect(x.pgnMsg()?.text).toBe('Die Partie war schon in deinen Partien.');
    (deps.addToMyGames as jasmine.Spy).and.rejectWith(new Error('500'));
    await x.addToMyGames();
    expect(x.pgnMsg()).toEqual({ text: 'Speichern in deinen Partien hat nicht geklappt.', err: true });
  });
});
