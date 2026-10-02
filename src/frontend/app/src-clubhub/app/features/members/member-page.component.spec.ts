import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService } from '../../core/club-api.service';
import { GroupRow, Member, MemberInput } from '../../core/club.models';
import { MemberPageComponent, formatDay } from './member-page.component';
import { of } from 'rxjs';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';

const MEMBER = (extra: Partial<Member> = {}): Member => ({
  id: 7, firstName: 'Daniel', lastName: 'Huber', birthYear: 2015, birthDate: '2015-03-12', level: 'Bauerndiplom', archived: false, isTrainer: false,
  linked: false, groups: [{ id: 1, name: 'Anfänger' }],
  contacts: [{ kind: 'phone', value: '0660 111 22 33', label: 'Mutter Daniela' }, { kind: 'phone', value: '0512/58 12 34', label: 'Vater Franz' },
             { kind: 'email', value: 'daniela@example.org', label: null }],
  linkedUsername: null, linkCode: null, linkCodeExpires: null,
  createdAt: '2026-09-30T10:00:00Z', updatedAt: '2026-09-30T10:00:00Z',
  noteEntries: [{ id: 3, text: 'kann die Gabel', createdAt: '2026-09-25T16:00:00Z', author: 'tina', canDelete: true }],
  attendance: { present: 8, absent: 2, recent: [{ sessionId: 5, groupId: 1, group: 'Anfänger', date: '2026-09-25', topic: 'Gabel', status: 'present' }] },
  canDelete: false, ...extra,
});
const GROUPS: GroupRow[] = [
  { id: 1, name: 'Anfänger', weekday: 5, schedule: null, archived: false, memberCount: 0, sessionCount: 0, trainers: [] },
  { id: 2, name: 'Turnier', weekday: 2, schedule: null, archived: false, memberCount: 0, sessionCount: 0, trainers: [] },
];

