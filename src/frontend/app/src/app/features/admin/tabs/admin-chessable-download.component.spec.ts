import { AdminChessableDownloadComponent } from './admin-chessable-download.component';

/** Reine Logik-Tests (ohne Template/ngOnInit) — Filter-Getter der Kurs-Download-Ansicht. */
function make() {
  const chessable = {} as any;
  const snackbar = { info: () => {}, show: () => {} } as any;
  const translate = { instant: (k: string) => k } as any;
  return new AdminChessableDownloadComponent(chessable, snackbar, translate);
}

describe('AdminChessableDownloadComponent', () => {
  it('dlVisibleCourses hides already-loaded courses only when dlHideLoaded is on', () => {
    const c = make();
    c.dlCourses = [
      { bid: '1', name: 'fresh' },
      { bid: '2', name: 'as-rep', importedRepertoire: true },
      { bid: '3', name: 'as-book', importedBook: true },
    ] as any;

    c.dlHideLoaded = false;
    expect(c.dlVisibleCourses().length).toBe(3);

    c.dlHideLoaded = true;
    expect(c.dlVisibleCourses().map(x => x.bid)).toEqual(['1']);
  });

  it('dlVisibleUsers hides blocked users unless dlShowExpired is on', () => {
    const c = make();
    c.dlUsers = [
      { userId: 1, username: 'ok' },
      { userId: 2, username: 'dead', blocked: true },
    ] as any;

    c.dlShowExpired = false;
    expect(c.dlVisibleUsers().map(u => u.userId)).toEqual([1]);

    c.dlShowExpired = true;
    expect(c.dlVisibleUsers().map(u => u.userId)).toEqual([1, 2]);
  });

  it('onDlShowExpiredChange clears a now-hidden blocked selection', () => {
    const c = make();
    c.dlUsers = [
      { userId: 1, username: 'ok' },
      { userId: 2, username: 'dead', blocked: true },
    ] as any;
    c.dlShowExpired = true;
    c.dlSelectedUserId = 2;
    c.dlCourses = [{ bid: '9', name: 'x' }] as any;

    // Haken wieder raus → gesperrter User 2 fällt aus der Liste → Auswahl + Kurse leeren.
    c.dlShowExpired = false;
    c.onDlShowExpiredChange();

    expect(c.dlSelectedUserId).toBeNull();
    expect(c.dlCourses.length).toBe(0);
  });

  it('onDlShowExpiredChange keeps a still-visible selection', () => {
    const c = make();
    c.dlUsers = [{ userId: 1, username: 'ok' }, { userId: 2, username: 'dead', blocked: true }] as any;
    c.dlShowExpired = false;
    c.dlSelectedUserId = 1;
    c.onDlShowExpiredChange();
    expect(c.dlSelectedUserId).toBe(1);
  });
});
