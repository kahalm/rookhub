import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { LeagueApiService } from '../core/league-api.service';
import { Account } from '../core/league.models';
import { OnlineAccountsComponent, accountErrorText, accountStatus, scanNoteText, siteLabel } from './online-accounts.component';

const SURE: Account = { id: 7, site: 'lichess', user: 'patrik', url: 'https://lichess.org/@/patrik', conf: 'sicher',
  comment: 'Profil nennt den Klarnamen', games: 1234, syncedAt: '2026-09-30T08:00:00Z', error: null };
const UNSURE: Account = { id: 8, site: 'chess.com', user: 'pat_o', url: 'https://www.chess.com/member/pat_o', conf: 'wahrscheinlich',
  comment: null, games: 0, syncedAt: null, error: null };

describe('OnlineAccountsComponent', () => {
  let fixture: ComponentFixture<OnlineAccountsComponent>;
  let api: jasmine.SpyObj<LeagueApiService>;
  let changed: number;
  const el = () => fixture.nativeElement as HTMLElement;
  const button = (text: string) => Array.from(el().querySelectorAll<HTMLButtonElement>('button')).find(b => b.textContent?.trim() === text);

  function render(accounts: Account[], canEdit: boolean): void {
    fixture.componentRef.setInput('fide', '1606921');
    fixture.componentRef.setInput('accounts', accounts);
    fixture.componentRef.setInput('canEdit', canEdit);
    fixture.detectChanges();
  }

  beforeEach(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService',
      ['addAccount', 'updateAccount', 'deleteAccount', 'syncAccount', 'playerSuggestions', 'scanSuggestions', 'acceptSuggestion', 'rejectSuggestion']);
    api.playerSuggestions.and.resolveTo({ items: [] });
    TestBed.configureTestingModule({ imports: [OnlineAccountsComponent], providers: [{ provide: LeagueApiService, useValue: api }] });
    fixture = TestBed.createComponent(OnlineAccountsComponent);
    changed = 0;
    fixture.componentInstance.changed.subscribe(() => changed++);
  });

  it('Hilfen: Seitenname, Absagen, Stand des Abrufs', () => {
    expect(siteLabel('lichess')).toBe('Lichess');
    expect(siteLabel('chess.com')).toBe('chess.com');
    expect(siteLabel('xyz')).toBe('xyz');
    expect(accountErrorText('duplicate')).toContain('schon da');
    expect(accountErrorText('invalidUser')).toContain('kein gültiger');
    expect(accountErrorText(undefined)).toContain('nicht geklappt');
    expect(accountStatus({ ...SURE, id: undefined })).toBeNull();                      // Teilen-Link: kein Stand
    expect(accountStatus(SURE)).toBe('1.234 Online-Partien geholt (Stand 30.09.).');
    expect(accountStatus({ ...SURE, games: 1 })).toContain('1 Online-Partie geholt');
    expect(accountStatus(UNSURE)).toBe('Partien werden geholt …');
    expect(accountStatus({ ...UNSURE, error: 'Konto nicht gefunden' })).toBe('Partien nicht geholt: Konto nicht gefunden.');
  });

  it('zeigt gesichert/unsicher und den Kommentar; ohne Recht keine Knöpfe', () => {
    render([SURE, UNSURE], false);
    const rows = el().querySelectorAll('.acc-list li');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Lichess: patrik');
    expect(rows[0].querySelector('.tag-sure')?.textContent).toContain('gesichert');
    expect(rows[0].querySelector('.acc-comment')?.textContent).toContain('Klarnamen');
    expect(rows[1].querySelector('.tag')?.textContent).toContain('unsicher');
    expect(rows[1].querySelector('.tag-sure')).toBeNull();
    expect(button('Bearbeiten')).toBeUndefined();
    expect(el().querySelector('.acc-add')).toBeNull();
  });

  it('Konto hinzufügen: Seite, Name, gesichert, Kommentar — danach „changed"', async () => {
    api.addAccount.and.resolveTo(SURE);
    render([], true);
    expect(el().textContent).toContain('Noch kein Online-Konto');
    (el().querySelector('.acc-add') as HTMLButtonElement).click();
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.formUser.set('https://lichess.org/@/patrik');
    c.formComment.set('  Profil nennt den Klarnamen ');
    button('gesichert')!.click();
    fixture.detectChanges();
    await c.save();
    expect(api.addAccount).toHaveBeenCalledWith('1606921',
      { site: 'lichess', user: 'https://lichess.org/@/patrik', sure: true, comment: '  Profil nennt den Klarnamen ' });
    expect(changed).toBe(1);
    expect(c.adding()).toBeFalse();
  });

  it('Bearbeiten füllt das Formular vor und schickt die Änderung an dieses Konto', async () => {
    api.updateAccount.and.resolveTo({ ...UNSURE, conf: 'sicher' });
    render([SURE, UNSURE], true);
    const edit = Array.from(el().querySelectorAll<HTMLButtonElement>('.acc-list li'))[1].querySelector('button') as HTMLButtonElement;
    edit.click();
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.editing()).toBe(8);
    expect(c.formSite()).toBe('chess.com');
    expect(c.formUser()).toBe('pat_o');
    expect(c.formSure()).toBeFalse();
    expect(el().querySelector('.acc-form')).not.toBeNull();
    c.formSure.set(true);
    await c.save();
    expect(api.updateAccount).toHaveBeenCalledWith(8, { site: 'chess.com', user: 'pat_o', sure: true, comment: '' });
    expect(c.editing()).toBeNull();
    expect(changed).toBe(1);
  });

  it('Absage des Servers steht als Satz da, das Formular bleibt offen', async () => {
    api.addAccount.and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'duplicate' } }));
    render([SURE], true);
    const c = fixture.componentInstance;
    c.startAdd();
    c.formUser.set('patrik');
    await c.save();
    fixture.detectChanges();
    expect(el().querySelector('.update-msg')?.textContent).toContain('schon da');
    expect(c.adding()).toBeTrue();
    expect(changed).toBe(0);
  });

  it('Entfernen fragt nach; „Nochmal holen" nur bei einem Fehler', async () => {
    api.deleteAccount.and.resolveTo();
    api.syncAccount.and.resolveTo({ ...UNSURE, error: null });
    render([SURE, { ...UNSURE, error: 'Konto nicht gefunden' }], true);
    expect(Array.from(el().querySelectorAll('.acc-status')).map(p => !!p.querySelector('button'))).toEqual([false, true]);
    spyOn(window, 'confirm').and.returnValues(false, true);
    const c = fixture.componentInstance;
    await c.remove(SURE);
    expect(api.deleteAccount).not.toHaveBeenCalled();
    await c.remove(SURE);
    expect(api.deleteAccount).toHaveBeenCalledWith(7);
    await c.resync({ ...UNSURE });
    expect(api.syncAccount).toHaveBeenCalledWith(8);
    expect(changed).toBe(2);
  });

  it('Vorschläge der Konto-Suche: nur mit Recht geladen, „Jetzt suchen" sagt, was herauskam (0.607.0)', async () => {
    const sugg = { id: 3, fide: '1606921', site: 'lichess', user: 'PatrikOberschmid', url: 'https://lichess.org/@/PatrikOberschmid',
      score: 5, evidence: 'Nutzername aus dem Namen; Klarname im Profil', profileName: 'Patrik Oberschmid', location: 'Schwaz', lastActive: null };
    render([], false);
    await fixture.whenStable();
    expect(api.playerSuggestions).not.toHaveBeenCalled();
    expect(el().querySelector('.sugg')).toBeNull();

    api.playerSuggestions.and.resolveTo({ items: [sugg] });
    render([], true);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.playerSuggestions).toHaveBeenCalledWith('1606921');
    expect(el().querySelector('.sugg-list')?.textContent).toContain('Lichess: PatrikOberschmid');

    api.scanSuggestions.and.resolveTo({ items: [sugg], found: 0, skipped: null });
    await fixture.componentInstance.scan();
    fixture.detectChanges();
    expect(el().querySelector('.sugg')?.textContent).toContain('Nichts Neues gefunden.');

    api.acceptSuggestion.and.resolveTo(SURE);
    (Array.from(el().querySelectorAll<HTMLButtonElement>('.sugg-actions button')).find(b => b.textContent?.includes('gesichert')))!.click();
    await fixture.whenStable();
    expect(api.acceptSuggestion).toHaveBeenCalledWith(3, true);
    expect(changed).toBe(1);                                                        // Karte lädt neu, das Konto steht dann da
  });

  it('„Jetzt suchen": Absagen als Satz', async () => {
    expect(scanNoteText({ items: [], found: 2 })).toBe('2 neue Vorschläge.');
    expect(scanNoteText({ items: [], found: 1 })).toBe('1 neuer Vorschlag.');
    expect(scanNoteText({ items: [], skipped: 'minderjährig' })).toBe('Nicht gesucht: minderjährig.');
    api.scanSuggestions.and.rejectWith(new HttpErrorResponse({ status: 503, error: { reason: 'rateLimited' } }));
    render([], true);
    await fixture.componentInstance.scan();
    expect(fixture.componentInstance.scanNote()).toContain('bremst gerade');
    expect(fixture.componentInstance.scanning()).toBeFalse();
  });
});
