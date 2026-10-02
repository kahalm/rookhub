import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService } from '../../core/club-api.service';
import { GroupRow, MemberRow } from '../../core/club.models';
import { MembersPageComponent, matchesName } from './members-page.component';

const KID = (id: number, firstName: string, lastName: string, extra: Partial<MemberRow> = {}): MemberRow =>
  ({ id, firstName, lastName, birthYear: null, level: null, archived: false, isTrainer: false, linked: false, groups: [], contacts: [], ...extra });
const GROUP = (id: number, name: string, weekday: number | null): GroupRow =>
  ({ id, name, weekday, schedule: null, archived: false, memberCount: 0, sessionCount: 0, lastSession: null, trainers: [] });

describe('MembersPageComponent (Kartei)', () => {
  let fixture: ComponentFixture<MembersPageComponent>;
  let api: jasmine.SpyObj<ClubApiService>;
  let perms: Set<string>;
  const el = () => fixture.nativeElement as HTMLElement;
  const names = () => Array.from(el().querySelectorAll('.kid-name')).map(n => n.textContent?.trim().replace(/\s+/g, ' '));

  async function create(now = new Date(2026, 8, 30, 10, 0)): Promise<void> {   // Mittwoch
    TestBed.configureTestingModule({
      imports: [MembersPageComponent],
      providers: [provideRouter([]), { provide: ClubApiService, useValue: api },
        { provide: AuthService, useValue: { has: (p: string) => perms.has(p) } }],
    });
    fixture = TestBed.createComponent(MembersPageComponent);
    fixture.componentInstance.now = () => now;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    perms = new Set(['club.trainer']);
    api = jasmine.createSpyObj<ClubApiService>('ClubApiService', ['members', 'groups', 'memberPhotoBlob']);
    api.memberPhotoBlob.and.resolveTo(new Blob(['x'], { type: 'image/jpeg' }));
    api.groups.and.resolveTo([GROUP(1, 'Anfänger', 5), GROUP(2, 'Turnier', 2)]);
    api.members.and.resolveTo([
      KID(1, 'Anna', 'Auer', { birthYear: 2016, level: 'Bauerndiplom', groups: [{ id: 1, name: 'Anfänger' }],
        contacts: [{ kind: 'email', value: 'auer@example.org', label: 'Mutter' },
                   { kind: 'phone', value: '0660 111 22 33', label: 'Mutter Daniela' }, { kind: 'phone', value: '0512/58 12 34', label: 'Vater Franz' }] }),
      KID(2, 'Ben', 'Berger', { groups: [{ id: 2, name: 'Turnier' }] }),
      KID(3, 'Ivo', 'Šarić', { groups: [{ id: 1, name: 'Anfänger' }] }),
    ]);
  });

  it('ohne Club-Recht nur der Hinweis, kein Abruf — mit dem Weg zum Verknüpfen', async () => {
    perms.clear();
    await create();
    expect(el().textContent).toContain('Nicht freigeschaltet');
    expect(el().querySelector('a[href="/verknuepfen"]')).not.toBeNull();
    expect(api.members).not.toHaveBeenCalled();
  });

  it('zeigt die Kartei in Registern; die erste TELEFONNUMMER steht mit ihrem Hinweis zum Antippen in der Zeile', async () => {
    await create();
    expect(Array.from(el().querySelectorAll('.letter')).map(l => l.textContent)).toEqual(['A', 'B', 'S']);
    expect(names()).toEqual(['Auer Anna', 'Berger Ben', 'Šarić Ivo']);
    const call = el().querySelector('.kid-call')!;
    expect(call.querySelector('a')!.getAttribute('href')).toBe('tel:06601112233');     // nicht die E-Mail davor
    expect(call.querySelector('a')!.textContent).toBe('0660 111 22 33');
    expect(call.querySelector('span')!.textContent).toBe('Mutter Daniela');
    expect(el().querySelectorAll('.kid-call').length).toBe(1);                          // ohne Nummer kein leerer Kasten
    expect(el().querySelector('.kid-meta')!.textContent).toContain('Jg. 2016 (U10)');
    expect(el().querySelector('.count')!.textContent).toBe('3 Kinder');
  });

  it('ein Kind ohne Nachnamen steht unter dem Buchstaben seines VORNAMENS, fett wie sonst der Nachname', async () => {
    api.members.and.resolveTo([KID(1, 'Anna', 'Auer'), KID(4, 'Emil', ''), KID(2, 'Ivo', 'Šarić')]);
    await create();
    expect(Array.from(el().querySelectorAll('.letter')).map(l => l.textContent)).toEqual(['A', 'E', 'S']);
    expect(names()).toEqual(['Auer Anna', 'Emil', 'Šarić Ivo']);
    expect(el().querySelectorAll('.kid-name b')[1].textContent).toBe('Emil');
    fixture.componentInstance.q.set('emi');
    fixture.detectChanges();
    expect(names()).toEqual(['Emil']);
  });

  it('Trainer stehen als eigener Block unter den Registern, mit Nummer; der Gruppenfilter lässt sie stehen', async () => {
    api.members.and.resolveTo([KID(1, 'Anna', 'Auer', { groups: [{ id: 1, name: 'Anfänger' }] }),
      KID(9, 'Bernhard', '', { isTrainer: true, contacts: [{ kind: 'phone', value: '0664 1', label: null }] }), KID(8, 'Georg', 'Auer', { isTrainer: true })]);
    await create();
    expect(Array.from(el().querySelectorAll('.register:not(.coaches) .letter')).map(l => l.textContent)).toEqual(['A']);
    expect(Array.from(el().querySelectorAll('.coaches .kid-name')).map(n => n.textContent?.trim().replace(/\s+/g, ' '))).toEqual(['Bernhard', 'Auer Georg']);   // Reihenfolge vom Server
    expect(el().querySelector('.coaches .kid-call a')!.getAttribute('href')).toBe('tel:06641');
    expect(el().querySelector('.count')!.textContent).toBe('1 Kind, 2 Trainer');
    fixture.componentInstance.groupId.set(2);
    fixture.detectChanges();
    expect(el().querySelectorAll('.register:not(.coaches) .kid').length).toBe(0);
    expect(el().querySelectorAll('.coaches .kid').length).toBe(2);
    expect(el().querySelector('a[href="/kind/neu?trainer=1"]')).not.toBeNull();
  });

  it('sucht nach Namen (ohne Akzente) und filtert nach Gruppe — im Browser, ohne neuen Abruf', async () => {
    await create();
    const c = fixture.componentInstance;
    c.q.set('saric');
    fixture.detectChanges();
    expect(names()).toEqual(['Šarić Ivo']);
    expect(el().querySelector('.count')!.textContent).toBe('1 von 3 Kindern');
    c.q.set('');
    c.groupId.set(1);
    fixture.detectChanges();
    expect(names()).toEqual(['Auer Anna', 'Šarić Ivo']);
    c.q.set('niemand');
    fixture.detectChanges();
    expect(el().textContent).toContain('Niemand passt zu dieser Suche.');
    expect(api.members).toHaveBeenCalledTimes(1);
    expect(matchesName({ firstName: 'Anna', lastName: 'Auer' }, 'auer an')).toBeTrue();   // „Nachname Vorname" geht auch
    expect(matchesName({ firstName: 'Anna', lastName: 'Auer' }, 'anna au')).toBeTrue();
  });

  it('Gruppenfilter als Knöpfe: je Gruppe mit der Zahl ihrer Kinder, ein Tipp filtert, ein zweiter zeigt wieder alle', async () => {
    await create();
    const chips = () => Array.from(el().querySelectorAll<HTMLButtonElement>('.chip-btn'));
    expect(el().querySelector('.tools select')).toBeNull();                               // keine Auswahlliste mehr
    expect(chips().map(c => c.textContent!.trim().replace(/\s+/g, ' '))).toEqual(['Alle', 'Anfänger 2', 'Turnier 1']);
    expect(chips().map(c => c.getAttribute('aria-pressed'))).toEqual(['true', 'false', 'false']);

    chips()[1].click();
    fixture.detectChanges();
    expect(names()).toEqual(['Auer Anna', 'Šarić Ivo']);
    expect(chips().map(c => c.getAttribute('aria-pressed'))).toEqual(['false', 'true', 'false']);
    expect(el().querySelector('.count')!.textContent).toBe('2 von 3 Kindern');

    chips()[2].click();
    fixture.detectChanges();
    expect(names()).toEqual(['Berger Ben']);
    chips()[2].click();                                                                  // noch einmal dieselbe = alle
    fixture.detectChanges();
    expect(names()).toEqual(['Auer Anna', 'Berger Ben', 'Šarić Ivo']);
    chips()[1].click();
    chips()[0].click();                                                                  // „Alle"
    fixture.detectChanges();
    expect(fixture.componentInstance.groupId()).toBeNull();
    expect(api.members).toHaveBeenCalledTimes(1);
  });

  it('eine archivierte Gruppe bekommt nur dann einen Knopf, wenn Blätter der Liste in ihr sind', async () => {
    api.groups.and.resolveTo([GROUP(1, 'Anfänger', 5), { ...GROUP(3, 'Alt', null), archived: true }, { ...GROUP(4, 'Uralt', null), archived: true }]);
    api.members.and.resolveTo([KID(1, 'Anna', 'Auer', { groups: [{ id: 3, name: 'Alt' }] })]);
    await create();
    expect(fixture.componentInstance.chips()).toEqual([{ id: 1, name: 'Anfänger', count: 0 }, { id: 3, name: 'Alt', count: 1 }]);
  });

  it('Bilder: ohne ein einziges Bild bleibt die Liste ohne Porträts; mit einem steht vor jedem Namen eines (sonst der Anfangsbuchstabe)', async () => {
    await create();
    expect(el().querySelector('ch-member-photo')).toBeNull();
    expect(api.memberPhotoBlob).not.toHaveBeenCalled();

    TestBed.resetTestingModule();
    spyOn(URL, 'createObjectURL').and.returnValue('blob:bild-1');
    api.members.and.resolveTo([KID(1, 'Anna', 'Auer', { photoVersion: 1759400000000 }), KID(2, 'Ben', 'Berger'), KID(9, 'Georg', 'Auer', { isTrainer: true })]);
    await create();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el().querySelectorAll('ch-member-photo').length).toBe(3);                      // Kinder UND Trainer
    expect(api.memberPhotoBlob.calls.allArgs()).toEqual([[1, true, 1759400000000]]);      // nur wer ein Bild hat, nur das Vorschaubild
    const kids = Array.from(el().querySelectorAll('.register:not(.coaches) .kid'));
    expect(kids[0].querySelector('.avatar img')!.getAttribute('src')).toBe('blob:bild-1');
    expect(kids[1].querySelector('.avatar img')).toBeNull();
    expect(kids[1].querySelector('.avatar-ph')!.textContent).toBe('B');
    expect(names()).toEqual(['Auer Anna', 'Berger Ben', 'Auer Georg']);                   // die Namen stehen wie vorher
  });

  it('am Trainingstag steht oben der Streifen zur Anwesenheitsliste — nur für die Gruppe, die heute trainiert', async () => {
    await create(new Date(2026, 8, 25, 16, 0));                                          // Freitag
    const strips = Array.from(el().querySelectorAll('.today'));
    expect(strips.length).toBe(1);
    expect(strips[0].textContent).toContain('Heute ist Freitag: Anfänger trainiert.');
    expect(strips[0].querySelector('a')!.getAttribute('href')).toBe('/gruppen/1/anwesenheit');
  });

  it('an einem anderen Tag kein Streifen; „Archiv" lädt die archivierten Blätter', async () => {
    await create();
    expect(el().querySelector('.today')).toBeNull();
    api.members.and.resolveTo([]);
    fixture.componentInstance.toggleArchived(true);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.members.calls.mostRecent().args).toEqual([true]);
    expect(el().textContent).toContain('Im Archiv liegt kein Blatt.');
  });

  it('leere Kartei lädt zum Anlegen ein; ein Fehler beim Laden steht da', async () => {
    api.members.and.resolveTo([]);
    await create();
    expect(el().querySelector('.empty a')!.getAttribute('href')).toBe('/kind/neu');
    api.members.and.rejectWith(new Error('offline'));
    await fixture.componentInstance.load();
    fixture.detectChanges();
    expect(el().querySelector('[role=alert]')!.textContent).toContain('Die Kartei konnte nicht geladen werden.');
  });
});
