import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService } from '../../core/club-api.service';
import { Group, SessionDetail, SessionInput } from '../../core/club.models';
import { AttendancePageComponent, tallyText } from './attendance-page.component';
import { of } from 'rxjs';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';

const GROUP = (extra: Partial<Group> = {}): Group => ({
  id: 1, name: 'Anfänger', weekday: 5, schedule: '17:00', archived: false, memberCount: 3, sessionCount: 0, lastSession: null, trainers: [],
  sessions: [], canManage: false, coaches: [],
  members: [
    { id: 11, firstName: 'Anna', lastName: 'Auer', statuses: [], present: 0, recorded: 0 },
    { id: 12, firstName: 'Ben', lastName: 'Berger', statuses: [], present: 0, recorded: 0 },
    { id: 13, firstName: 'Carla', lastName: '', statuses: [], present: 0, recorded: 0 },          // Nachname nicht bekannt
  ], ...extra,
});
const SESSION = (extra: Partial<SessionDetail> = {}): SessionDetail =>
  ({ id: 5, groupId: 1, date: '2026-09-25', topic: 'Gabel', notes: 'Arbeitsblatt 3', present: 1, absent: 2, photos: [],
    attendance: [{ memberId: 11, status: 'present' }, { memberId: 12, status: 'absent' }, { memberId: 13, status: 'absent' }], ...extra });

