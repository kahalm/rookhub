import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService } from '../../core/club-api.service';
import { Group, GroupRow } from '../../core/club.models';
import { GroupPageComponent, statusMark } from './group-page.component';
import { GroupsPageComponent, scheduleText } from './groups-page.component';

const GROUP = (extra: Partial<Group> = {}): Group => ({
  id: 1, name: 'Anfänger', weekday: 5, schedule: '17:00, Vereinsheim', archived: false, memberCount: 2, sessionCount: 2, lastSession: '2026-09-25',
  trainers: [{ userId: 4, username: 'tina' }], canManage: false, coaches: [],
  sessions: [{ id: 5, date: '2026-09-18', topic: 'Matt in 1', notes: 'Arbeitsblatt 2, danach Turnier jeder gegen jeden', present: 1, absent: 1, photos: [] },
             { id: 6, date: '2026-09-25', topic: null, notes: null, present: 1, absent: 1,
               photos: [{ id: 3, width: 1600, height: 1200, createdAt: '2026-09-25T17:00:00Z' }] }],
  members: [
    { id: 11, firstName: 'Anna', lastName: 'Auer', statuses: ['absent', 'present'], present: 1, recorded: 2 },
    { id: 12, firstName: 'Ben', lastName: '', statuses: [null, 'absent'], present: 0, recorded: 1 },   // Nachname nicht bekannt
  ], ...extra,
});
const ROW = (extra: Partial<GroupRow> = {}): GroupRow =>
  ({ id: 1, name: 'Anfänger', weekday: 5, schedule: '17:00', archived: false, memberCount: 8, sessionCount: 3, lastSession: '2026-09-25',
    trainers: [{ userId: 4, username: 'tina' }, { userId: 5, username: 'tom' }], ...extra });

