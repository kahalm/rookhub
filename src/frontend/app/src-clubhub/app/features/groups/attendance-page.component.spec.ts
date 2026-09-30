import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService } from '../../core/club-api.service';
import { Group, SessionDetail, SessionInput } from '../../core/club.models';
import { AttendancePageComponent, tallyText } from './attendance-page.component';

const GROUP = (extra: Partial<Group> = {}): Group => ({
  id: 1, name: 'Anfänger', weekday: 5, schedule: '17:00', archived: false, memberCount: 3, sessionCount: 0, lastSession: null, trainers: [],
  sessions: [], canManage: false,
  members: [
    { id: 11, firstName: 'Anna', lastName: 'Auer', statuses: [], present: 0, recorded: 0 },
    { id: 12, firstName: 'Ben', lastName: 'Berger', statuses: [], present: 0, recorded: 0 },
    { id: 13, firstName: 'Carla', lastName: 'Czerny', statuses: [], present: 0, recorded: 0 },
  ], ...extra,
});
const SESSION = (extra: Partial<SessionDetail> = {}): SessionDetail =>
  ({ id: 5, groupId: 1, date: '2026-09-25', topic: 'Gabel', notes: null, present: 1, excused: 1, absent: 1,
    attendance: [{ memberId: 11, status: 'present' }, { memberId: 12, status: 'excused' }, { memberId: 13, status: 'absent' }], ...extra });

describe('AttendancePageComponent (am Freitag abhaken, wer da ist)', () => {
  let fixture: ComponentFixture<AttendancePageComponent>;
  let api: jasmine.SpyObj<ClubApiService>;
  let router: Router;
  const el = () => fixture.nativeElement as HTMLElement;
  const ticks = () => Array.from(el().querySelectorAll<HTMLButtonElement>('.tick'));
  const pressed = () => ticks().map(t => t.getAttribute('aria-pressed'));

  async function create(now: Date, query: Record<string, string> = {}): Promise<void> {
    TestBed.configureTestingModule({
      imports: [AttendancePageComponent],
      providers: [provideRouter([]), { provide: ClubApiService, useValue: api },
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

  beforeEach(() => {
    api = jasmine.createSpyObj<ClubApiService>('ClubApiService', ['group', 'sessionByDate', 'saveSession', 'deleteSession']);
    api.group.and.resolveTo(GROUP());
    api.sessionByDate.and.resolveTo(null);
  });

  it('am Freitag geht die Liste für HEUTE auf, alle noch ohne Haken', async () => {
    await create(new Date(2026, 8, 25, 17, 5));
    expect(api.sessionByDate).toHaveBeenCalledWith(1, '2026-09-25');
    expect(el().querySelector<HTMLInputElement>('input[name=date]')!.value).toBe('2026-09-25');
    expect(el().textContent).toContain('Freitag, 25. September 2026');
    expect(ticks().map(t => t.textContent!.trim().replace(/\s+/g, ' '))).toEqual(['Auer Anna', 'Berger Ben', 'Czerny Carla']);
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

  it('ein Tipp hakt ab, „entschuldigt" daneben; gespeichert wird JEDES Kind — wer keinen Haken hat, hat gefehlt', async () => {
    api.saveSession.and.callFake(async (_g: number, input: SessionInput) => SESSION({
      topic: input.topic, attendance: input.attendance,
      present: input.attendance.filter(a => a.status === 'present').length, excused: 1, absent: 1 }));
    await create(new Date(2026, 8, 25, 17, 5));

    ticks()[0].click();
    el().querySelectorAll<HTMLButtonElement>('.excuse')[1].click();
    fixture.detectChanges();
    expect(pressed()).toEqual(['true', 'false', 'false']);
    expect(el().querySelectorAll('.excuse')[1].getAttribute('aria-pressed')).toBe('true');
    expect(el().querySelector('.tally strong')!.textContent).toBe('1 von 3 da');
    ticks()[0].click();                                                                  // Vertippt: Haken wieder weg
    ticks()[0].click();
    fixture.componentInstance.topic.set(' Gabel und Spieß ');
    el().querySelector<HTMLButtonElement>('.save')!.click();
    await settle();

    expect(api.saveSession).toHaveBeenCalledWith(1, {
      date: '2026-09-25', topic: 'Gabel und Spieß', notes: null,
      attendance: [{ memberId: 11, status: 'present' }, { memberId: 12, status: 'excused' }, { memberId: 13, status: 'absent' }],
    });
    expect(el().querySelector('[role=status]')!.textContent).toBe('Gespeichert: 1 von 3 da.');
    expect(el().querySelector('.existing')).not.toBeNull();                              // ab jetzt gibt es die Einheit
  });

  it('eine schon erfasste Einheit kommt mit ihrem Stand — sonst überschriebe die leere Liste sie', async () => {
    api.sessionByDate.and.resolveTo(SESSION());
    await create(new Date(2026, 8, 25, 19, 0));
    expect(pressed()).toEqual(['true', 'false', 'false']);
    expect(el().querySelectorAll('.excuse')[1].getAttribute('aria-pressed')).toBe('true');
    expect(el().querySelector<HTMLInputElement>('input[name=topic]')!.value).toBe('Gabel');
    expect(el().querySelector('.existing')!.textContent).toContain('schon eine Einheit erfasst');
    ticks()[1].click();                                                                  // entschuldigt → da
    fixture.detectChanges();
    expect(pressed()).toEqual(['true', 'true', 'false']);
    expect(el().querySelectorAll('.excuse')[1].getAttribute('aria-pressed')).toBe('false');
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

  it('„Alle da" hakt alle ab; scheitert das Speichern, steht es da und nichts gilt als gesichert', async () => {
    api.saveSession.and.rejectWith(new HttpErrorResponse({ status: 500 }));
    await create(new Date(2026, 8, 25, 17, 5));
    el().querySelector<HTMLButtonElement>('.all-present')!.click();
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
    const ask = spyOn(window, 'confirm').and.returnValue(false);
    await fixture.componentInstance.remove(SESSION());
    expect(api.deleteSession).not.toHaveBeenCalled();
    ask.and.returnValue(true);
    await fixture.componentInstance.remove(SESSION());
    expect(api.deleteSession).toHaveBeenCalledWith(5);
    expect(router.navigate).toHaveBeenCalledWith(['/gruppen', 1]);
  });

  it('eine Gruppe ohne Kinder lädt zum Anlegen ein (mit der Gruppe vorausgewählt)', async () => {
    api.group.and.resolveTo(GROUP({ members: [] }));
    await create(new Date(2026, 8, 25, 17, 5));
    expect(el().querySelector('.empty a')!.getAttribute('href')).toBe('/kind/neu?gruppe=1');
    expect(tallyText(7, 12)).toBe('7 von 12 da');
  });
});
