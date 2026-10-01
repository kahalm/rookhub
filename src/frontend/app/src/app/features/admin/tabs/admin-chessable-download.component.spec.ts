import { Subject, of, throwError } from 'rxjs';
import { AdminChessableDownloadComponent } from './admin-chessable-download.component';

/** Reine Logik-Tests (ohne Template/ngOnInit) — Filter-Getter der Kurs-Download-Ansicht. */
function make(chessable: any = {}) {
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

  // W5 F5-017: ein Ladefehler der User-Liste darf nicht wie „keine User mit Zugang" aussehen.
  it('loadDlUsers setzt bei Fehler dlUsersError und loescht ihn beim naechsten Erfolg', () => {
    const getCredentialedUsersAdmin = jasmine.createSpy('get').and.returnValues(
      throwError(() => ({ status: 500 })), of([{ userId: 1, username: 'a' }]));
    const c = make({ getCredentialedUsersAdmin });
    c.loadDlUsers();
    expect(c.dlUsersLoading).toBeFalse();
    expect(c.dlUsersError).toBeTrue();
    c.loadDlUsers();
    expect(c.dlUsersError).toBeFalse();
    expect(c.dlUsers.length).toBe(1);
  });

  // W5 F5-004: User A (langsam), dann B — A's Kurse duerfen nicht unter B stehen (Import liefe mit B's Bearer).
  it('onDlUserChange: die spaete Kursliste des vorher gewaehlten Users landet nicht beim neuen', () => {
    const a$ = new Subject<any>();
    const b$ = new Subject<any>();
    const c = make({ getUserCoursesAdmin: jasmine.createSpy('courses').and.callFake((uid: number) => uid === 1 ? a$ : b$) });
    c.dlSelectedUserId = 1;
    c.onDlUserChange();
    c.dlSelectedUserId = 2;
    c.onDlUserChange();
    expect(a$.observed).toBeFalse();
    b$.next({ courses: [{ bid: 'b1', name: 'B-Kurs' }] });
    a$.next({ courses: [{ bid: 'a1', name: 'A-Kurs' }] });
    expect(c.dlCourses.map(x => x.bid)).toEqual(['b1']);
    expect(c.dlCoursesLoading).toBeFalse();
  });

  it('onDlShowExpiredChange: geleerte Auswahl bricht das Laden ab, keine Kurse kommen nach', () => {
    const a$ = new Subject<any>();
    const c = make({ getUserCoursesAdmin: jasmine.createSpy('courses').and.returnValue(a$) });
    c.dlUsers = [{ userId: 2, username: 'dead', blocked: true }] as any;
    c.dlShowExpired = true;
    c.dlSelectedUserId = 2;
    c.onDlUserChange();
    c.dlShowExpired = false;
    c.onDlShowExpiredChange();
    a$.next({ courses: [{ bid: 'a1', name: 'A-Kurs' }] });
    expect(c.dlSelectedUserId).toBeNull();
    expect(c.dlCourses).toEqual([]);
    expect(c.dlCoursesLoading).toBeFalse();
  });
});