describe('ClubHub-Gruppen', () => {
  let api: jasmine.SpyObj<ClubApiService>;
  let perms: Set<string>;
  let router: Router;

  function configure(component: unknown, id?: string): void {
    TestBed.configureTestingModule({
      imports: [component as never],
      providers: [provideRouter([]), { provide: ClubApiService, useValue: api },
        { provide: AuthService, useValue: { has: (p: string) => perms.has(p) } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}), queryParamMap: convertToParamMap({}) } } }],
    });
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
  }

  async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    perms = new Set(['club.trainer']);
    api = jasmine.createSpyObj<ClubApiService>('ClubApiService',
      ['groups', 'group', 'createGroup', 'updateGroup', 'deleteGroup', 'addTrainer', 'removeTrainer', 'members', 'addGroupMember', 'removeGroupMember', 'photoBlob', 'deletePhoto']);
    api.photoBlob.and.resolveTo(new Blob(['x'], { type: 'image/jpeg' }));
  });

  describe('Liste', () => {
    it('zeigt je Gruppe Trainingstag, Kinderzahl, Trainer und den Weg zur Anwesenheitsliste; Anlegen nur für die Leitung', async () => {
      api.groups.and.resolveTo([ROW(), ROW({ id: 2, name: 'Alt', archived: true, weekday: null, schedule: null, memberCount: 1, trainers: [], lastSession: null })]);
      configure(GroupsPageComponent);
      const fixture = TestBed.createComponent(GroupsPageComponent);
      await settle(fixture);
      const el = fixture.nativeElement as HTMLElement;
      const first = el.querySelectorAll('.group')[0];
      expect(first.querySelector('h2 a')!.getAttribute('href')).toBe('/gruppen/1');
      expect(Array.from(first.querySelectorAll('.kid-meta > span')).map(s => s.textContent!.trim()))
        .toEqual(['Freitag, 17:00', '8 Kinder', 'zuletzt Fr 25.09.', 'Zugriff: tina, tom']);
      expect(first.querySelector('a.btn')!.getAttribute('href')).toBe('/gruppen/1/anwesenheit');
      expect(el.querySelectorAll('.group')[1].querySelector('a.btn')).toBeNull();          // archiviert: nichts abzuhaken
      expect(el.querySelectorAll('.group')[1].textContent).toContain('1 Kind');
      expect(el.querySelector('.add-group')).toBeNull();
    });

    it('die Leitung legt eine Gruppe mit Trainingstag an und landet auf ihr', async () => {
      perms.add('club.manage');
      api.groups.and.resolveTo([]);
      api.createGroup.and.resolveTo(GROUP({ id: 9 }));
      configure(GroupsPageComponent);
      const fixture = TestBed.createComponent(GroupsPageComponent);
      await settle(fixture);
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('.empty')!.textContent).toContain('Noch keine Gruppe');
      el.querySelector<HTMLButtonElement>('.add-group')!.click();
      fixture.detectChanges();
      await fixture.componentInstance.create();                                           // ohne Namen
      expect(api.createGroup).not.toHaveBeenCalled();
      fixture.componentInstance.patch({ name: ' Anfänger ', weekday: 5, schedule: ' 17:00 ' });
      await fixture.componentInstance.create();
      expect(api.createGroup).toHaveBeenCalledWith({ name: 'Anfänger', weekday: 5, schedule: '17:00', archived: false });
      expect(router.navigate).toHaveBeenCalledWith(['/gruppen', 9]);
    });

    it('scheduleText: was da ist, mit Komma', () => {
      expect(scheduleText({ weekday: 5, schedule: ' 17:00 ' })).toBe('Freitag, 17:00');
      expect(scheduleText({ weekday: null, schedule: 'nach Absprache' })).toBe('nach Absprache');
      expect(scheduleText({ weekday: 2, schedule: null })).toBe('Dienstag');
      expect(scheduleText({})).toBe('');
    });
  });

  describe('Gruppe', () => {
    async function open(group: Group): Promise<ComponentFixture<GroupPageComponent>> {
      api.group.and.resolveTo(group);
      configure(GroupPageComponent, '1');
      const fixture = TestBed.createComponent(GroupPageComponent);
      await settle(fixture);
      return fixture;
    }

    it('Anwesenheitstabelle: Kinder × Einheiten (älteste links), jedes Datum führt zur Liste dieses Tages', async () => {
      const fixture = await open(GROUP());
      const el = fixture.nativeElement as HTMLElement;
      const heads = Array.from(el.querySelectorAll('.matrix thead th a'));
      expect(heads.map(a => a.textContent)).toEqual(['Fr 18.09.', 'Fr 25.09.']);
      expect(heads[0].getAttribute('href')).toBe('/gruppen/1/anwesenheit?datum=2026-09-18');
      const rows = Array.from(el.querySelectorAll('.matrix tbody tr')).map(tr =>
        [tr.querySelector('.name')!.textContent!.trim().replace(/\s+/g, ' '),
         ...Array.from(tr.querySelectorAll('.cell')).map(c => c.textContent), tr.querySelector('.rate')!.textContent]);
      expect(rows).toEqual([['Auer Anna', '–', '✓', '1 von 2'], ['Ben', '·', '–', '0 von 1']]);
      expect(el.querySelector('.matrix tbody .cell')!.classList).toContain('absent');
      expect(el.querySelector('.legend')!.textContent).not.toContain('entschuldigt');
      expect(el.querySelector('.page-head a.primary')!.getAttribute('href')).toBe('/gruppen/1/anwesenheit');
      expect(el.querySelector('.manage-group')).toBeNull();                               // Trainer ändern die Gruppe nicht
      expect([statusMark('present'), statusMark('absent'), statusMark(null)]).toEqual(['✓', '–', '·']);
    });

    it('Trainer stehen als eigener Block unter den Kindern in der Tabelle, mit Haken und Quote', async () => {
      const fixture = await open(GROUP({ coaches: [{ id: 91, firstName: 'Bernhard', lastName: '', statuses: ['present', 'present'], present: 2, recorded: 2 }] }));
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('.coach-head th')!.textContent).toBe('Trainer');
      const row = el.querySelector('tr.coach')!;
      expect(row.querySelector('.name')!.textContent!.trim()).toBe('Bernhard');
      expect(Array.from(row.querySelectorAll('.cell')).map(c => c.textContent)).toEqual(['✓', '✓']);
      expect(row.querySelector('.rate')!.textContent).toBe('2 von 2');
    });

    it('Trainingstagebuch: je Einheit Thema und „was wurde gemacht", die neueste zuerst', async () => {
      const fixture = await open(GROUP());
      const entries = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.diary > .notes > li'));
      expect(entries.map(li => li.querySelector('a')!.textContent)).toEqual(['Freitag, 25. September 2026', 'Freitag, 18. September 2026']);
      expect(entries[0].textContent).toContain('1 von 2 da');
      expect(entries[0].textContent).toContain('Kein Thema eingetragen.');
      expect(entries[0].querySelector('ch-session-photos .photos li')).not.toBeNull();     // Vorschaubild dabei
      expect(entries[0].querySelector('.photo-del')).toBeNull();                            // im Tagebuch nur ansehen
      expect(entries[1].querySelector('ch-session-photos')).toBeNull();
      expect(entries[1].querySelector('.topic')!.textContent).toBe('Matt in 1');
      expect(entries[1].textContent).toContain('Arbeitsblatt 2, danach Turnier jeder gegen jeden');
      expect(entries[1].querySelector('a')!.getAttribute('href')).toBe('/gruppen/1/anwesenheit?datum=2026-09-18');   // zum Nachtragen
    });

    it('ohne Einheit kein Tagebuch', async () => {
      const fixture = await open(GROUP({ sessions: [], members: [] }));
      expect((fixture.nativeElement as HTMLElement).querySelector('.diary')).toBeNull();
    });

    it('Kinder verwalten: die Kartei lädt erst beim Aufklappen; angeboten wird nur, wer noch nicht drin ist', async () => {
      api.members.and.resolveTo([
        { id: 11, firstName: 'Anna', lastName: 'Auer', archived: false, isTrainer: false, linked: false, groups: [], contacts: [] },
        { id: 30, firstName: 'Dora', lastName: 'Neu', archived: false, isTrainer: false, linked: false, groups: [], contacts: [] },
      ]);
      api.addGroupMember.and.resolveTo(GROUP({ members: [...GROUP().members, { id: 30, firstName: 'Dora', lastName: 'Neu', statuses: [null, null], present: 0, recorded: 0 }] }));
      const fixture = await open(GROUP());
      expect(api.members).not.toHaveBeenCalled();
      fixture.componentInstance.onKidsToggle(true);
      await settle(fixture);
      const el = fixture.nativeElement as HTMLElement;
      expect(Array.from(el.querySelectorAll('.add-kid option')).map(o => o.textContent)).toEqual(['auswählen …', 'Neu Dora']);
      fixture.componentInstance.pick.set(30);
      await fixture.componentInstance.addKid();
      await settle(fixture);
      expect(api.addGroupMember).toHaveBeenCalledWith(1, 30);
      expect(el.querySelectorAll('.matrix tbody tr').length).toBe(3);
      expect(el.querySelector('.manage-kids')!.textContent).toContain('Alle Kinder, die du siehst, sind schon in der Gruppe.');
      fixture.componentInstance.onKidsToggle(true);                                       // zweites Aufklappen lädt nicht erneut
      expect(api.members).toHaveBeenCalledTimes(1);
    });

    it('aus der Gruppe nehmen fragt nach', async () => {
      api.removeGroupMember.and.resolveTo(GROUP({ members: [GROUP().members[0]] }));
      const fixture = await open(GROUP());
      const ask = spyOn(window, 'confirm').and.returnValue(false);
      await fixture.componentInstance.removeKid(12, 'Ben');
      expect(api.removeGroupMember).not.toHaveBeenCalled();
      ask.and.returnValue(true);
      await fixture.componentInstance.removeKid(12, 'Ben');
      expect(api.removeGroupMember).toHaveBeenCalledWith(1, 12);
    });

    it('die Leitung ändert Trainingstag und Namen, teilt Trainer zu — die Begründung des Servers steht da', async () => {
      api.updateGroup.and.callFake(async (_id, input) => GROUP({ canManage: true, ...input }));
      api.addTrainer.and.rejectWith(new HttpErrorResponse({ status: 404, error: { message: 'Ein Konto mit diesem Benutzernamen gibt es nicht.' } }));
      const fixture = await open(GROUP({ canManage: true }));
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('.manage-group')).not.toBeNull();
      expect(el.querySelector<HTMLInputElement>('.manage-group input[name=name]')!.value).toBe('Anfänger');

      fixture.componentInstance.patch({ name: 'Anfänger A', weekday: 2 });
      el.querySelector<HTMLButtonElement>('.save-group')!.click();
      await settle(fixture);
      expect(api.updateGroup).toHaveBeenCalledWith(1, { name: 'Anfänger A', weekday: 2, schedule: '17:00, Vereinsheim', archived: false });
      expect(el.querySelector('h1')!.textContent).toBe('Anfänger A');
      expect(el.querySelector('.page-head .kid-meta')!.textContent).toContain('Dienstag, 17:00, Vereinsheim');

      fixture.componentInstance.trainerName.set(' niemand ');
      await fixture.componentInstance.addTrainer();
      fixture.detectChanges();
      expect(api.addTrainer).toHaveBeenCalledWith(1, 'niemand');
      expect(Array.from(el.querySelectorAll('[role=alert]')).map(a => a.textContent).join('|')).toContain('Ein Konto mit diesem Benutzernamen gibt es nicht.');
    });

    it('Gruppe löschen fragt nach und führt zur Liste', async () => {
      api.deleteGroup.and.resolveTo();
      const fixture = await open(GROUP({ canManage: true }));
      const ask = spyOn(window, 'confirm').and.returnValue(false);
      await fixture.componentInstance.deleteGroup();
      expect(api.deleteGroup).not.toHaveBeenCalled();
      ask.and.returnValue(true);
      await fixture.componentInstance.deleteGroup();
      expect(api.deleteGroup).toHaveBeenCalledWith(1);
      expect(router.navigateByUrl).toHaveBeenCalledWith('/gruppen');
    });

    it('eine fremde Gruppe sieht aus wie eine unbekannte', async () => {
      api.group.and.rejectWith(new HttpErrorResponse({ status: 404, error: { message: 'Diese Gruppe gibt es nicht.' } }));
      configure(GroupPageComponent, '77');
      const fixture = TestBed.createComponent(GroupPageComponent);
      await settle(fixture);
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Gruppe nicht gefunden');
    });
  });
});