describe('AttendancePageComponent (am Freitag abhaken, wer da ist)', () => {
  let fixture: ComponentFixture<AttendancePageComponent>;
  let api: jasmine.SpyObj<ClubApiService>;
  /** Rückfrage (ConfirmService) — Vorgabe „ja", je Test umstellbar. */
  let confirmAsk: jasmine.Spy;
  let router: Router;
  const el = () => fixture.nativeElement as HTMLElement;
  const ticks = () => Array.from(el().querySelectorAll<HTMLButtonElement>('.tick'));
  const pressed = () => ticks().map(t => t.getAttribute('aria-pressed'));

  async function create(now: Date, query: Record<string, string> = {}): Promise<void> {
    TestBed.configureTestingModule({
      imports: [AttendancePageComponent],
      providers: [{ provide: ConfirmService, useValue: { ask: (...a: unknown[]) => confirmAsk(...a) } }, provideRouter([]), { provide: ClubApiService, useValue: api },
        { provide: AuthService, useValue: { has: (p: string) => p === 'club.trainer' } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '1' }), queryParamMap: convertToParamMap(query) } } }],
    });
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    fixture = TestBed.createComponent(AttendancePageComponent);
    fixture.componentInstance.now = () => now;
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function type(selector: string, value: string): void {
    const input = el().querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  beforeEach(() => {
    confirmAsk = jasmine.createSpy('ask').and.returnValue(of(true));
    api = jasmine.createSpyObj<ClubApiService>('ClubApiService', ['group', 'sessionByDate', 'sessionDates', 'saveSession', 'deleteSession', 'uploadPhoto', 'photoBlob', 'deletePhoto', 'memberPhotoBlob']);
    api.memberPhotoBlob.and.resolveTo(new Blob(['x'], { type: 'image/jpeg' }));
    api.sessionDates.and.resolveTo([]);
    api.photoBlob.and.resolveTo(new Blob(['x'], { type: 'image/jpeg' }));
    api.group.and.resolveTo(GROUP());
    api.sessionByDate.and.resolveTo(null);
  });

  it('am Freitag geht die Liste für HEUTE auf, alle noch ohne Haken; ein Kind ohne Nachnamen steht mit dem Vornamen da', async () => {
    await create(new Date(2026, 8, 25, 17, 5));
    expect(api.sessionByDate).toHaveBeenCalledWith(1, '2026-09-25');
    expect(el().querySelector<HTMLInputElement>('input[name=date]')!.value).toBe('2026-09-25');
    expect(el().textContent).toContain('Freitag, 25. September 2026');
    expect(ticks().map(t => t.textContent!.trim().replace(/\s+/g, ' '))).toEqual(['Auer Anna', 'Berger Ben', 'Carla']);
    expect(pressed()).toEqual(['false', 'false', 'false']);
    expect(el().querySelector('.tally strong')!.textContent).toBe('0 von 3 da');
    expect(el().querySelector('.existing')).toBeNull();
  });

  it('am Montag darauf für den vergangenen Freitag (nachtragen); ?datum= gewinnt', async () => {
    await create(new Date(2026, 8, 28, 9, 0));
    expect(api.sessionByDate).toHaveBeenCalledWith(1, '2026-09-25');
    TestBed.resetTestingModule();
    api.sessionByDate.calls.reset();
    await create(new Date(2026, 8, 28, 9, 0), { datum: '2026-09-11' });
    expect(api.sessionByDate).toHaveBeenCalledWith(1, '2026-09-11');
  });

  it('nur „da" oder „nicht da" — ein „entschuldigt" gibt es nicht', async () => {
    await create(new Date(2026, 8, 25, 17, 5));
    expect(el().textContent).not.toContain('entschuldigt');
    expect(el().querySelectorAll('.roll button').length).toBe(3);                       // je Kind genau EIN Knopf
  });

  it('ein Tipp hakt ab; gespeichert wird JEDES Kind (ohne Haken = gefehlt) samt Thema und „Was wurde gemacht?"', async () => {
    api.saveSession.and.callFake(async (_g: number, input: SessionInput) => SESSION({
      topic: input.topic, notes: input.notes, attendance: input.attendance,
      present: input.attendance.filter(a => a.status === 'present').length,
      absent: input.attendance.filter(a => a.status === 'absent').length }));
    await create(new Date(2026, 8, 25, 17, 5));

    ticks()[0].click();
    ticks()[2].click();
    fixture.detectChanges();
    expect(pressed()).toEqual(['true', 'false', 'true']);
    expect(el().querySelector('.tally strong')!.textContent).toBe('2 von 3 da');
    ticks()[2].click();                                                                  // vertippt: Haken wieder weg
    type('input[name=topic]', ' Gabel und Spieß ');
    type('textarea[name=notes]', ' Gabel wiederholt, Arbeitsblatt 3, zum Schluss Simultan ');
    el().querySelector<HTMLButtonElement>('.save')!.click();
    await settle();

    expect(api.saveSession).toHaveBeenCalledWith(1, {
      date: '2026-09-25', topic: 'Gabel und Spieß', notes: 'Gabel wiederholt, Arbeitsblatt 3, zum Schluss Simultan',
      attendance: [{ memberId: 11, status: 'present' }, { memberId: 12, status: 'absent' }, { memberId: 13, status: 'absent' }],
    });
    expect(el().querySelector('[role=status]')!.textContent).toBe('Gespeichert: 1 von 3 da.');
    expect(el().querySelector('.existing')).not.toBeNull();                              // ab jetzt gibt es die Einheit

    type('textarea[name=notes]', 'noch etwas ergänzt');                                  // geändert = nicht mehr „gespeichert"
    expect(el().querySelector('[role=status]')!.textContent).toBe('');
  });

  it('eine schon erfasste Einheit kommt mit Haken, Thema und Text — sonst überschriebe die leere Liste sie', async () => {
    api.sessionByDate.and.resolveTo(SESSION());
    await create(new Date(2026, 8, 25, 19, 0));
    expect(pressed()).toEqual(['true', 'false', 'false']);
    expect(el().querySelector<HTMLInputElement>('input[name=topic]')!.value).toBe('Gabel');
    expect(el().querySelector<HTMLTextAreaElement>('textarea[name=notes]')!.value).toBe('Arbeitsblatt 3');
    expect(el().querySelector('.existing')!.textContent).toContain('schon eine Einheit erfasst');
  });

  it('solange der Stand des Tages lädt, ist die Liste gesperrt — und eine Antwort für einen verlassenen Tag zählt nicht', async () => {
    let releaseOld!: (s: SessionDetail | null) => void;
    await create(new Date(2026, 8, 25, 17, 5));
    api.sessionByDate.and.returnValues(new Promise(r => (releaseOld = r)), Promise.resolve(null));
    fixture.componentInstance.changeDate('2026-09-18');
    fixture.detectChanges();
    expect(ticks().every(t => t.disabled)).toBeTrue();
    expect(el().querySelector<HTMLButtonElement>('.save')!.disabled).toBeTrue();

    fixture.componentInstance.changeDate('2026-09-11');                                  // weitergeblättert, bevor die Antwort da ist
    await settle();
    releaseOld(SESSION({ date: '2026-09-18' }));                                         // späte Antwort für den 18.
    await settle();
    expect(fixture.componentInstance.date()).toBe('2026-09-11');
    expect(pressed()).toEqual(['false', 'false', 'false']);
    expect(ticks().every(t => !t.disabled)).toBeTrue();
  });

  it('einen Knopf „Alle da" gibt es nicht; scheitert das Speichern, steht es da und nichts gilt als gesichert', async () => {
    api.saveSession.and.rejectWith(new HttpErrorResponse({ status: 500 }));
    await create(new Date(2026, 8, 25, 17, 5));
    expect(el().querySelector('.all-present')).toBeNull();
    expect(el().textContent).not.toContain('Alle da');
    ticks().forEach(t => t.click());
    fixture.detectChanges();
    expect(el().querySelector('.tally strong')!.textContent).toBe('3 von 3 da');
    el().querySelector<HTMLButtonElement>('.save')!.click();
    await settle();
    expect(el().querySelector('.save-bar [role=alert]')!.textContent).toContain('noch nicht gesichert');
    expect(el().querySelector('[role=status]')!.textContent).toBe('');
    expect(pressed()).toEqual(['true', 'true', 'true']);                                 // die Haken bleiben für den nächsten Versuch
  });

  it('Einheit löschen fragt nach und führt zurück zur Gruppe', async () => {
    api.sessionByDate.and.resolveTo(SESSION());
    api.deleteSession.and.resolveTo();
    await create(new Date(2026, 8, 25, 19, 0));
    const ask = confirmAsk.and.returnValue(of(false));
    await fixture.componentInstance.remove(SESSION());
    expect(api.deleteSession).not.toHaveBeenCalled();
    ask.and.returnValue(of(true));
    await fixture.componentInstance.remove(SESSION());
    expect(api.deleteSession).toHaveBeenCalledWith(5);
    expect(router.navigate).toHaveBeenCalledWith(['/gruppen', 1]);
  });

  it('die Trainer stehen unter den Kindern und werden genauso abgehakt und gespeichert', async () => {
    api.group.and.resolveTo(GROUP({ coaches: [{ id: 91, firstName: 'Bernhard', lastName: '', statuses: [], present: 0, recorded: 0 },
                                             { id: 92, firstName: 'Georg', lastName: 'Auer', statuses: [], present: 0, recorded: 0 }] }));
    api.saveSession.and.callFake(async (_g: number, input: SessionInput) => SESSION({ attendance: input.attendance,
      present: input.attendance.filter(a => a.status === 'present').length, absent: input.attendance.filter(a => a.status === 'absent').length }));
    await create(new Date(2026, 8, 25, 17, 5));
    expect(el().querySelector('.roll-group')!.textContent).toBe('Trainer');
    expect(ticks().map(t => t.textContent!.trim().replace(/\s+/g, ' '))).toEqual(['Auer Anna', 'Berger Ben', 'Carla', 'Bernhard', 'Auer Georg']);
    expect(el().querySelector('.tally strong')!.textContent).toBe('0 von 5 da');
    ticks()[0].click();
    ticks()[4].click();                                                                  // Georg (Trainer) da
    fixture.detectChanges();
    expect(el().querySelector('.tally strong')!.textContent).toBe('2 von 5 da');
    el().querySelector<HTMLButtonElement>('.save')!.click();
    await settle();
    expect(api.saveSession.calls.mostRecent().args[1].attendance).toEqual([
      { memberId: 11, status: 'present' }, { memberId: 12, status: 'absent' }, { memberId: 13, status: 'absent' },
      { memberId: 91, status: 'absent' }, { memberId: 92, status: 'present' }]);
    expect(el().querySelector('[role=status]')!.textContent).toBe('Gespeichert: 2 von 5 da.');
  });

  it('Gesichter: hat einer auf der Liste ein Bild, steht in jeder Zeile zwischen Kästchen und Namen das Porträt — sonst der Anfangsbuchstabe', async () => {
    await create(new Date(2026, 8, 25, 17, 5));
    expect(el().querySelector('.roll ch-member-photo')).toBeNull();                       // niemand hat ein Bild: die Liste bleibt, wie sie war
    expect(api.memberPhotoBlob).not.toHaveBeenCalled();

    TestBed.resetTestingModule();
    spyOn(URL, 'createObjectURL').and.returnValue('blob:gesicht');
    const g = GROUP({ coaches: [{ id: 91, firstName: 'Bernhard', lastName: '', photoVersion: 7, statuses: [], present: 0, recorded: 0 }] });
    g.members[1] = { ...g.members[1], photoVersion: 1759400000000 };                      // Ben hat ein Bild
    api.group.and.resolveTo(g);
    await create(new Date(2026, 8, 25, 17, 5));
    await settle();
    expect(api.memberPhotoBlob.calls.allArgs()).toEqual([[12, true, 1759400000000], [91, true, 7]]);   // nur wer eins hat, nur das Vorschaubild
    const rows = ticks();
    expect(rows.map(t => Array.from(t.children).map(c => c.className.split(' ')[0] || c.tagName.toLowerCase())))
      .toEqual(Array(4).fill(['box', 'avatar', 'tick-name']));                              // Kästchen, Gesicht, Name
    expect(rows[1].querySelector('.avatar img')!.getAttribute('src')).toBe('blob:gesicht');
    expect(rows[0].querySelector('.avatar-ph')!.textContent).toBe('A');
    expect(rows[3].querySelector('.avatar img')).not.toBeNull();                          // auch der Trainer
    expect(rows.map(t => t.querySelector('.tick-name')!.textContent!.trim().replace(/\s+/g, ' '))).toEqual(['Auer Anna', 'Berger Ben', 'Carla', 'Bernhard']);
    rows[1].click();                                                                      // ein Tipp aufs Gesicht hakt ab wie einer auf den Namen
    rows[1].querySelector<HTMLElement>('.avatar img')!.click();
    fixture.detectChanges();
    expect(pressed()).toEqual(['false', 'false', 'false', 'false']);                      // zweimal getippt = wieder ohne Haken
    rows[1].querySelector<HTMLElement>('.avatar img')!.click();
    fixture.detectChanges();
    expect(pressed()).toEqual(['false', 'true', 'false', 'false']);
  });

  it('Blättern: „‹" und „›" führen zur Einheit davor und danach — am Ende zurück auf den Tag, für den die Liste aufgeht', async () => {
    api.sessionDates.and.resolveTo(['2026-09-11', '2026-09-18']);
    await create(new Date(2026, 8, 25, 17, 5));                                          // Freitag, noch nichts erfasst
    expect(api.sessionDates).toHaveBeenCalledWith(1);
    const prev = () => el().querySelector<HTMLButtonElement>('.pager .prev')!;
    const next = () => el().querySelector<HTMLButtonElement>('.pager .next')!;
    const label = (b: HTMLButtonElement) => b.textContent!.trim().replace(/\s+/g, ' ');
    expect(label(prev())).toBe('‹ Fr 18.09.');
    expect(next().disabled).toBeTrue();
    expect(label(next())).toBe('keine spätere ›');

    api.sessionByDate.and.resolveTo(SESSION({ date: '2026-09-18', topic: 'Spieß' }));
    prev().click();
    await settle();
    expect(api.sessionByDate).toHaveBeenCalledWith(1, '2026-09-18');
    expect(fixture.componentInstance.date()).toBe('2026-09-18');
    expect(el().querySelector<HTMLInputElement>('input[name=topic]')!.value).toBe('Spieß');
    expect(pressed()).toEqual(['true', 'false', 'false']);
    expect([label(prev()), label(next())]).toEqual(['‹ Fr 11.09.', 'Fr 25.09. ›']);
    // Der Tag steht in der Adresse — Neuladen bleibt auf ihm.
    expect((router.navigate as jasmine.Spy).calls.mostRecent().args[1].queryParams).toEqual({ datum: '2026-09-18' });

    api.sessionByDate.and.resolveTo(null);
    prev().click();
    await settle();
    expect(prev().disabled).toBeTrue();
    expect(label(prev())).toBe('‹ keine frühere');
    next().click();
    await settle();
    next().click();
    await settle();
    expect(fixture.componentInstance.date()).toBe('2026-09-25');                         // wieder „heute"
    expect((router.navigate as jasmine.Spy).calls.mostRecent().args[1].queryParams).toEqual({ datum: null });
  });

  it('Blättern mit ungespeicherten Haken fragt nach — „nein" bleibt auf dem Tag, die Haken bleiben', async () => {
    api.sessionDates.and.resolveTo(['2026-09-18']);
    await create(new Date(2026, 8, 25, 17, 5));
    api.sessionByDate.calls.reset();
    ticks()[0].click();
    fixture.detectChanges();
    const ask = confirmAsk.and.returnValue(of(false));
    el().querySelector<HTMLButtonElement>('.pager .prev')!.click();
    await settle();
    expect(ask).toHaveBeenCalledTimes(1);
    expect(api.sessionByDate).not.toHaveBeenCalled();
    expect(fixture.componentInstance.date()).toBe('2026-09-25');
    expect(pressed()).toEqual(['true', 'false', 'false']);

    // Auch das Datumsfeld fragt — und zeigt bei „nein" wieder den Tag, auf dem man steht.
    const input = el().querySelector<HTMLInputElement>('input[name=date]')!;
    input.value = '2026-09-18';
    input.dispatchEvent(new Event('change'));
    await settle();
    expect(input.value).toBe('2026-09-25');

    ask.and.returnValue(of(true));
    el().querySelector<HTMLButtonElement>('.pager .prev')!.click();
    await settle();
    expect(api.sessionByDate).toHaveBeenCalledWith(1, '2026-09-18');
    expect(pressed()).toEqual(['false', 'false', 'false']);
  });

  it('nach dem Speichern zählt die neue Einheit beim Blättern mit; ohne die Liste der Tage bleibt die Seite benutzbar', async () => {
    api.sessionDates.and.rejectWith(new HttpErrorResponse({ status: 500 }));
    api.saveSession.and.resolveTo(SESSION({ date: '2026-09-18' }));
    await create(new Date(2026, 8, 25, 17, 5), { datum: '2026-09-18' });
    expect(ticks().length).toBe(3);
    expect(el().querySelector<HTMLButtonElement>('.pager .prev')!.disabled).toBeTrue();
    expect(fixture.componentInstance.around()).toEqual({ prev: null, next: '2026-09-25' });
    el().querySelector<HTMLButtonElement>('.save')!.click();
    await settle();
    expect(fixture.componentInstance.sessionDates()).toEqual(['2026-09-18']);
    api.sessionByDate.and.resolveTo(null);
    el().querySelector<HTMLButtonElement>('.pager .next')!.click();                      // gespeichert = keine Rückfrage
    await settle();
    expect(confirmAsk).not.toHaveBeenCalled();
    expect(fixture.componentInstance.around()).toEqual({ prev: '2026-09-18', next: null });
  });

  it('Fotos: mehrere auf einmal, eines nach dem anderen hochgeladen — vorher wird die Einheit gespeichert, wenn es sie noch nicht gibt', async () => {
    api.saveSession.and.resolveTo(SESSION({ photos: [] }));
    let n = 0;
    api.uploadPhoto.and.callFake(async (_s: number, file: File) => ({ id: ++n, width: 1600, height: 1200, createdAt: '2026-09-25T17:00:00Z' }));
    await create(new Date(2026, 8, 25, 17, 5));
    expect(el().textContent).toContain('Beim ersten Foto wird die Einheit gespeichert.');

    const input = el().querySelector<HTMLInputElement>('input[type=file]')!;
    expect(input.multiple).toBeTrue();
    const dt = new DataTransfer();
    dt.items.add(new File(['a'], 'a.jpg', { type: 'image/jpeg' }));
    dt.items.add(new File(['b'], 'b.jpg', { type: 'image/jpeg' }));
    input.files = dt.files;
    input.dispatchEvent(new Event('change'));
    await settle();
    await settle();

    expect(api.saveSession).toHaveBeenCalledTimes(1);                                     // die Einheit zuerst
    expect(api.uploadPhoto.calls.allArgs().map(a => [a[0], (a[1] as File).name])).toEqual([[5, 'a.jpg'], [5, 'b.jpg']]);
    expect(fixture.componentInstance.session()!.photos.map(p => p.id)).toEqual([1, 2]);
    expect(el().querySelectorAll('.photos li').length).toBe(2);
    expect(input.value).toBe('');                                                          // dieselbe Datei geht noch einmal
  });

  it('Fotos: ein gescheitertes Hochladen steht da, die anderen kommen trotzdem an', async () => {
    api.sessionByDate.and.resolveTo(SESSION());
    api.uploadPhoto.and.returnValues(Promise.reject(new HttpErrorResponse({ status: 400, error: { message: 'Das ist kein Bild, das sich lesen lässt (JPEG, PNG oder WebP).' } })),
      Promise.resolve({ id: 7, width: 100, height: 100, createdAt: '2026-09-25T17:00:00Z' }));
    await create(new Date(2026, 8, 25, 19, 0));
    const input = el().querySelector<HTMLInputElement>('input[type=file]')!;
    const dt = new DataTransfer();
    dt.items.add(new File(['x'], 'kaputt.txt', { type: 'text/plain' }));
    dt.items.add(new File(['b'], 'b.jpg', { type: 'image/jpeg' }));
    input.files = dt.files;
    input.dispatchEvent(new Event('change'));
    await settle();
    await settle();
    expect(api.saveSession).not.toHaveBeenCalled();                                        // die Einheit gab es schon
    expect(fixture.componentInstance.session()!.photos.map(p => p.id)).toEqual([7]);
    expect(el().querySelector('.photos-section > [role=alert]')!.textContent).toContain('Nicht hochgeladen: Das ist kein Bild');
  });

  it('eine Gruppe ohne Kinder lädt zum Anlegen ein (mit der Gruppe vorausgewählt)', async () => {
    api.group.and.resolveTo(GROUP({ members: [] }));
    await create(new Date(2026, 8, 25, 17, 5));
    expect(el().querySelector('.empty a')!.getAttribute('href')).toBe('/kind/neu?gruppe=1');
    expect(tallyText(7, 12)).toBe('7 von 12 da');
  });
});
