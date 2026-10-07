import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { LeagueApiService } from '../../core/league-api.service';
import { ClubContextService } from '../../core/club-context.service';
import { provideTestClub } from '../../core/club-context.testing';
import { AdminClub, RhGroup } from '../../core/league.models';
import { ClubsPageComponent, clubDate, clubErrorText, clubRegionText } from './clubs-page.component';

const HOME: AdminClub = {
  id: 1, name: 'SK Testdorf', anonName: 'Testdorf', teamPrefix: 'Testdorf', region: 'tirol', createdAt: '2026-10-07T12:00:00Z',
  clubGames: 42, groups: [{ id: 10, name: 'Testdorf', members: 7 }],
};
const OTHER: AdminClub = {
  id: 2, name: 'SK Weiler', anonName: 'Weiler', teamPrefix: 'SK Weiler', region: 'bayern', createdAt: '2026-10-07T12:00:00Z',
  clubGames: 0, groups: [{ id: 11, name: 'Weiler Mannschaft', members: 1 }],
};
const GROUPS: RhGroup[] = [
  { id: 1, name: 'Everyone', memberCount: 300, isEveryone: true },
  { id: 10, name: 'Testdorf', memberCount: 7, isEveryone: false },
  { id: 11, name: 'Weiler Mannschaft', memberCount: 1, isEveryone: false },
  { id: 12, name: 'Weiler Jugend', memberCount: 4, isEveryone: false },
];