describe('MemberPageComponent (Karteiblatt)', () => {
  let fixture: ComponentFixture<MemberPageComponent>;
  let api: jasmine.SpyObj<ClubApiService>;
  /** Rückfrage (ConfirmService) — Vorgabe „ja", je Test umstellbar. */
  let confirmAsk: jasmine.Spy;
  let router: Router;
  const el = () => fixture.nativeElement as HTMLElement;

  async function create(id: string | null, query: Record<string, string> = {}): Promise<void> {
    TestBed.configureTestingModule({
      imports: [MemberPageComponent],
      providers: [{ provide: ConfirmService, useValue: { ask: (...a: unknown[]) => confirmAsk(...a) } }, provideRouter([]), { provide: ClubApiService, useValue: api },
        { provide: AuthService, useValue: { has: (p: string) => p === 'club.trainer' } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}), queryParamMap: convertToParamMap(query) } } }],
    });
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
    fixture = TestBed.createComponent(MemberPageComponent);
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function type(selector: string, value: string, index = 0): void {
    const input = el().querySelectorAll<HTMLInputElement>(selector)[index];
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  beforeEach(() => {
    confirmAsk = jasmine.createSpy('ask').and.returnValue(of(true));
    api = jasmine.createSpyObj<ClubApiService>('ClubApiService',
      ['member', 'groups', 'createMember', 'updateMember', 'deleteMember', 'addNote', 'deleteNote', 'createLinkCode', 'unlink', 'progress',
       'uploadMemberPhoto', 'deleteMemberPhoto', 'memberPhotoBlob']);
    api.memberPhotoBlob.and.resolveTo(new Blob(['x'], { type: 'image/jpeg' }));
    api.groups.and.resolveTo(GROUPS);
    api.member.and.resolveTo(MEMBER());
  });

  it('Anlegen: mehrere Telefonnummern und eine E-Mail, jede mit Hinweis — so gehen sie an den Server', async () => {
    api.createMember.and.callFake(async (input: MemberInput) => MEMBER({ id: 12, contacts: input.contacts }));
    await create(null, { gruppe: '1' });
    expect(el().querySelector('h1')!.textContent).toBe('Kind anlegen');

    type('input[name=firstName]', ' Daniel ');
    type('input[name=lastName]', 'Huber');
    type('input[name=birth]', '12.3.2015');
    el().querySelector<HTMLButtonElement>('.add-phone')!.click();
    el().querySelector<HTMLButtonElement>('.add-phone')!.click();
    el().querySelector<HTMLButtonElement>('.add-phone')!.click();                       // bleibt leer → fällt weg
    el().querySelector<HTMLButtonElement>('.add-email')!.click();
    fixture.detectChanges();
    expect(el().querySelectorAll('.contact-edit').length).toBe(4);
    const c = fixture.componentInstance;
    c.setContact(0, { value: '0660 111 22 33', label: 'Mutter Daniela' });
    c.setContact(1, { value: '0512/58 12 34', label: 'Vater Franz' });
    c.setContact(3, { value: 'daniela@example.org', label: ' ' });
    fixture.detectChanges();

    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();

    const sent = api.createMember.calls.mostRecent().args[0];
    expect(sent.contacts).toEqual([
      { kind: 'phone', value: '0660 111 22 33', label: 'Mutter Daniela' },
      { kind: 'phone', value: '0512/58 12 34', label: 'Vater Franz' },
      { kind: 'email', value: 'daniela@example.org', label: null },
    ]);
    expect([sent.firstName, sent.lastName, sent.birthDate, sent.birthYear, sent.groupIds]).toEqual(['Daniel', 'Huber', '2015-03-12', 2015, [1]]);
    expect(router.navigate).toHaveBeenCalledWith(['/kind', 12], { replaceUrl: true });
  });

  it('die Speichern-Leiste klebt am unteren Rand — egal, wo man im Formular gerade ist', async () => {
    await create(null);
    const bar = el().querySelector<HTMLElement>('.form-save')!;
    expect(bar.querySelector('button[type=submit]')!.textContent).toBe('Kind anlegen');
    expect(getComputedStyle(bar).position).toBe('sticky');                             // aus clubhub.scss, das Karma mitlädt
    expect(getComputedStyle(bar).bottom).toBe('0px');
  });

  it('Trainer anlegen: ?trainer=1 wählt „Trainer" vor, Stufe und Gruppen fallen weg, isTrainer geht mit', async () => {
    api.createMember.and.callFake(async (input: MemberInput) => MEMBER({ id: 21, firstName: input.firstName, lastName: '', isTrainer: input.isTrainer, level: null, groups: [], contacts: [] }));
    await create(null, { trainer: '1' });
    expect(el().querySelector('h1')!.textContent).toBe('Trainer anlegen');
    expect(el().querySelector<HTMLInputElement>('input[name=isTrainer][value], input[name=isTrainer]:checked')).not.toBeNull();
    expect(el().querySelector('input[name=level]')).toBeNull();
    type('input[name=firstName]', 'Bernhard');
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
    const sent = api.createMember.calls.mostRecent().args[0];
    expect([sent.firstName, sent.isTrainer, sent.groupIds]).toEqual(['Bernhard', true, []]);
    // Das Blatt eines Trainers: Kontakte und Anwesenheit ja, Lernstand und Konto nicht.
    expect(el().querySelector('.sub .chip')!.textContent).toBe('Trainer');
    expect(el().querySelector('.note-input')!.closest('section')!.hasAttribute('hidden')).toBeTrue();
    expect(el().querySelector('.link')!.hasAttribute('hidden')).toBeTrue();
    expect(el().querySelector('.contacts, .sheet section:not([hidden]) h2')!.textContent).toBe('Kontakte');
  });

  it('Anlegen: der Nachname ist optional — der Vorname genügt', async () => {
    api.createMember.and.callFake(async (input: MemberInput) => MEMBER({ id: 13, firstName: input.firstName, lastName: input.lastName, contacts: [] }));
    await create(null);
    expect(el().textContent).toContain('Nachname (wenn bekannt)');
    type('input[name=firstName]', 'Emil');
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
    const sent = api.createMember.calls.mostRecent().args[0];
    expect([sent.firstName, sent.lastName]).toEqual(['Emil', '']);
    expect(el().querySelector('h1')!.textContent).toBe('Emil');                          // kein hängendes Leerzeichen, kein „undefined"
  });

  it('Anlegen: ohne Vornamen oder mit unlesbarem Geburtsdatum geht nichts an den Server', async () => {
    await create(null);
    type('input[name=lastName]', 'Huber');
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
    expect(el().querySelector('[role=alert]')!.textContent).toContain('Der Vorname fehlt noch.');

    type('input[name=firstName]', 'Daniel');
    type('input[name=lastName]', 'Huber');
    type('input[name=birth]', 'März 2015');
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
    expect(el().querySelector('[role=alert]')!.textContent).toContain('12.3.2015');
    expect(api.createMember).not.toHaveBeenCalled();
  });

  it('Anlegen: die Begründung des Servers steht da, das Formular bleibt offen', async () => {
    api.createMember.and.rejectWith(new HttpErrorResponse({ status: 400, error: { message: '„abc“ ist keine Telefonnummer.' } }));
    await create(null);
    type('input[name=firstName]', 'Daniel');
    type('input[name=lastName]', 'Huber');
    fixture.componentInstance.addContact('phone');
    fixture.componentInstance.setContact(0, { value: 'abc' });
    await fixture.componentInstance.save();
    fixture.detectChanges();
    expect(el().querySelector('[role=alert]')!.textContent).toContain('„abc“ ist keine Telefonnummer.');
    expect(el().querySelector('form')).not.toBeNull();
  });

  /** Eine Datei ins Bildfeld des Formulars legen, wie es die Dateiauswahl täte. */
  function pick(name = 'anna.jpg'): File {
    const file = new File(['bild'], name, { type: 'image/jpeg' });
    const input = el().querySelector<HTMLInputElement>('input[name=photo]')!;
    const dt = new DataTransfer();
    dt.items.add(file);
    input.files = dt.files;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    return file;
  }

  it('„Weiteres Kind anlegen" speichert das Blatt und öffnet gleich ein leeres — die Gruppe bleibt angehakt', async () => {
    let id = 30;
    api.createMember.and.callFake(async (input: MemberInput) => MEMBER({ id: ++id, firstName: input.firstName, lastName: input.lastName, contacts: [] }));
    await create(null, { gruppe: '1' });
    const next = () => el().querySelector<HTMLButtonElement>('.save-next')!;
    expect(next().textContent).toBe('Weiteres Kind anlegen');
    type('input[name=firstName]', 'Anna');
    type('input[name=lastName]', 'Auer');
    type('input[name=birth]', '2016');
    type('input[name=level]', 'Bauerndiplom');
    next().click();
    await settle();

    expect(api.createMember).toHaveBeenCalledTimes(1);
    expect(router.navigate).not.toHaveBeenCalled();                                      // kein Wechsel aufs Blatt
    expect(el().querySelector('h1')!.textContent).toBe('Kind anlegen');
    expect(['firstName', 'lastName', 'birth', 'level'].map(n => el().querySelector<HTMLInputElement>(`input[name=${n}]`)!.value)).toEqual(['', '', '', '']);
    expect(fixture.componentInstance.form().groupIds).toEqual([1]);
    const note = el().querySelector('.saved-note')!;
    expect(note.textContent).toContain('Anna Auer ist gespeichert');
    expect(note.querySelector('a')!.getAttribute('href')).toBe('/kind/31');
    expect(el().querySelector('.form-save .btn:last-child')!.textContent).toBe('Fertig');

    type('input[name=firstName]', 'Ben');
    next().click();
    await settle();
    expect(api.createMember.calls.allArgs().map(a => [a[0].firstName, a[0].groupIds])).toEqual([['Anna', [1]], ['Ben', [1]]]);
    expect(el().querySelector('.saved-note')!.textContent).toContain('Ben ist gespeichert');   // ohne Nachnamen nur der Vorname
  });

  it('„Weiteres Kind anlegen" ohne Vornamen speichert nichts; beim Ändern eines Blatts gibt es den Knopf nicht', async () => {
    await create(null);
    el().querySelector<HTMLButtonElement>('.save-next')!.click();
    await settle();
    expect(api.createMember).not.toHaveBeenCalled();
    expect(el().querySelector('[role=alert]')!.textContent).toContain('Der Vorname fehlt noch.');

    TestBed.resetTestingModule();
    await create('7');
    fixture.componentInstance.startEdit();
    await settle();
    expect(el().querySelector('.save-next')).toBeNull();
    expect(el().querySelector('.form-save .btn:last-child')!.textContent).toBe('Abbrechen');
  });

  it('Personennummer und FIDE-Nummer: gehen mit, stehen am Blatt — die FIDE-Nummer nur aus Ziffern', async () => {
    api.createMember.and.callFake(async (input: MemberInput) => MEMBER({ id: 14, fideId: input.fideId, nationalId: input.nationalId }));
    await create(null);
    type('input[name=firstName]', 'Daniel');
    type('input[name=nationalId]', ' 123456 ');
    type('input[name=fideId]', '16 55 000');
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
    expect(el().querySelector('[role=alert]')!.textContent).toContain('Die FIDE-Nummer besteht nur aus Ziffern.');
    expect(api.createMember).not.toHaveBeenCalled();

    type('input[name=fideId]', ' 1655000 ');
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
    const sent = api.createMember.calls.mostRecent().args[0];
    expect([sent.nationalId, sent.fideId]).toEqual(['123456', '1655000']);
    expect(el().querySelector('.sub .pnr')!.textContent).toBe('PNr. 123456');
    const fide = el().querySelector<HTMLAnchorElement>('.sub .fide')!;
    expect([fide.textContent, fide.getAttribute('href')]).toEqual(['FIDE 1655000', 'https://ratings.fide.com/profile/1655000']);
  });

  it('leer gelassene Nummern gehen als „nicht angegeben" (null); beim Ändern stehen die vorhandenen im Formular', async () => {
    api.member.and.resolveTo(MEMBER({ fideId: '1655000', nationalId: '123456' }));
    api.updateMember.and.callFake(async (_id: number, input: MemberInput) => MEMBER({ fideId: input.fideId, nationalId: input.nationalId }));
    await create('7');
    fixture.componentInstance.startEdit();
    await settle();
    expect(['nationalId', 'fideId'].map(n => el().querySelector<HTMLInputElement>(`input[name=${n}]`)!.value)).toEqual(['123456', '1655000']);
    type('input[name=fideId]', '  ');
    await fixture.componentInstance.save();
    const sent = api.updateMember.calls.mostRecent().args[1];
    expect([sent.nationalId, sent.fideId]).toEqual(['123456', null]);
  });

  it('Bild beim Anlegen: Vorschau im Formular, hochgeladen wird NACH dem Speichern des Blatts — danach steht das Porträt am Blatt', async () => {
    spyOn(URL, 'createObjectURL').and.returnValue('blob:vorschau');
    const revoke = spyOn(URL, 'revokeObjectURL');
    api.createMember.and.callFake(async (input: MemberInput) => MEMBER({ id: 12, firstName: input.firstName }));
    api.uploadMemberPhoto.and.resolveTo({ photoVersion: 1759400000000 });
    await create(null);
    expect(el().querySelector('.pick-photo')!.textContent!.trim()).toBe('Bild wählen');
    expect(el().querySelector('.photo-field .avatar-ph')!.textContent).toBe('kein Bild');
    expect(el().querySelector('.drop-photo')).toBeNull();

    const file = pick();
    expect(el().querySelector('.photo-field img')!.getAttribute('src')).toBe('blob:vorschau');
    expect(el().querySelector('.pick-photo')!.textContent!.trim()).toBe('Anderes Bild wählen');
    expect(el().querySelector<HTMLInputElement>('input[name=photo]')!.value).toBe('');
    expect(api.uploadMemberPhoto).not.toHaveBeenCalled();                                // noch gibt es kein Blatt

    type('input[name=firstName]', 'Anna');
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
    expect(api.uploadMemberPhoto).toHaveBeenCalledOnceWith(12, file);
    expect(api.createMember).toHaveBeenCalledBefore(api.uploadMemberPhoto);
    expect(revoke).toHaveBeenCalledWith('blob:vorschau');
    expect(fixture.componentInstance.member()!.photoVersion).toBe(1759400000000);
    expect(el().querySelector('.sheet-head')!.classList).toContain('has-photo');
    expect(el().querySelector('.sheet-head ch-member-photo')).not.toBeNull();
    expect(api.memberPhotoBlob).toHaveBeenCalledWith(12, true, 1759400000000);
    expect(router.navigate).toHaveBeenCalledWith(['/kind', 12], { replaceUrl: true });
  });

  it('Bild scheitert: das Blatt IST gespeichert — das Formular bleibt als „Blatt ändern" offen, der zweite Versuch legt kein zweites Kind an', async () => {
    api.createMember.and.callFake(async (input: MemberInput) => MEMBER({ id: 12, firstName: input.firstName, lastName: '' }));
    api.updateMember.and.callFake(async (_id: number, input: MemberInput) => MEMBER({ id: 12, firstName: input.firstName, lastName: '' }));
    api.uploadMemberPhoto.and.returnValues(
      Promise.reject(new HttpErrorResponse({ status: 400, error: { message: 'Das ist kein Bild, das sich lesen lässt (JPEG, PNG oder WebP).' } })),
      Promise.resolve({ photoVersion: 5 }));
    await create(null);
    type('input[name=firstName]', 'Anna');
    pick('kaputt.jpg');
    el().querySelector<HTMLButtonElement>('.save-next')!.click();                       // auch über „Weiteres Kind": erst muss das Bild sitzen
    await settle();

    expect(el().querySelector('[role=alert]')!.textContent).toContain('Das Blatt ist gespeichert, das Bild aber nicht: Das ist kein Bild');
    expect(el().querySelector('h1')!.textContent).toBe('Blatt ändern');
    expect(el().querySelector<HTMLInputElement>('input[name=firstName]')!.value).toBe('Anna');
    expect(el().querySelector('.photo-field img')).not.toBeNull();                       // die Auswahl bleibt für den nächsten Versuch
    expect(el().querySelector('.save-next')).toBeNull();
    expect(router.navigate).not.toHaveBeenCalled();

    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
    expect(api.createMember).toHaveBeenCalledTimes(1);
    expect(api.updateMember).toHaveBeenCalledTimes(1);
    expect(api.uploadMemberPhoto).toHaveBeenCalledTimes(2);
    expect(el().querySelector('form')).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/kind', 12], { replaceUrl: true });   // von /kind/neu weiter aufs Blatt
  });

  it('Bild beim Ändern: das vorhandene steht im Formular; „Bild entfernen" löscht es beim Speichern — ohne Änderung passiert am Bild nichts', async () => {
    api.member.and.resolveTo(MEMBER({ photoVersion: 5 }));
    api.updateMember.and.callFake(async () => MEMBER({ photoVersion: 5 }));
    api.deleteMemberPhoto.and.resolveTo();
    await create('7');
    await settle();                                                                      // das Vorschaubild kommt als Blob nach
    expect(el().querySelector('.sheet-head ch-member-photo .avatar-open')).not.toBeNull(); // am Blatt lässt es sich groß zeigen
    fixture.componentInstance.startEdit();
    await settle();
    expect(el().querySelector('.photo-field ch-member-photo')).not.toBeNull();
    await fixture.componentInstance.save();
    await settle();
    expect(api.uploadMemberPhoto).not.toHaveBeenCalled();
    expect(api.deleteMemberPhoto).not.toHaveBeenCalled();

    fixture.componentInstance.startEdit();
    await settle();
    el().querySelector<HTMLButtonElement>('.drop-photo')!.click();
    fixture.detectChanges();
    expect(el().querySelector('.photo-field ch-member-photo')).toBeNull();
    expect(el().querySelector('.photo-field .avatar-ph')!.textContent).toBe('kein Bild');
    fixture.componentInstance.cancel();                                                  // abgebrochen = das Bild bleibt
    fixture.componentInstance.startEdit();
    await settle();
    expect(el().querySelector('.photo-field ch-member-photo')).not.toBeNull();

    el().querySelector<HTMLButtonElement>('.drop-photo')!.click();
    await fixture.componentInstance.save();
    await settle();
    expect(api.deleteMemberPhoto).toHaveBeenCalledOnceWith(7);
    expect(fixture.componentInstance.member()!.photoVersion).toBeNull();
    expect(el().querySelector('.sheet-head ch-member-photo')).toBeNull();
  });

  it('Ansehen: jede Nummer ist ein Anruf-Link mit dem Hinweis, wessen sie ist; die E-Mail ein mailto', async () => {
    await create('7');
    expect(el().querySelector('h1')!.textContent).toBe('Daniel Huber');
    expect(el().querySelector('.sub')!.textContent).toContain('Geboren 12.03.2015, U12');
    expect(el().textContent).not.toContain('Weitere Angaben');                          // keinen eigenen Abschnitt dafür
    expect(el().querySelector('ch-member-photo')).toBeNull();                           // ohne Bild kein Porträt, kein Platzhalter
    expect(el().querySelector('.sub .pnr, .sub .fide')).toBeNull();                     // ohne Nummern keine leeren Angaben
    const rows = Array.from(el().querySelectorAll('.contacts li')).map(li =>
      [li.querySelector('.whose')!.textContent, li.querySelector('a')!.getAttribute('href')]);
    expect(rows).toEqual([['Mutter Daniela', 'tel:06601112233'], ['Vater Franz', 'tel:0512581234'], ['E-Mail', 'mailto:daniela@example.org']]);
    expect(el().textContent).toContain('8 von 10 Einheiten da.');
    expect(el().textContent).toContain('kann die Gabel');
    expect(api.progress).not.toHaveBeenCalled();                                         // nicht verknüpft → kein Abruf
  });

  it('Ändern: das Formular kommt mit dem Bestand, ein Kontakt lässt sich entfernen', async () => {
    api.updateMember.and.callFake(async (_id: number, input: MemberInput) => MEMBER({ contacts: input.contacts, level: input.level }));
    await create('7');
    el().querySelector<HTMLButtonElement>('.edit')!.click();
    await settle();
    expect(el().querySelector<HTMLInputElement>('input[name=birth]')!.value).toBe('12.03.2015');
    expect(el().querySelectorAll('.contact-edit').length).toBe(3);
    fixture.componentInstance.removeContact(1);
    type('input[name=level]', 'Turmdiplom');
    await fixture.componentInstance.save();
    await settle();

    const [id, sent] = api.updateMember.calls.mostRecent().args;
    expect(id).toBe(7);
    expect(sent.contacts.map(c => c.value)).toEqual(['0660 111 22 33', 'daniela@example.org']);
    expect([sent.level, sent.birthDate, sent.groupIds]).toEqual(['Turmdiplom', '2015-03-12', [1]]);
    expect(el().querySelector('form')).toBeNull();                                       // zurück in der Ansicht
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('Notiz: speichern leert das Feld; löschen fragt nach', async () => {
    api.addNote.and.resolveTo(MEMBER({ noteEntries: [{ id: 4, text: 'rechnet zwei Züge', createdAt: '2026-09-30T10:00:00Z', author: 'tina', canDelete: true }] }));
    api.deleteNote.and.resolveTo(MEMBER({ noteEntries: [] }));
    await create('7');
    type('.note-input', ' rechnet zwei Züge ');
    el().querySelector<HTMLButtonElement>('.add-note')!.click();
    await settle();
    expect(api.addNote).toHaveBeenCalledWith(7, 'rechnet zwei Züge');
    expect(el().querySelector<HTMLInputElement>('.note-input')!.value).toBe('');
    expect(el().textContent).toContain('rechnet zwei Züge');

    const ask = confirmAsk.and.returnValue(of(false));
    await fixture.componentInstance.deleteNote(4);
    expect(api.deleteNote).not.toHaveBeenCalled();
    ask.and.returnValue(of(true));
    await fixture.componentInstance.deleteNote(4);
    expect(api.deleteNote).toHaveBeenCalledWith(7, 4);
  });

  it('Konto: der Code steht in zwei Gruppen da, samt Link zum Einlösen', async () => {
    api.createLinkCode.and.resolveTo({ code: 'ABCDEFGHJK', expires: '2026-10-14T10:00:00Z' });
    await create('7');
    el().querySelector<HTMLButtonElement>('.new-code')!.click();
    await settle();
    expect(el().querySelector('.code')!.textContent).toBe('ABCDE-FGHJK');
    expect(el().querySelector('.link a')!.getAttribute('href')).toBe(`${location.origin}/verknuepfen?code=ABCDE-FGHJK`);
    expect(el().querySelector('.link')!.textContent).toContain('Gültig bis 14.10.2026');
  });

  it('Konto: verknüpft → der Lernstand aus dem Konto wird geholt und gezeigt', async () => {
    api.member.and.resolveTo(MEMBER({ linked: true, linkedUsername: 'daniel2015' }));
    api.progress.and.resolveTo({ username: 'daniel2015', puzzleAttempts: 50, puzzlesSolved: 40, puzzleAccuracy: 80, puzzleElo: 1130, bestStreak: 9,
      minutes28: 95, activeDays28: 6, lastActive: '2026-09-28', kidsLevelsDone: 4, kidsStars: 10, kidsCourseLines: 0 });
    await create('7');
    expect(api.progress).toHaveBeenCalledWith(7);
    const stats = Array.from(el().querySelectorAll('.stats div')).map(d => `${d.querySelector('dd')!.textContent} ${d.querySelector('dt')!.textContent}`);
    expect(stats).toEqual(['95 Trainingsminuten in 4 Wochen', '6 Trainingstage in 4 Wochen', '40 Puzzles gelöst', '1130 Puzzle-Wertung', '4 KidHub-Stufen geschafft']);
    expect(el().querySelector('.link')!.textContent).toContain('daniel2015');
    expect(el().querySelector('.link')!.textContent).toContain('Zuletzt trainiert am Mo 28.09.');
  });

  it('Löschen bietet das Formular nur der Leitung an — und erst nach Rückfrage', async () => {
    await create('7');
    fixture.componentInstance.startEdit();
    await settle();
    expect(el().textContent).not.toContain('Blatt löschen');

    api.member.and.resolveTo(MEMBER({ canDelete: true }));
    api.deleteMember.and.resolveTo();
    TestBed.resetTestingModule();
    await create('7');
    fixture.componentInstance.startEdit();
    await settle();
    expect(el().textContent).toContain('Blatt löschen');
    const ask = confirmAsk.and.returnValue(of(false));
    await fixture.componentInstance.remove();
    expect(api.deleteMember).not.toHaveBeenCalled();
    ask.and.returnValue(of(true));
    await fixture.componentInstance.remove();
    expect(api.deleteMember).toHaveBeenCalledWith(7);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('ein unbekanntes (oder fremdes) Blatt: Hinweis mit dem Weg zurück', async () => {
    api.member.and.rejectWith(new HttpErrorResponse({ status: 404, error: { message: 'Dieses Kind gibt es nicht.' } }));
    await create('99');
    expect(el().textContent).toContain('Blatt nicht gefunden');
    expect(el().textContent).toContain('Dieses Kind gibt es nicht.');
  });

  it('formatDay: Tag aus dem Zeitstempel, leer ohne', () => {
    expect(formatDay('2026-09-30T10:00:00Z')).toBe('30.09.2026');
    expect(formatDay(null)).toBe('');
    expect(formatDay('kaputt')).toBe('');
  });
});
