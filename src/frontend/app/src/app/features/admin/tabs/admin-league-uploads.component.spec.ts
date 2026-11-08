import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { AdminLeagueUploadsComponent, LeagueUploadBatch, leagueHubScanUrl } from './admin-league-uploads.component';
import { SnackbarService } from '../../../core/snackbar.service';
import { ConfirmService } from '../../../shared/confirm-dialog/confirm-dialog.component';
import { of } from 'rxjs';

describe('AdminLeagueUploadsComponent', () => {
  let fixture: ComponentFixture<AdminLeagueUploadsComponent>;
  let http: HttpTestingController;
  let asked: string[];
  let answer: boolean;
  let snack: jasmine.SpyObj<SnackbarService>;

  beforeEach(() => {
    asked = [];
    answer = true;
    snack = jasmine.createSpyObj('SnackbarService', ['success', 'warn']);
    TestBed.configureTestingModule({
      imports: [AdminLeagueUploadsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: SnackbarService, useValue: snack },
        { provide: ConfirmService, useValue: { ask: (key: string) => { asked.push(key); return of(answer); } } }],
    });
    fixture = TestBed.createComponent(AdminLeagueUploadsComponent);
    http = TestBed.inject(HttpTestingController);
  });

  it('listet die Stapel, anonym als Teilen-Link, und löscht nach Rückfrage', async () => {
    fixture.detectChanges();
    http.expectOne('/api/admin/league-uploads').flush([
      { id: 3, createdAt: '2026-10-04T18:00:00Z', finishedAt: '2026-10-04T18:05:00Z', user: null, viaShare: true, files: 12, bytes: 3 * 1048576, comment: 'Runde 3' },
      { id: 2, createdAt: '2026-10-04T17:00:00Z', finishedAt: null, user: 'mitglied', viaShare: false, files: 1, bytes: 1048576, comment: null },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();
    const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr'));
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('admin.uploads.viaShare');
    expect(rows[0].textContent).toContain('3.0 MB');
    expect(rows[1].textContent).toContain('mitglied');
    expect(rows[1].textContent).toContain('admin.uploads.open');

    const p = fixture.componentInstance.remove(fixture.componentInstance.rows()![0]);
    await Promise.resolve();
    const req = http.expectOne('/api/admin/league-uploads/3');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
    await p;
    expect(fixture.componentInstance.rows()!.map(r => r.id)).toEqual([2]);
  });

  const batch: LeagueUploadBatch = {
    id: 3, createdAt: '2026-11-08T10:00:00Z', finishedAt: '2026-11-08T10:01:00Z', user: 'kahalm', viaShare: false, files: 2,
    bytes: 1048576, comment: 'Blatt 1+2', downloadName: 'kahalm-2026-11-08', readQueuedAt: null, readScanIds: null,
  };

  async function loaded(rows: LeagueUploadBatch[] = [batch]): Promise<void> {
    fixture.detectChanges();
    http.expectOne('/api/admin/league-uploads').flush(rows);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('Einlesen als eine Partie: fragt nach, schickt die Art, vermerkt die Einlesungen in der Zeile mit Link', async () => {
    await loaded();
    const c = fixture.componentInstance;
    const p = c.read(c.rows()![0], 'one');
    await Promise.resolve();
    const req = http.expectOne('/api/admin/league-uploads/3/read');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ mode: 'one' });
    req.flush({ scanIds: [812], skipped: 0, queuedAt: '2026-11-08T12:00:00Z' });
    await p;
    expect(asked).toEqual(['admin.uploads.readConfirmOne']);
    expect(c.rows()![0].readScanIds).toEqual([812]);
    expect(snack.success).toHaveBeenCalled();
    fixture.detectChanges();
    const link = (fixture.nativeElement as HTMLElement).querySelector('.read-mark a') as HTMLAnchorElement;
    expect(link.textContent).toContain('#812');
    expect(link.href).toContain('/verein/formular/812');
  });

  it('schon eingelesen (409): zweite Rückfrage, dann mit force; abgelehnt → kein zweiter Aufruf', async () => {
    await loaded();
    const c = fixture.componentInstance;
    let p = c.read(c.rows()![0], 'each');
    await Promise.resolve();
    http.expectOne('/api/admin/league-uploads/3/read').flush(
      { reason: 'alreadyQueued', queuedAt: '2026-11-08T11:00:00Z', scanIds: [800] }, { status: 409, statusText: 'Conflict' });
    await new Promise(r => setTimeout(r));
    const forced = http.expectOne('/api/admin/league-uploads/3/read');
    expect(forced.request.body).toEqual({ mode: 'each', force: true });
    forced.flush({ scanIds: [813, 814], skipped: 1, queuedAt: '2026-11-08T12:00:00Z' });
    await p;
    expect(asked).toEqual(['admin.uploads.readConfirmEach', 'admin.uploads.readAgain']);
    expect(c.rows()![0].readScanIds).toEqual([813, 814]);

    asked = [];
    p = c.read(c.rows()![0], 'each');
    await Promise.resolve();
    http.expectOne('/api/admin/league-uploads/3/read').flush(
      { reason: 'alreadyQueued', queuedAt: '2026-11-08T12:00:00Z', scanIds: [813, 814] }, { status: 409, statusText: 'Conflict' });
    answer = false;
    await p;
    http.expectNone('/api/admin/league-uploads/3/read');
    expect(asked.length).toBe(2);
  });

  it('meldet den Grund einer Absage', async () => {
    await loaded();
    const c = fixture.componentInstance;
    const p = c.read(c.rows()![0], 'one');
    await Promise.resolve();
    http.expectOne('/api/admin/league-uploads/3/read').flush({ reason: 'tooManyPages' }, { status: 400, statusText: 'Bad Request' });
    await p;
    expect(snack.warn).toHaveBeenCalledWith('admin.uploads.readErrors.tooManyPages');
  });

  it('„Eine Partie" ist bei mehr als drei Bildern gesperrt, mit Grund', async () => {
    await loaded([{ ...batch, files: 4 }]);
    (fixture.nativeElement.querySelector('td.acts button') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    const items = Array.from(document.querySelectorAll('.mat-mdc-menu-item')) as HTMLButtonElement[];
    expect(items.length).toBe(2);
    expect(items[0].disabled).toBeTrue();
    expect(items[0].textContent).toContain('admin.uploads.readOneTooMany');
    expect(items[1].disabled).toBeFalse();
  });

  // Wunsch 2026-11-08: „da wär auch ein Erledigt-Knopf gut (markiert als erledigt)"
  it('„Erledigt" markiert ohne Rückfrage, dämpft die Zeile; „Wieder öffnen" nimmt es zurück', async () => {
    fixture.detectChanges();
    http.expectOne('/api/admin/league-uploads').flush([
      { id: 3, createdAt: '2026-11-08T18:16:00Z', finishedAt: '2026-11-08T18:16:01Z', user: 'claude-prod', viaShare: false, files: 2, bytes: 700000, comment: null },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('.done-btn') as HTMLButtonElement).click();
    const put = http.expectOne('/api/admin/league-uploads/3/done');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ done: true });
    put.flush({ doneAt: '2026-11-08T19:00:00Z' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(asked).toEqual([]);
    expect(el.querySelector('tbody tr')!.classList).toContain('done');
    expect(el.querySelector('.done-mark')).not.toBeNull();
    expect(el.querySelector('.done-btn')!.textContent).toContain('admin.uploads.reopen');

    (el.querySelector('.done-btn') as HTMLButtonElement).click();
    const back = http.expectOne('/api/admin/league-uploads/3/done');
    expect(back.request.body).toEqual({ done: false });
    back.flush({ doneAt: null });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('tbody tr')!.classList).not.toContain('done');
    expect(el.querySelector('.done-mark')).toBeNull();
  });

  it('LeagueHub-Adresse der Einlesung aus der RookHub-Adresse', () => {
    expect(leagueHubScanUrl(5, 'https://rookhub.oberschmid.homes')).toBe('https://leaguehub.oberschmid.homes/verein/formular/5');
    expect(leagueHubScanUrl(5, 'http://rookhub-dev.oberschmid.homes')).toBe('http://leaguehub-dev.oberschmid.homes/verein/formular/5');
  });
});