describe('ClubsPageComponent (Vereine verwalten, 0.700.0)', () => {
  let fixture: ComponentFixture<ClubsPageComponent>;
  let api: jasmine.SpyObj<LeagueApiService>;
  let admin: boolean;
  let perms: Set<string>;
  let confirmAsk: jasmine.Spy;
  let reload: jasmine.Spy;
  const el = () => fixture.nativeElement as HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({
      imports: [ClubsPageComponent],
      providers: [
        provideTestClub(),
        { provide: LeagueApiService, useValue: api },
        { provide: AuthService, useValue: { get isAdmin() { return admin; }, has: (p: string) => perms.has(p) } },
        { provide: ConfirmService, useValue: { ask: (...a: unknown[]) => confirmAsk(...a) } },
      ],
    });
    reload = spyOn(TestBed.inject(ClubContextService), 'reload').and.returnValue(of(1));
    fixture = TestBed.createComponent(ClubsPageComponent);
    fixture.detectChanges();
    await settle();
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function input(name: string, value: string): void {
    const i = el().querySelector(`[name="${name}"]`) as HTMLInputElement | HTMLSelectElement;
    i.value = value;
    i.dispatchEvent(new Event(i instanceof HTMLSelectElement ? 'change' : 'input'));
    fixture.detectChanges();
  }

  function button(text: string, root: ParentNode = el()): HTMLButtonElement {
    const b = Array.from(root.querySelectorAll('button')).find(x => x.textContent!.trim() === text);
    if (!b) throw new Error(`Knopf „${text}" fehlt`);
    return b as HTMLButtonElement;
  }

  beforeEach(() => {
    admin = true;
    perms = new Set(['league.manage']);
    confirmAsk = jasmine.createSpy('ask').and.returnValue(of(true));
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService',
      ['adminClubs', 'createClub', 'updateClub', 'addClubGroup', 'removeClubGroup', 'groups']);
    api.adminClubs.and.resolveTo([HOME, OTHER]);
    api.groups.and.resolveTo(GROUPS);
  });

  it('Hilfen: Region, Datum, Absagen als Klartext', () => {
    expect(clubRegionText(null)).toBe('Tirol (chess-results)');
    expect(clubRegionText('tirol')).toBe('Tirol (chess-results)');
    expect(clubRegionText('bayern')).toBe('Bayern (Ligamanager + Schachkreis Zugspitze)');
    expect(clubErrorText('invalidRegion')).toContain('Region');
    expect(clubDate('2026-10-07T12:00:00Z')).toBe('07.10.2026');
    expect(clubDate(null)).toBe('—');
    expect(clubErrorText('invalidTeamPrefix')).toContain('Mannschafts-Präfix');
    expect(clubErrorText('duplicate')).toContain('gibt es schon');
    expect(clubErrorText('everyone')).toContain('Everyone');
    expect(clubErrorText(undefined, 403)).toContain('league.manage');
  });

  it('ohne Recht nur die Sperrkarte — auch league.manage ohne Admin', async () => {
    admin = false;
    await create();
    expect(el().textContent).toContain('Nicht freigeschaltet');
    expect(api.adminClubs).not.toHaveBeenCalled();
    expect(el().querySelector('.clubs-tbl')).toBeNull();
  });

  it('listet die Vereine mit Region, Gruppen, Partien und Datum', async () => {
    await create();
    const rows = el().querySelectorAll('.clubs-tbl tbody tr');
    expect(rows.length).toBe(2);
    const first = rows[0].textContent!;
    expect(first).toContain('SK Testdorf');
    expect(first).toContain('Tirol (chess-results)');
    expect(first).toContain('42');
    expect(first).toContain('07.10.2026');
    expect(rows[1].textContent).toContain('Bayern (Ligamanager + Schachkreis Zugspitze)');
    expect(el().querySelector('.clubs-help')!.textContent).toContain('keiner Vereinsgruppe');
  });

  it('Anlegen schickt den richtigen Rumpf und frischt den Vereins-Kontext auf', async () => {
    const created: AdminClub = { id: 3, name: 'SK Neu', anonName: 'Neu', teamPrefix: 'SK Neu', region: 'bayern', createdAt: null, clubGames: 0, groups: [] };
    api.createClub.and.resolveTo(created);
    await create();
    button('Neuer Verein').click();
    fixture.detectChanges();
    expect(button('Verein anlegen').disabled).toBeTrue();             // leer: nicht speicherbar
    input('name', '  SK Neu ');
    input('teamPrefix', 'SK Neu');
    input('anonName', 'Neu');
    input('region', 'bayern');
    api.adminClubs.and.resolveTo([HOME, OTHER, created]);
    button('Verein anlegen').click();
    await settle();
    expect(api.createClub).toHaveBeenCalledWith({ name: 'SK Neu', teamPrefix: 'SK Neu', anonName: 'Neu', region: 'bayern' });
    expect(reload).toHaveBeenCalled();
    expect(el().querySelector('.update-msg')!.textContent).toContain('„SK Neu" angelegt');
    // gleich danach: der neue Verein ist offen, mit dem Gruppen-Teil
    expect(el().querySelector('.club-edit h3')!.textContent).toContain('SK Neu');
    expect(el().querySelector('.club-groups')).not.toBeNull();
  });

  it('400 mit reason erscheint als Klartext am Formular', async () => {
    api.createClub.and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'invalidAnonName' } }));
    await create();
    button('Neuer Verein').click();
    fixture.detectChanges();
    input('name', 'SK Neu');
    input('teamPrefix', 'Neu');
    input('anonName', 'x');
    button('Verein anlegen').click();
    await settle();
    expect(el().querySelector('.club-edit .err')!.textContent).toContain('Anzeigename anonymisierter Spieler');
    expect(reload).not.toHaveBeenCalled();
  });

  it('Region ändern fragt nach; „nein" schickt nichts, sonst PUT mit allen Feldern', async () => {
    api.updateClub.and.resolveTo({ ...HOME, region: 'bayern' });
    await create();
    button('Bearbeiten', el().querySelectorAll('.clubs-tbl tbody tr')[0]).click();
    fixture.detectChanges();
    input('region', 'bayern');
    confirmAsk.and.returnValue(of(false));
    button('Speichern').click();
    await settle();
    expect(confirmAsk).toHaveBeenCalled();
    expect(api.updateClub).not.toHaveBeenCalled();
    confirmAsk.and.returnValue(of(true));
    button('Speichern').click();
    await settle();
    expect(api.updateClub).toHaveBeenCalledWith(1, { name: 'SK Testdorf', teamPrefix: 'Testdorf', anonName: 'Testdorf', region: 'bayern' });
  });

  it('Name ändern ohne Regionswechsel: keine Rückfrage', async () => {
    api.updateClub.and.resolveTo({ ...HOME, name: 'SK Testdorf 1920' });
    await create();
    button('Bearbeiten', el().querySelectorAll('.clubs-tbl tbody tr')[0]).click();
    fixture.detectChanges();
    input('name', 'SK Testdorf 1920');
    button('Speichern').click();
    await settle();
    expect(confirmAsk).not.toHaveBeenCalled();
    expect(api.updateClub).toHaveBeenCalledWith(1, jasmine.objectContaining({ name: 'SK Testdorf 1920', region: 'tirol' }));
  });

  it('Gruppe zuordnen: Everyone und eigene fehlen, Gruppen fremder Vereine sind gesperrt', async () => {
    api.addClubGroup.and.resolveTo(null);
    await create();
    button('Bearbeiten', el().querySelectorAll('.clubs-tbl tbody tr')[0]).click();
    fixture.detectChanges();
    const search = el().querySelector('.group-search input') as HTMLInputElement;
    search.value = 'e';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const hits = Array.from(el().querySelectorAll('.group-hits li'));
    const names = hits.map(h => h.textContent!);
    expect(names.some(n => n.includes('Everyone'))).toBeFalse();
    expect(names.some(n => n.includes('Testdorf'))).toBeFalse();      // schon zugeordnet
    const foreign = hits.find(h => h.textContent!.includes('Weiler Mannschaft'))!;
    expect(foreign.textContent).toContain('gehört schon zu SK Weiler');
    expect(button('Zuordnen', foreign).disabled).toBeTrue();
    const free = hits.find(h => h.textContent!.includes('Weiler Jugend'))!;
    expect(button('Zuordnen', free).disabled).toBeFalse();
    button('Zuordnen', free).click();
    await settle();
    expect(api.addClubGroup).toHaveBeenCalledWith(1, 12);
    expect(reload).toHaveBeenCalled();
    expect(el().querySelector('.update-msg')!.textContent).toContain('Weiler Jugend');
  });

  it('Gruppe entfernen fragt nach; „nein" lässt sie stehen', async () => {
    api.removeClubGroup.and.resolveTo(null);
    await create();
    button('Bearbeiten', el().querySelectorAll('.clubs-tbl tbody tr')[0]).click();
    fixture.detectChanges();
    confirmAsk.and.returnValue(of(false));
    button('Entfernen').click();
    await settle();
    expect(confirmAsk.calls.mostRecent().args[0]).toContain('Testdorf');
    expect(api.removeClubGroup).not.toHaveBeenCalled();
    confirmAsk.and.returnValue(of(true));
    button('Entfernen').click();
    await settle();
    expect(api.removeClubGroup).toHaveBeenCalledWith(1, 10);
    expect(reload).toHaveBeenCalled();
  });

  /** Wie `touch-targets.spec`: der Karma-Browser ist 1400 px breit, die Handy-Regeln (`max-width: 640px`) griffen nie — deshalb
   *  die gerenderte Seite in einem 390 px breiten iframe mit denselben globalen Styles messen. */
  it('am Handy (390 px) kein waagrechter Rollbalken — Liste und offener Verein', async () => {
    await create();
    button('Bearbeiten', el().querySelectorAll('.clubs-tbl tbody tr')[0]).click();
    fixture.detectChanges();
    const search = el().querySelector('.group-search input') as HTMLInputElement;
    search.value = 'Weiler';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const styles = Array.from(document.querySelectorAll('link[rel="stylesheet"], style')).map(n =>
      n instanceof HTMLLinkElement ? `<link rel="stylesheet" href="${n.href}">` : `<style>${n.textContent}</style>`).join('\n');
    const frame = document.createElement('iframe');
    frame.style.width = '390px';
    frame.style.height = '900px';
    document.body.appendChild(frame);
    try {
      const loaded = new Promise(r => frame.addEventListener('load', r, { once: true }));
      const doc = frame.contentDocument!;
      doc.open();
      doc.write(`<!doctype html><html><head><meta name="viewport" content="width=device-width">${styles}</head>` +
        `<body><main class="wrap">${el().innerHTML}</main></body></html>`);
      doc.close();
      await loaded;
      expect(doc.querySelector('.clubs-tbl th.hide-s') && getComputedStyle(doc.querySelector('.clubs-tbl th.hide-s')!).display).toBe('none');
      expect(doc.documentElement.scrollWidth).toBeLessThanOrEqual(390);
    } finally {
      frame.remove();
    }
  });
});
